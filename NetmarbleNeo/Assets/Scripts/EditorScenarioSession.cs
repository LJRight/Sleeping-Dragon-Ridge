#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

// The main Editor owns the Python child. Virtual players receive only the published endpoints.
internal static partial class EditorScenarioSession
{
    [Serializable]
    private sealed class Endpoints
    {
        public string editorSession;
        public int authPort, gamePort;
    }

    internal static string ServerKey { get; private set; }
    internal static ushort GamePort { get; private set; }
    internal static string AuthUrl { get; private set; }

    private static Process child;
    private static Endpoints endpoints;
    private static string ownedReadyFile;
    private static bool published;

    static EditorScenarioSession()
    {
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
        EditorApplication.quitting += Stop;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingPlayMode) Stop();
        };
    }

    private static DirectoryInfo MainProject()
    {
        var project = Directory.GetParent(Application.dataPath);
        // MPPM virtual projects are stored in <main>/Library/VP/<instance>.
        return project.Parent?.Name == "VP" && project.Parent.Parent?.Name == "Library"
            ? project.Parent.Parent.Parent : project;
    }

    private static string ReadyFile(DirectoryInfo project) =>
        Path.Combine(project.FullName, "Library", "NeoEditorSession.json");

    private static string Python()
    {
        foreach (var path in new[] { "/opt/homebrew/bin/python3", "/usr/local/bin/python3" })
            if (File.Exists(path)) return path;
        return Application.platform == RuntimePlatform.WindowsEditor ? "python" : "python3";
    }

    internal static IEnumerator PrepareServer(Action<string> done)
    {
        Stop();
        string error = null;
        Task<string> portLine = null, errors = null;
        try
        {
            var project = MainProject();
            string backend = Path.Combine(project.Parent.FullName, "localServer");
            if (!File.Exists(Path.Combine(backend, "server.py")))
                throw new FileNotFoundException("로그인 서버 파일을 찾을 수 없습니다.", Path.Combine(backend, "server.py"));

            ownedReadyFile = ReadyFile(project);
            File.Delete(ownedReadyFile);
            endpoints = new Endpoints { editorSession = Guid.NewGuid().ToString("N") };
            byte[] bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            ServerKey = Convert.ToBase64String(bytes);

            var start = new ProcessStartInfo(Python(), "-B -u server.py serve --host 127.0.0.1 --port 0 --editor-parent")
            {
                WorkingDirectory = backend,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.EnvironmentVariables["GAME_SERVER_KEY"] = ServerKey;
            start.EnvironmentVariables["NEO_EDITOR_SESSION"] = endpoints.editorSession;
            child = Process.Start(start);
            portLine = child.StandardOutput.ReadLineAsync();
            errors = child.StandardError.ReadToEndAsync();
        }
        catch (Exception ex) { error = ex.Message; }

        double deadline = EditorApplication.timeSinceStartup + 30;
        while (error == null && !portLine.IsCompleted && EditorApplication.timeSinceStartup < deadline)
            yield return null;

        if (error == null)
        {
            if (portLine.Status == TaskStatus.RanToCompletion && ushort.TryParse(portLine.Result, out var port) && port != 0)
            {
                endpoints.authPort = port;
                AuthUrl = $"http://127.0.0.1:{port}";
                // UTP chooses its own free port at bind time. PublishReady reads the actual port.
                GamePort = 0;
                EditorApplication.update += CheckChild;
            }
            else error = errors.Status == TaskStatus.RanToCompletion && !string.IsNullOrWhiteSpace(errors.Result)
                ? errors.Result.Trim() : "로그인 서버에서 수신 포트를 확인하지 못했습니다. Python 설치와 DB 경로를 확인하세요.";
        }
        if (error != null) Stop();
        done(error);
    }

    internal static void PublishReady()
    {
        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport;
        if (child == null || child.HasExited || !transport)
            throw new InvalidOperationException("에디터 로그인 서버 또는 게임 서버가 준비되지 않았습니다.");
        GamePort = transport.GetLocalEndpoint().Port;
        if (GamePort == 0) throw new InvalidOperationException("게임 서버의 실제 수신 포트를 확인하지 못했습니다.");
        endpoints.gamePort = GamePort;
        var temporary = ownedReadyFile + ".tmp";
        File.WriteAllText(temporary, JsonUtility.ToJson(endpoints));
        File.Move(temporary, ownedReadyFile);
        published = true;
    }

    internal static IEnumerator WaitForServer(Action<string> done)
    {
        string path = ReadyFile(MainProject());
        double deadline = EditorApplication.timeSinceStartup + 90;
        while (EditorApplication.timeSinceStartup < deadline)
        {
            var ready = ReadEndpoints(path);
            if (ready != null)
            {
                var url = $"http://127.0.0.1:{ready.authPort}";
                using var request = UnityWebRequest.Get(url + "/health");
                request.timeout = 2;
                yield return request.SendWebRequest();
                // A stale file after an Editor crash must not route a player to a reused port.
                if (request.result == UnityWebRequest.Result.Success &&
                    MatchesSession(request.downloadHandler.text, ready.editorSession))
                {
                    AuthUrl = url;
                    GamePort = (ushort)ready.gamePort;
                    done(null);
                    yield break;
                }
            }
            yield return new WaitForSecondsRealtime(.20f);
        }
        done("메인 에디터의 로그인·게임 서버를 기다리는 시간이 초과됐습니다. 메인 에디터 Console을 확인하세요.");
    }

    private static Endpoints ReadEndpoints(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var info = JsonUtility.FromJson<Endpoints>(File.ReadAllText(path));
            return info != null && Guid.TryParseExact(info.editorSession, "N", out _) &&
                info.authPort > 0 && info.authPort <= ushort.MaxValue &&
                info.gamePort > 0 && info.gamePort <= ushort.MaxValue ? info : null;
        }
        catch (IOException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private static bool MatchesSession(string response, string session)
    {
        try { return JsonUtility.FromJson<Endpoints>(response)?.editorSession == session; }
        catch (ArgumentException) { return false; }
    }

    private static void CheckChild()
    {
        if (!published || child == null || !child.HasExited) return;
        Debug.LogError($"에디터 로그인 서버가 종료됐습니다 (종료 코드 {child.ExitCode}).");
        Stop();
        EditorApplication.ExitPlaymode();
    }

    internal static void Stop()
    {
        EditorApplication.update -= CheckChild;
        published = false;
        if (ownedReadyFile != null)
        {
            try
            {
                File.Delete(ownedReadyFile);
                File.Delete(ownedReadyFile + ".tmp");
            }
            catch (IOException ex) { Debug.LogWarning(ex.Message); }
            ownedReadyFile = null;
        }
        if (child != null)
        {
            try
            {
                child.StandardInput.Close();
                if (!child.WaitForExit(1500)) child.Kill();
            }
            catch (InvalidOperationException) { /* Already exited. */ }
            finally { child.Dispose(); child = null; }
        }
        ServerKey = null;
        AuthUrl = null;
        GamePort = 0;
        endpoints = null;
    }
}
#endif
