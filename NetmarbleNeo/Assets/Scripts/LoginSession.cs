using System;
using System.Collections;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

// One authenticated session survives travel between the expedition maps.
public class LoginSession : MonoBehaviour
{
    public static LoginSession Instance { get; private set; }
    public bool AutoLoginAttempted { get; set; }
    public void ApplyProgress(int level, double experience) { Level = level; Experience = experience; }
    public string AccountId { get; private set; }
    public int? Level { get; private set; }
    public double? Experience { get; private set; }
    public string GameServerAddress => serverAddress;
    public string Status { get; private set; } = "로그인해 주세요.";
    public bool IsBusy => connecting || returning;
    public event Action<string> StatusChanged;
    private string token;
    private long expiresAt;
    private string loginScene;
    private string gameScene;
    private string serverAddress;
    private ushort serverPort;
    private NetworkManager manager;
    private bool connecting;
    private bool connected;
    private bool returning;
    private float deadline;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (Instance == null)
            new GameObject(nameof(LoginSession)).AddComponent<LoginSession>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public void SetStatus(string message)
    {
        Status = message;
        StatusChanged?.Invoke(message);
        Debug.Log(message);
    }

    public void Connect(string accessToken, string accountId, long expiry,
        string targetScene, string address, ushort port, int? level = null, double? experience = null)
    {
        if (IsBusy || connected) return;
        if (string.IsNullOrEmpty(accessToken) || expiry <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            SetStatus("로그인이 만료됐습니다. 다시 로그인해 주세요.");
            return;
        }
        if (!Application.CanStreamedLevelBeLoaded(targetScene))
        {
            SetStatus("게임 씬을 Scene List에 추가해 주세요: " + targetScene);
            return;
        }
        token = accessToken;
        AccountId = accountId;
        Level = level;
        Experience = experience;
        expiresAt = expiry;
        loginScene = SceneManager.GetActiveScene().path;
        gameScene = targetScene;
        serverAddress = address;
        serverPort = port;
        connecting = true;
        deadline = Time.realtimeSinceStartup + 30f;
        SetStatus("게임 씬을 불러오는 중입니다.");
        if (gameScene == WorldSceneCoordinator.SessionName) WorldLoadingScreen.Show(SceneWorld.Village);
        SceneManager.LoadSceneAsync(gameScene);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (LocalCombatTest.InScene(scene)) return;
        var candidate = NetworkManager.Singleton;
        if (candidate == null)
            candidate = FindFirstObjectByType<NetworkManager>();
        if (candidate != null && !candidate.IsListening)
        {
            // Runs before Start, including when the dedicated server opens the session scene.
            if (candidate.GetComponent<GameServerAuthentication>() == null)
                candidate.gameObject.AddComponent<GameServerAuthentication>();
        }
        if (!connecting || manager != null || (scene.name != gameScene && scene.path != gameScene))
            return;
        if (candidate == null) { Fail("게임 씬에 NetworkManager가 없습니다."); return; }
        if (candidate.IsListening) { Fail("이미 실행 중인 네트워크 연결이 있습니다."); return; }
        if (expiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        { Fail("로그인이 만료됐습니다. 다시 로그인해 주세요."); return; }

        manager = candidate;
        var transport = manager.GetComponent<UnityTransport>();
        if (transport == null) { Fail("Unity Transport가 없습니다."); return; }
        transport.SetConnectionData(serverAddress, serverPort);
        manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(token);
        manager.OnClientDisconnectCallback += OnDisconnected;
        deadline = Time.realtimeSinceStartup + 25f;
        SetStatus("게임 서버에 접속하고 인증하는 중입니다.");
        if (!manager.StartClient())
            Fail("게임 서버 접속을 시작하지 못했습니다.");
    }

    private void Update()
    {
        if (!connecting || returning) return;
        if (manager != null && manager.IsConnectedClient
            && manager.LocalClient.PlayerObject != null && manager.LocalClient.PlayerObject.IsSpawned)
        {
            connecting = false;
            connected = true;
            // No need to retain the raw token once game admission is complete.
            token = null;
            manager.NetworkConfig.ConnectionData = Array.Empty<byte>();
            SetStatus("플레이 준비 완료");
        }
        else if (Time.realtimeSinceStartup > deadline)
            Fail("게임 서버 연결 시간이 초과됐습니다. 서버 실행 상태를 확인해 주세요.");
    }

    private void OnDisconnected(ulong clientId)
    {
        if (returning || manager == null || manager.IsServer) return;
        string reason = manager.DisconnectReason;
        string message = reason switch
        {
            "AUTH_REQUIRED" or "AUTH_INVALID" => "인증이 거절됐습니다. 다시 로그인해 주세요.",
            "ACCOUNT_ALREADY_CONNECTED" => "이미 게임에 접속한 계정입니다. 다른 계정을 사용해 주세요.",
            "AUTH_UNAVAILABLE" or "AUTH_BUSY" => "로그인 서버를 사용할 수 없습니다. 잠시 후 다시 시도해 주세요.",
            _ => "게임 서버 연결이 종료됐습니다. 다시 로그인해 주세요."
        };
        Fail(message);
    }

    internal void ReturnAfterMapError(string scene) => Fail("지역을 불러올 수 없습니다: " + scene);

    private void Fail(string message)
    {
        if (returning) return;
        AutoLoginAttempted = true;
        returning = true;
        connecting = connected = false;
        token = null;
        AccountId = null;
        Level = null;
        Experience = null;
        serverAddress = null;
        expiresAt = 0;
        SetStatus(message);
        StartCoroutine(ReturnToLogin());
    }

    private IEnumerator ReturnToLogin()
    {
        if (manager != null)
        {
            manager.OnClientDisconnectCallback -= OnDisconnected;
            manager.NetworkConfig.ConnectionData = Array.Empty<byte>();
            manager.Shutdown();
            while (manager != null && manager.ShutdownInProgress)
                yield return null;
            if (manager != null)
                Destroy(manager.gameObject);
            manager = null;
            // Allow singleton cleanup before loading another scene.
            yield return null;
        }
        if (!string.IsNullOrEmpty(loginScene) && Application.CanStreamedLevelBeLoaded(loginScene))
            yield return SceneManager.LoadSceneAsync(loginScene);
        WorldLoadingScreen.Hide();
        returning = false;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (manager != null)
            manager.OnClientDisconnectCallback -= OnDisconnected;
        Instance = null;
    }
}
