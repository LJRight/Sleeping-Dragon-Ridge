using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Networking;

// The dedicated server serializes each account's checkpoints, including earned rewards after disconnect.
[RequireComponent(typeof(NetworkManager))]
public sealed partial class GameProgressBridge : MonoBehaviour
{
    [Serializable] public sealed class Stats { public int hp, attack, defense; }
    [Serializable] private sealed class Goal { public string target; public int count, required, observedCount; public bool allowExisting; }
    [Serializable] private sealed class Quest { public string id, type, state; public Goal[] goals; }
    [Serializable] private sealed class GearState { public string instanceId, definitionId, name, rarity; public bool equipped; public int enhancement, quantity, sellGold; }
    [Serializable] private sealed class Snapshot
    {
        public int revision, level, gold;
        public double experience;
        public string objective, starterSwordInstanceId;
        public bool bossDiscovered;
        public GearState[] items;
        public Stats stats;
        public Quest[] quests;
        public LootReceipt lootResult;
    }

    [Serializable] private sealed class Mutation
    {
        public string actionId, kind, target, npcId;
        public int revision;
        [NonSerialized] public PlayerCombat requester;
        [NonSerialized] public uint travelEpoch;
        [NonSerialized] public bool submitted;
    }

    private sealed class Account
    {
        public string id;
        public Snapshot snapshot;
        public PlayerCombat owner;
        public bool busy, reload;
        public int failures;
        public bool savingBossDiscovery;
        public TradeRoom trade;
        public readonly Queue<Mutation> mutations = new();
        public readonly Dictionary<string, int> pendingTutorial = new();
    }

    private readonly Dictionary<string, Account> accounts = new();
    private GameServerAuthentication authentication;
    private string internalKey;
    private static GameProgressBridge instance;

