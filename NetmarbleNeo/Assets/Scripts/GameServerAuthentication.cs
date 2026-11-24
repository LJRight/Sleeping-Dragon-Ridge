using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Networking;

[RequireComponent(typeof(NetworkManager))]
public class GameServerAuthentication : MonoBehaviour
{
    // Trusted server setting. Never accept the verification URL from a client.
    [SerializeField] private string authBaseUrl = "http://127.0.0.1:8080";
    private NetworkManager manager;
    private readonly Dictionary<ulong, string> accounts = new();
    private readonly Dictionary<ulong, NetworkManager.ConnectionApprovalResponse> pending = new();
    public IReadOnlyDictionary<ulong, string> Accounts => accounts;

    [Serializable] private class User { public string id; }
    [Serializable] private class MeResponse { public User user; }

    public string BaseUrl => authBaseUrl.TrimEnd('/');

    private void Awake()
    {
        authBaseUrl = Environment.GetEnvironmentVariable("NEO_AUTH_URL") ?? authBaseUrl;
#if UNITY_EDITOR
        if (EditorScenarioSession.IsScenarioServer) authBaseUrl = EditorScenarioSession.AuthUrl;
#endif
        manager = GetComponent<NetworkManager>();
        if (!TryGetComponent<GameProgressBridge>(out _)) gameObject.AddComponent<GameProgressBridge>();
        manager.NetworkConfig.ConnectionApproval = true;
        manager.NetworkConfig.ClientConnectionBufferTimeout = 15;
        manager.ConnectionApprovalCallback = ApproveConnection;
        manager.OnClientDisconnectCallback += OnDisconnected;
        manager.OnServerStopped += OnServerStopped;
    }

    private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        response.Approved = false;
        response.CreatePlayerObject = false;
        // This flow uses a dedicated server; async authentication of a local host is unsupported.
        if (request.ClientNetworkId == NetworkManager.ServerClientId)
        {
            response.Reason = "USE_DEDICATED_SERVER";
            return;
        }
        if (request.Payload == null || request.Payload.Length < 32 || request.Payload.Length > 256)
        {
            response.Reason = "AUTH_REQUIRED";
            return;
        }
        // Tokens issued by this backend are URL-safe ASCII strings.
        foreach (byte value in request.Payload)
        {
            if (!(value >= 'A' && value <= 'Z') && !(value >= 'a' && value <= 'z')
                && !(value >= '0' && value <= '9') && value != '-' && value != '_')
            {
                response.Reason = "AUTH_INVALID";
                return;
            }
        }
        if (pending.Count >= 32)
        {
            response.Reason = "AUTH_BUSY";
            return;
        }
        response.Pending = true;
        pending[request.ClientNetworkId] = response;
        StartCoroutine(Verify(request.ClientNetworkId, Encoding.ASCII.GetString(request.Payload), response));
    }

    private IEnumerator Verify(ulong clientId, string token,
        NetworkManager.ConnectionApprovalResponse response)
    {
        using var request = UnityWebRequest.Get($"{authBaseUrl.TrimEnd('/')}/auth/me");
        request.SetRequestHeader("Authorization", $"Bearer {token}");
        request.timeout = 10;
        yield return request.SendWebRequest();

        if (!manager.IsServer || manager.ShutdownInProgress
            || !pending.TryGetValue(clientId, out var current) || current != response)
            yield break;

        string reason = null;
        string accountId = null;
        if (request.responseCode == 401)
            reason = "AUTH_INVALID";
        else if (request.result != UnityWebRequest.Result.Success)
            reason = "AUTH_UNAVAILABLE";
        else
        {
            try { accountId = JsonUtility.FromJson<MeResponse>(request.downloadHandler.text)?.user?.id; }
            catch (ArgumentException) { }
            if (string.IsNullOrEmpty(accountId))
                reason = "AUTH_INVALID_RESPONSE";
            else if (accounts.ContainsValue(accountId))
                reason = "ACCOUNT_ALREADY_CONNECTED";
        }

        pending.Remove(clientId);
        if (reason == null)
        {
            // Reserve the identity before completing approval to prevent duplicate logins racing.
            accounts[clientId] = accountId;
            response.Approved = true;
            response.CreatePlayerObject = true;
        }
        else
            response.Reason = reason;
        response.Pending = false;
    }

    private void OnDisconnected(ulong clientId)
    {
        pending.Remove(clientId);
        accounts.Remove(clientId);
    }

    private void OnServerStopped(bool wasHost)
    {
        StopAllCoroutines();
        pending.Clear();
        accounts.Clear();
#if UNITY_EDITOR
        if (EditorScenarioSession.IsScenarioServer) EditorScenarioSession.Stop();
#endif
    }

    private void OnDestroy()
    {
        if (manager == null) return;
        manager.OnClientDisconnectCallback -= OnDisconnected;
        manager.OnServerStopped -= OnServerStopped;
        if (manager.ConnectionApprovalCallback == ApproveConnection)
            manager.ConnectionApprovalCallback = null;
    }
}
