using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class LoginClient : MonoBehaviour
{
    [SerializeField] private string baseUrl = "http://127.0.0.1:8080";
    [SerializeField] private string gameScene = WorldSceneCoordinator.SessionName;
    [SerializeField] private string gameServerAddress = "127.0.0.1";
    [SerializeField] private ushort gameServerPort = 7777;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button newGameButton;
    private string rejectedCredential;

    [Serializable] private class GuestRequest { public string guestToken; }
    [Serializable] private class ErrorResponse { public string error; }

    [Serializable] private class User { public string id; }
    [Serializable] private class Progress { public int level; public double experience; }
    [Serializable] private class LoginResponse
    {
        public Progress progress;
        public string guestToken;
        public string accessToken;
        public long expiresAt;
        public User user;
    }

    private void Awake()
    {
        if (!newGameButton) return;
        newGameButton.gameObject.SetActive(false);
        UiPrefab.Listen(newGameButton, StartNewGame);
    }

    private IEnumerator Start()
    {
        if (MultiplayerBootstrap.IsServerRole() || LocalCombatTest.InScene(gameObject.scene)) yield break;
#if !UNITY_EDITOR && (UNITY_IOS || UNITY_ANDROID)
        baseUrl = MobileServerEndpoint.LoginUrl;
        gameServerAddress = MobileServerEndpoint.Host;
        gameServerPort = MobileServerEndpoint.GamePort;
#else
        baseUrl = Environment.GetEnvironmentVariable("NEO_AUTH_URL") ?? baseUrl;
        gameServerAddress = Environment.GetEnvironmentVariable("NEO_GAME_ADDRESS") ?? gameServerAddress;
        if (ushort.TryParse(Environment.GetEnvironmentVariable("NEO_GAME_PORT"), out var port)) gameServerPort = port;
#endif
#if UNITY_EDITOR
        if (EditorScenarioSession.IsScenarioClient)
        {
            baseUrl = EditorScenarioSession.AuthUrl;
            gameServerAddress = "127.0.0.1";
            gameServerPort = EditorScenarioSession.GamePort;
        }
#endif
        var session = LoginSession.Instance;
        if (session == null || session.AutoLoginAttempted) yield break;
        session.AutoLoginAttempted = true;
        session.SetStatus("Connecting...");
        yield return new WaitForSecondsRealtime(1.4f);
        if (session.IsBusy) yield break;
        yield return LoginGuest(session);
    }

    private void StartNewGame()
    {
        var session = LoginSession.Instance;
        if (!session || session.IsBusy || rejectedCredential == null) return;
        StartCoroutine(LoginGuest(session, rejectedCredential));
    }

    private IEnumerator LoginGuest(LoginSession session, string replaceRejected = null)
    {
        rejectedCredential = null;
        if (newGameButton) newGameButton.gameObject.SetActive(false);
        string credential;
        try
        {
            credential = replaceRejected == null ? GuestCredentialStore.LoadOrCreate()
                : GuestCredentialStore.ReplaceRejected(replaceRejected);
        }
        catch (Exception)
        {
            session.SetStatus("Cannot access saved guest credentials. Check device storage and reopen the app.");
            yield break;
        }
        bool pending = credential.StartsWith("P:", StringComparison.Ordinal);
        session.SetStatus(pending ? "Creating guest account..." : "Restoring guest account...");
        string endpoint = pending ? "/auth/guest/create" : "/auth/guest/resume";
        string payload = JsonUtility.ToJson(new GuestRequest { guestToken = credential.Substring(2) });
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var request = JsonPost(endpoint, payload);
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                ConnectGuest(session, credential, request.downloadHandler.text);
                yield break;
            }
            if (!pending && request.responseCode == 401 && IsRejectedGuest(request.downloadHandler?.text))
            {
                rejectedCredential = credential;
                if (newGameButton)
                {
                    newGameButton.GetComponentInChildren<TMP_Text>(true).font = KnightUIArt.Font;
                    newGameButton.gameObject.SetActive(true);
                }
                if (statusText) statusText.font = KnightUIArt.Font;
                session.SetStatus("기존 게스트 계정을 복원할 수 없습니다.");
                yield break;
            }
            bool temporary = request.result == UnityWebRequest.Result.ConnectionError ||
                request.responseCode == 408 || request.responseCode >= 500;
            if (attempt == 0 && temporary)
            {
                session.SetStatus("Connection interrupted. Retrying...");
                yield return new WaitForSecondsRealtime(2f);
                continue;
            }
            session.SetStatus("Cannot connect. Check the server and reopen the app to retry.");
            yield break;
        }
    }

    private static bool IsRejectedGuest(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try { return JsonUtility.FromJson<ErrorResponse>(json)?.error == "GUEST_CREDENTIAL_INVALID"; }
        catch (ArgumentException) { return false; }
    }

    private void ConnectGuest(LoginSession session, string credential, string json)
    {
        LoginResponse response;
        try
        {
            response = JsonUtility.FromJson<LoginResponse>(json);
            if (string.IsNullOrEmpty(response?.user?.id) || string.IsNullOrEmpty(response.accessToken)
                || string.IsNullOrEmpty(response.guestToken)) throw new FormatException();
            if (credential != "R:" + response.guestToken)
                GuestCredentialStore.Write("R:" + response.guestToken);
        }
        catch (Exception)
        {
            session.SetStatus("Could not confirm the saved account. Reopen the app to retry.");
            return;
        }
        session.Connect(response.accessToken, response.user.id, response.expiresAt, gameScene,
            gameServerAddress, gameServerPort, response.progress?.level, response.progress?.experience);
    }

    private void OnEnable()
    {
        if (LoginSession.Instance == null) return;
        LoginSession.Instance.StatusChanged += ShowStatus;
        ShowStatus(LoginSession.Instance.Status);
    }

    private void OnDisable()
    {
        if (LoginSession.Instance != null)
            LoginSession.Instance.StatusChanged -= ShowStatus;
    }

    private void ShowStatus(string message)
    {
        if (statusText == null) return;
        // The prototype's bundled font is Latin-only, matching its existing English UI.
        statusText.text = message switch
        {
            "로그인해 주세요." => "Connecting...",
            "게임 씬을 불러오는 중입니다." => "Loading game...",
            "게임 서버에 접속하고 인증하는 중입니다." => "Connecting to game server...",
            "플레이 준비 완료" => "Ready to play.",
            "로그인이 만료됐습니다. 다시 로그인해 주세요." => "Session expired. Reopen the app to reconnect.",
            "인증이 거절됐습니다. 다시 로그인해 주세요." => "Authentication rejected. Reopen the app to reconnect.",
            "이미 게임에 접속한 계정입니다. 다른 계정을 사용해 주세요." => "This account is already connected. Close the other session and reopen the app.",
            "로그인 서버를 사용할 수 없습니다. 잠시 후 다시 시도해 주세요." => "Authentication server unavailable. Reopen the app to retry.",
            "게임 서버 연결이 종료됐습니다. 다시 로그인해 주세요." => "Disconnected from game server. Reopen the app to reconnect.",
            "게임 서버 연결 시간이 초과됐습니다. 서버 실행 상태를 확인해 주세요." => "Game connection timed out. Check that the game server is running.",
            _ => message
        };
    }

    private UnityWebRequest JsonPost(string endpoint, string json)
    {
        var request = new UnityWebRequest($"{baseUrl.TrimEnd('/')}{endpoint}", "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 10;
        return request;
    }
}
