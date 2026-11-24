using System;
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using Unity.Multiplayer.PlayMode;
#endif

// Only put this component in Bootstrap. Clients authenticate before entering the game session.
public class MultiplayerBootstrap : MonoBehaviour
{
    [SerializeField] private string serverScene = WorldSceneCoordinator.SessionName;
    [SerializeField] private string clientScene = "Login Scene";
    private static MultiplayerBootstrap active;
#if UNITY_EDITOR
    private bool startingScenarioServer;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => active = null;

    private IEnumerator Start()
    {
        if (active != null && active != this) { Destroy(gameObject); yield break; }
        active = this;
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;

        bool server = IsServerRole();
        try
        {
            yield return StartInstance(server);
        }
        finally
        {
            ReleasePendingStartup();
            Destroy(gameObject);
        }
    }

    private IEnumerator StartInstance(bool server)
    {
#if UNITY_EDITOR
        startingScenarioServer = EditorScenarioSession.IsScenarioServer;
#endif
        string destination = server ? serverScene : clientScene;
        if (!Application.CanStreamedLevelBeLoaded(destination))
        {
            Debug.LogError($"Bootstrap: Scene List에 {destination} 씬을 추가해 주세요.", this);
            yield break;
        }

#if UNITY_EDITOR
        if (startingScenarioServer || EditorScenarioSession.IsScenarioClient)
        {
            string error = "에디터 서버 준비가 완료되기 전에 중단됐습니다.";
            if (startingScenarioServer)
                yield return EditorScenarioSession.PrepareServer(message => error = message);
            else
                yield return EditorScenarioSession.WaitForServer(message => error = message);
            if (error != null)
            {
                Debug.LogError($"Bootstrap: {error}", this);
                yield break;
            }
        }
#endif
        Debug.Log($"Bootstrap: {(server ? "Server" : "Client")} → {destination}");
        yield return SceneManager.LoadSceneAsync(destination, LoadSceneMode.Single);

        // Clients stay at the login UI. LoginSession connects only after successful login.
        if (!server) yield break;
        var manager = NetworkManager.Singleton;
        if (!manager || manager.IsListening)
        {
            Debug.LogError(manager ? "Bootstrap: NetworkManager가 이미 실행 중입니다."
                : "Bootstrap: 게임 씬에 NetworkManager가 없습니다.", this);
            yield break;
        }
        var world = FindFirstObjectByType<WorldSceneCoordinator>();
        if (world)
        {
            yield return world.PrepareServer();
            if (!world.ServerMapsReady)
            {
                Debug.LogError("Bootstrap: 게임 지역을 준비하지 못했습니다.", this);
                yield break;
            }
        }

        try
        {
            // Set up approval before listening, regardless of scene callback ordering.
            if (!manager.TryGetComponent<GameServerAuthentication>(out _))
                manager.gameObject.AddComponent<GameServerAuthentication>();
            ConfigureTransport(manager);
            if (!manager.StartServer())
            {
                Debug.LogError("Bootstrap: 서버 시작 실패. 주소와 포트 사용 여부를 확인해 주세요.", this);
                yield break;
            }
            if (world) WorldSceneCoordinator.ConfigureServerSync(manager);
#if UNITY_EDITOR
            if (startingScenarioServer)
            {
                EditorScenarioSession.PublishReady();
                startingScenarioServer = false;
            }
#endif
            Debug.Log("Bootstrap: 게임 서버가 접속을 기다립니다.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Bootstrap: 게임 서버를 시작하지 못했습니다. {ex.Message}", this);
            if (manager.IsListening) manager.Shutdown();
        }
    }

    private static void ConfigureTransport(NetworkManager manager)
    {
#if UNITY_EDITOR
        if (EditorScenarioSession.IsScenarioServer)
        {
            manager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", EditorScenarioSession.GamePort, "127.0.0.1");
            return;
        }
#endif
        if (ushort.TryParse(Environment.GetEnvironmentVariable("NEO_GAME_PORT"), out var port))
            manager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", port, "0.0.0.0");
    }

    public static bool IsServerRole()
    {
#if UNITY_EDITOR
        if (EditorScenarioSession.IsScenarioServer) return true;
        if (EditorScenarioSession.IsScenarioClient) return false;
#endif
#if UNITY_SERVER
        return true;
#else
        if (Environment.GetEnvironmentVariable("NEO_DEDICATED_SERVER") == "1") return true;
#if UNITY_EDITOR
        bool server = false;
        bool client = false;
        foreach (string tag in CurrentPlayer.Tags)
        {
            server |= string.Equals(tag, "Server", StringComparison.OrdinalIgnoreCase);
            client |= string.Equals(tag, "Client", StringComparison.OrdinalIgnoreCase);
        }
        if (server && client)
            throw new InvalidOperationException("Bootstrap: Server와 Client 태그를 동시에 지정할 수 없습니다.");
        if (server || client)
            return server;
#endif
        // A normal standalone client or an untagged Editor starts at the login screen.
        return false;
#endif
    }

    private void OnDestroy()
    {
        ReleasePendingStartup();
        if (active == this)
            active = null;
    }

    private void ReleasePendingStartup()
    {
#if UNITY_EDITOR
        if (startingScenarioServer)
        {
            startingScenarioServer = false;
            EditorScenarioSession.Stop();
        }
#endif
    }
}