    public static string SnapshotJson { get; private set; }
    public static string Status { get; private set; } = "";
    public static bool Ready => !string.IsNullOrEmpty(SnapshotJson);
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        instance = null;
        SnapshotJson = null;
        Status = "";
        Changed = null;
        LootNotifications.Clear();
    }

    private void Awake()
    {
        instance = this;
        authentication = GetComponent<GameServerAuthentication>();
#if UNITY_EDITOR
        if (EditorScenarioSession.IsScenarioClient) return;
        if (EditorScenarioSession.IsScenarioServer)
        {
            internalKey = EditorScenarioSession.ServerKey;
            return;
        }
#endif
        internalKey = Environment.GetEnvironmentVariable("GAME_SERVER_KEY");
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        ClearClient();
    }

    internal static void ClearClient()
    {
        SnapshotJson = null;
        LootNotifications.Clear();
        SetStatus("");
    }

    private static PlayerCombat LocalPlayer()
    {
        var manager = NetworkManager.Singleton;
        return manager != null && manager.IsConnectedClient
            ? manager.LocalClient?.PlayerObject?.GetComponent<PlayerCombat>() : null;
    }

    public static void Request(string kind, string targetId = "", string npcId = "")
    {
        if (Ready) LocalPlayer()?.SendProgressRequest(kind, targetId, npcId);
    }

    public static void Refresh() => LocalPlayer()?.SendProgressRequest("refresh", "", "");

    internal static void SetStatus(string message)
    {
        Status = message ?? "";
        Changed?.Invoke();
    }

    internal static void ReceiveSnapshot(string json)
    {
        Snapshot data;
        try { data = JsonUtility.FromJson<Snapshot>(json); }
        catch (ArgumentException) { SetStatus("진행 정보를 읽을 수 없습니다."); return; }
        if (data?.stats == null || data.level < 1) { SetStatus("진행 정보가 비어 있습니다."); return; }
        SnapshotJson = json;
        LoginSession.Instance?.ApplyProgress(data.level, data.experience);
        SetStatus("");
    }

    internal static void Register(PlayerCombat player)
    {
        if (!instance || !instance.authentication.Accounts.TryGetValue(player.OwnerClientId, out var id)) return;
        if (!instance.accounts.TryGetValue(id, out var account))
            instance.accounts[id] = account = new Account { id = id };
        account.owner = player;
        // Re-read on every admission; this also picks up changes made while disconnected.
        account.reload = true;
        instance.StartWork(account);
    }

    internal static void Unregister(PlayerCombat player)
    {
        InterruptTrade(player);
        BossPartyServer.Disconnected(player);
        if (!instance) return;
        foreach (var entry in instance.accounts)
        {
            if (entry.Value.owner != player) continue;
            var account = entry.Value;
            account.owner = null;
            if (!account.busy && account.trade == null && account.mutations.Count == 0) instance.accounts.Remove(entry.Key);
            break;
        }
    }

    private static Account For(PlayerCombat player)
    {
        if (!player || !instance || !instance.authentication.Accounts.TryGetValue(player.OwnerClientId, out var id)
            || !instance.accounts.TryGetValue(id, out var account) || account.owner != player) return null;
        return account;
    }

    internal static bool Restored(PlayerCombat player) => For(player)?.snapshot != null;

    internal static void ClientRequest(PlayerCombat player, string kind, string target, string npc)
    {
        var account = For(player);
        if (account == null)
        {
            if (IsForgeAction(kind)) player.SendProgressError("진행 정보를 불러온 뒤 강화하세요.", true);
            return;
        }
        if (kind == "refresh")
        {
            if (account.busy) return;
            account.reload = true;
            instance.StartWork(account);
            return;
        }
        if (account.snapshot != null && BossPartyServer.Handle(player, kind, target)) return;
        if (kind == "leaveBoss")
        {
            BossArena.Leave(player);
            return;
        }
        if (account.snapshot == null)
        {
            if (IsForgeAction(kind)) player.SendProgressError("진행 정보를 불러온 뒤 강화하세요.", true);
            return;
        }
        if (kind == "enterBoss")
        {
            BossPartyServer.RequestEntry(player, target);
            return;
        }
        if (account.mutations.Count >= 8)
        {
            if (IsForgeAction(kind)) player.SendProgressError("이전 요청을 처리한 뒤 다시 강화하세요.", true);
            return;
        }
        if (kind == "accept" || kind == "report")
        {
            if (player.AreaId != 0 || !QuestNpcLocations.TryPosition(npc, out var position) ||
                Vector3.Distance(player.transform.position, position) > QuestNpcLocations.Radius)
            { player.SendProgressError("NPC에게 더 가까이 이동하세요."); return; }
        }
        else if (IsNpcService(kind))
        {
            string error = NpcServiceError(player, account.snapshot, kind, target, npc);
            if (error != null) { player.SendProgressError(error, IsForgeAction(kind)); return; }
            // An automatic retry may arrive before either peer has seen the first receipt.
            foreach (var pending in account.mutations)
            {
                if (pending.target != target || pending.requester != player || pending.travelEpoch != player.TravelEpoch) continue;
                if (kind == "sell" && pending.kind == "sell") return;
                if (IsForgeAction(kind) && IsForgeAction(pending.kind) && (kind == "temper" || pending.kind == "temper")) return;
            }
        }
        else if (kind == "tutorial" && target == "open_inventory")
        {
            Tutorial(player, target);
            return;
        }
        else if (kind == "equip" && (player.InBossArena || BossPartyServer.Entering(player)) &&
            Array.Exists(account.snapshot.items ?? Array.Empty<GearState>(), item => item.instanceId == target && item.definitionId == "ridge_sword"))
        {
            player.SendProgressError("도전 준비 중이거나 보스 전장에 있을 때는 종결 검을 착용할 수 없습니다.");
            return;
        }
        else if (kind != "equip" && kind != "unequip") return;

        instance.Enqueue(account, kind, target, npc);
    }

    private static bool IsForgeAction(string kind) => kind == "enhance" || kind == "temper";
    private static bool IsNpcService(string kind) => kind == "sell" || IsForgeAction(kind);

    private static string NpcServiceError(PlayerCombat player, Snapshot progress, string kind, string target, string npc)
    {
        if (player.GetComponent<NetworkHealth>().IsDead)
            return "쓰러진 상태에서는 이용할 수 없습니다.";
        bool selling = kind == "sell";
        string requiredNpc = selling ? "warden" : "smith";
        var actor = QuestNpcLocations.FindVillageActor(requiredNpc);
        if (npc != requiredNpc || player.AreaId != 0 || !actor ||
            (player.transform.position - actor.position).sqrMagnitude > QuestNpcLocations.Radius * QuestNpcLocations.Radius)
            return selling ? "마을 경비대장의 4m 안에서 판매할 수 있습니다." : "마을 대장장이의 4m 안에서 강화할 수 있습니다.";
        if (kind == "temper" && (string.IsNullOrEmpty(target) || target != progress.starterSwordInstanceId ||
            !Array.Exists(progress.quests ?? Array.Empty<Quest>(), q => q.id == "first_temper" && q.state == "active")))
            return "첫 강화 의뢰와 지급받은 검을 확인하세요.";
        return null;
    }

    internal static void Kill(PlayerCombat player, NetworkHealth defeated)
    {
        if (defeated.IsArenaBoss)
        {
            BossPartyServer.Defeated(defeated);
            return;
        }
        var account = For(player);
        if (account != null) instance.Enqueue(account, "kill", defeated.MonsterDefinition, "");
    }

    private static bool BossObjectiveOpen(Snapshot progress) =>
        progress.objective == "boss" || progress.objective == "complete";

    internal static bool CanJoinBoss(PlayerCombat player, string source)
    {
        var account = For(player);
        string error = null;
        if (account?.snapshot == null || account.reload || account.busy)
            error = "진행 정보를 저장한 뒤 다시 시도하세요.";
        else if (!BossObjectiveOpen(account.snapshot))
            error = "‘관문 앞의 엄니’를 보고하고 Lv.20에 도달하세요.";
        else if (source == "town" && !account.snapshot.bossDiscovered)
            error = "첫 도전은 능선 입구에서 시작하세요.";
        else if (Array.Exists(account.snapshot.items ?? Array.Empty<GearState>(), item =>
                     item.definitionId == "ridge_sword" && item.equipped))
            error = "종결 검을 해제한 뒤 도전하세요.";
        else if (!BossArena.CanEnter(player, source))
            error = source == "gate" ? "파티원 모두 능선 입구에 모이세요." : "파티원 모두 마을에 모이세요.";
        if (error == null) return true;
        player.SendProgressError(error);
        return false;
    }

    internal static void CancelBossStart(Coroutine request)
    {
        if (instance && request != null) instance.StopCoroutine(request);
    }

    internal static void DiscoverAfterEntry(PlayerCombat player)
    {
        var account = For(player);
        if (account?.snapshot == null) return;
        if (account.snapshot.bossDiscovered || account.savingBossDiscovery) return;
        // Record the completed physical crossing. A delayed write must never move a player.
        account.savingBossDiscovery = true;
        instance.Enqueue(account, "discoverBoss", "ridge", "");
    }

    internal static void Tutorial(PlayerCombat player, string target)
    {
        var account = For(player);
        if (account?.snapshot?.quests == null) return;
        account.pendingTutorial.TryGetValue(target, out int pendingCount);
        foreach (var quest in account.snapshot.quests)
        {
            if (quest.type != "tutorial" || quest.goals == null || quest.state == "ready" || quest.state == "completed") continue;
            foreach (var goal in quest.goals)
            {
                if (target != goal.target) continue;
                bool eligible = quest.state == "active" ? goal.count + pendingCount < goal.required
                    : goal.allowExisting && goal.observedCount + pendingCount < goal.required;
                if (!eligible) continue;
                account.pendingTutorial[target] = pendingCount + 1;
                instance.Enqueue(account, "tutorial", target, "");
                return;
            }
        }
    }

    private void Enqueue(Account account, string kind, string target, string npc)
    {
        var mutation = new Mutation { actionId = Guid.NewGuid().ToString("N"), kind = kind, target = target, npcId = npc, requester = account.owner };
        if (IsNpcService(kind))
        {
            mutation.travelEpoch = account.owner.TravelEpoch;
        }
        account.mutations.Enqueue(mutation);
        StartWork(account);
    }

    private void StartWork(Account account)
    {
        if (account.busy || account.trade?.committing == true) return;
        account.busy = true;
        StartCoroutine(Work(account));
    }

    private IEnumerator Work(Account account)
    {
        try
        {
            if (string.IsNullOrEmpty(internalKey))
            {
                Debug.LogError("Game progress unavailable: configure GAME_SERVER_KEY on the dedicated server.");
                if (account.owner) account.owner.SendProgressError("게임 서버의 진행 저장 설정을 확인하세요.");
                yield break;
            }
            string url = authentication.BaseUrl + "/internal/progress/" + Uri.EscapeDataString(account.id);
            do
            {
                if (!account.owner && account.mutations.Count == 0) break;
                var mutation = !account.reload && account.snapshot != null && account.mutations.Count > 0 ? account.mutations.Peek() : null;
                // A queued or retrying request can outlive the visit that authorized it.
                if (mutation != null && IsNpcService(mutation.kind))
                {
                    var owner = account.owner;
                    string serviceError = !owner || !owner.IsSpawned || mutation.requester != owner ||
                        mutation.travelEpoch != owner.TravelEpoch ? "마을 NPC를 다시 방문하세요." :
                        NpcServiceError(owner, account.snapshot, mutation.kind, mutation.target, mutation.npcId);
                    if (serviceError != null)
                    {
                        CompleteMutation(account, mutation);
                        // A lost HTTP reply may have followed a commit. Reconcile without a new charge.
                        if (mutation.submitted && owner) account.reload = true;
                        if (owner && owner == mutation.requester) owner.SendProgressError(serviceError, IsForgeAction(mutation.kind));
                        continue;
                    }
                }
                // A boss clear or reconnect can request another read while this one is in flight.
                if (mutation == null) account.reload = false;
                using var request = mutation == null ? UnityWebRequest.Get(url) : new UnityWebRequest(url + "/action", "POST");
                if (mutation != null)
                {
                    mutation.revision = account.snapshot.revision;
                    mutation.submitted = true;
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(mutation)));
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.SetRequestHeader("X-Game-Server-Key", internalKey);
                request.timeout = 8;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var json = request.downloadHandler.text;
                    Snapshot data = null;
                    try { data = JsonUtility.FromJson<Snapshot>(json); } catch (ArgumentException) { }
                    if (data?.stats == null || data.level < 1)
                    {
                        // The write may already have committed. Keep its action ID for reconciliation.
                        if (mutation == null) account.reload = true;
                        if (account.owner) account.owner.SendProgressError("진행 응답을 읽지 못해 다시 확인하고 있습니다.");
                        yield return new WaitForSecondsRealtime(3);
                        continue;
                    }
                    account.failures = 0;
                    account.snapshot = data;
                    if (mutation != null)
                    {
                        CompleteMutation(account, mutation);
                        Debug.Log($"[Progress] account={account.id} action={mutation.kind} id={mutation.actionId} revision={data.revision}");
                    }
                    if (account.owner)
                    {
                        account.owner.ApplyProgressStats(data.stats);
                        // Keep a one-off drop separate from the inventory state used for refresh/reconnect.
                        if (data.lootResult != null)
                        {
                            var snapshot = Newtonsoft.Json.Linq.JObject.Parse(json);
                            snapshot.Remove("lootResult");
                            json = snapshot.ToString(Newtonsoft.Json.Formatting.None);
                        }
                        account.owner.SendProgressSnapshot(json);
                        if (mutation != null && mutation.requester == account.owner && account.owner.IsSpawned)
                        {
                            if (mutation.kind == "kill" && data.lootResult?.actionId == mutation.actionId)
                                account.owner.SendLootReceipt(data.lootResult);
                            else if (mutation.kind is "sell" or "equip" or "unequip")
                                account.owner.ItemActionCompletedRpc(sold: mutation.kind == "sell");
                        }
                    }
                    data.lootResult = null;
                    continue;
                }

                string error = request.downloadHandler?.text ?? "";
                if (mutation == null) account.reload = true;
                if (mutation != null && request.responseCode == 409 && error.Contains("STALE_REVISION"))
                {
                    account.reload = true;
                    continue;
                }
                if (mutation != null && request.responseCode >= 400 && request.responseCode < 500 &&
                    request.responseCode != 408 && request.responseCode != 429 && request.responseCode != 401 && request.responseCode != 403)
                {
                    CompleteMutation(account, mutation);
                    if (account.owner) account.owner.SendProgressError("요청을 적용할 수 없습니다. 아이템과 의뢰 상태를 확인하세요.",
                        IsForgeAction(mutation.kind) && account.owner == mutation.requester);
                    continue;
                }

                if (account.owner) account.owner.SendProgressError("진행 서버에 연결 중입니다. 잠시 후 다시 시도합니다.");
                account.failures = Mathf.Min(account.failures + 1, 5);
                yield return new WaitForSecondsRealtime(Mathf.Min(20, 1 << account.failures));
            }
            while (account.reload || account.snapshot == null || account.mutations.Count > 0);
        }
        finally
        {
            account.busy = false;
            if (!account.owner && account.trade == null && account.mutations.Count == 0) accounts.Remove(account.id);
        }
    }

    private static void CompleteMutation(Account account, Mutation mutation)
    {
        account.mutations.Dequeue();
        if (mutation.kind == "discoverBoss") account.savingBossDiscovery = false;
        if (mutation.kind != "tutorial") return;
        int remaining = account.pendingTutorial[mutation.target] - 1;
        if (remaining == 0) account.pendingTutorial.Remove(mutation.target);
        else account.pendingTutorial[mutation.target] = remaining;
    }
}
