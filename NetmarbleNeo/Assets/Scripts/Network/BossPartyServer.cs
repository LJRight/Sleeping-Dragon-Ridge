using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public sealed class BossPartyState
{
    public ulong[] members = Array.Empty<ulong>(), ready = Array.Empty<ulong>();
    public ulong leader, inviter;
    public bool invited, preparing;
    public string source = "", token = "";
}

public partial class PlayerCombat
{
    public BossPartyState PartyState { get; private set; } = new();

    internal void SendPartyState(BossPartyState next)
    {
        if (IsSpawned) PartyStateRpc(JsonUtility.ToJson(next));
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void PartyStateRpc(string json) => PartyState = JsonUtility.FromJson<BossPartyState>(json);
}

internal static class BossPartyServer
{
    internal const int VictoryReturnSeconds = 3;

    private sealed class Party
    {
        // The first connected member leads. Attempts capture a separate, immutable roster.
        internal readonly List<PlayerCombat> members = new();
    }

    private sealed class Preparation
    {
        internal PlayerCombat[] members;
        internal readonly HashSet<PlayerCombat> ready = new();
        internal readonly string token = Guid.NewGuid().ToString("N");
        internal string source;
        internal bool submitting;
        internal Coroutine startRequest;
        internal float expires;
    }

    private sealed class Room
    {
        internal string attempt;
        internal int number;
        internal string[] accounts;
        internal NetworkObject boss;
        internal bool opening, clearing, celebrating, checkingSurvivors;
    }

    private static readonly Dictionary<PlayerCombat, Party> parties = new();
    private static readonly Dictionary<PlayerCombat, PlayerCombat> invites = new();
    private static readonly Dictionary<PlayerCombat, Preparation> preparations = new();
    private static readonly Dictionary<int, Room> rooms = new();
    private static readonly Dictionary<ulong, int> viewerRooms = new();
    private static bool movingGroup;
    internal static bool NewPlayerJoined { get; set; }

    internal static void RefreshNewPlayers(NetworkManager manager)
    {
        if (!NewPlayerJoined) return;
        NewPlayerJoined = false;
        RefreshObservers(manager);
    }

    internal static void Reset()
    {
        parties.Clear();
        invites.Clear();
        preparations.Clear();
        rooms.Clear();
        viewerRooms.Clear();
        movingGroup = false;
        NewPlayerJoined = false;
    }

    internal static int ViewerRoom(ulong client) => viewerRooms.TryGetValue(client, out var room) ? room : 0;
    internal static bool Entering(PlayerCombat player) => preparations.ContainsKey(player);
    internal static bool IsCelebrating(int number) => rooms.TryGetValue(number, out var room) && room.celebrating;

    private static void Publish(PlayerCombat player)
    {
        if (!player || !player.IsSpawned || !player.NetworkManager ||
            player.NetworkManager.ShutdownInProgress || !player.NetworkManager.ConnectedClients.ContainsKey(player.OwnerClientId)) return;
        var state = new BossPartyState();
        if (parties.TryGetValue(player, out var party))
        {
            state.members = party.members.Select(p => p.OwnerClientId).ToArray();
            state.leader = state.members[0];
        }
        if (invites.TryGetValue(player, out var sender))
        {
            state.invited = true;
            state.inviter = sender.OwnerClientId;
        }
        if (preparations.TryGetValue(player, out var check))
        {
            state.preparing = true;
            state.source = check.source;
            state.token = check.token;
            state.ready = check.ready.Select(p => p.OwnerClientId).ToArray();
        }
        player.SendPartyState(state);
    }

    private static PlayerCombat Peer(PlayerCombat requester, string raw)
    {
        if (!ulong.TryParse(raw, out var id) || !requester.NetworkManager.ConnectedClients.TryGetValue(id, out var client)
            || !client.PlayerObject) return null;
        return client.PlayerObject.GetComponent<PlayerCombat>();
    }

    private static bool Available(PlayerCombat player) =>
        player && player.IsSpawned && GameProgressBridge.Restored(player) && !player.InBossArena &&
        !player.GetComponent<NetworkHealth>().IsDead && !Entering(player);

    internal static bool Handle(PlayerCombat player, string action, string target)
    {
        switch (action)
        {
            case "partyInvite": Invite(player, Peer(player, target)); break;
            case "partyAccept": Respond(player, target, true); break;
            case "partyDecline": Respond(player, target, false); break;
            case "partyLeave": LeaveParty(player); break;
            case "partyReady":
            case "partyCancel":
                if (!preparations.TryGetValue(player, out var check) || check.token != target) break;
                if (action == "partyCancel") Cancel(check, "도전 준비가 취소되었습니다.");
                else if (!check.submitting && GameProgressBridge.CanJoinBoss(player, check.source))
                {
                    check.ready.Add(player);
                    if (check.ready.Count == check.members.Length) Submit(check);
                    else foreach (var member in check.members) Publish(member);
                }
                break;
            default: return false;
        }
        return true;
    }

    private static bool CanInvite(PlayerCombat sender, PlayerCombat recipient)
    {
        if (!Available(sender) || !Available(recipient) || sender == recipient || sender.AreaId != recipient.AreaId || parties.ContainsKey(recipient)) return false;
        return !parties.TryGetValue(sender, out var party) || (party.members[0] == sender && party.members.Count < 4);
    }

    private static void Invite(PlayerCombat sender, PlayerCombat recipient)
    {
        if (!CanInvite(sender, recipient) || invites.ContainsKey(recipient))
        {
            sender.SendProgressError("현재 이 기사에게 파티 초대를 보낼 수 없습니다.");
            return;
        }
        invites[recipient] = sender;
        Publish(recipient);
        sender.SendProgressError("파티 초대를 보냈습니다.");
    }

    private static void Respond(PlayerCombat recipient, string target, bool accept)
    {
        if (!invites.TryGetValue(recipient, out var sender) || sender.OwnerClientId.ToString() != target) return;
        invites.Remove(recipient);
        if (accept && CanInvite(sender, recipient))
        {
            if (!parties.TryGetValue(sender, out var party))
            {
                parties[sender] = party = new Party();
                party.members.Add(sender);
            }
            ClearInvites(recipient);
            parties[recipient] = party;
            party.members.Add(recipient);
            foreach (var member in party.members) Publish(member);
        }
        else
        {
            Publish(recipient);
            sender.SendProgressError(accept ? "파티 구성이 변경되었습니다." : "파티 초대를 거절했습니다.");
        }
    }

    private static void ClearInvites(PlayerCombat player)
    {
        invites.Remove(player);
        foreach (var recipient in invites.Where(pair => pair.Value == player).Select(pair => pair.Key).ToArray())
        {
            invites.Remove(recipient);
            Publish(recipient);
        }
    }

    private static void LeaveParty(PlayerCombat player)
    {
        ClearInvites(player);
        if (preparations.TryGetValue(player, out var check)) Cancel(check, "파티 구성이 변경되어 준비를 취소했습니다.");
        if (parties.Remove(player, out var party))
        {
            party.members.Remove(player);
            if (party.members.Count == 1) parties.Remove(party.members[0]);
            foreach (var other in party.members) Publish(other);
        }
        Publish(player);
    }

    internal static void RequestEntry(PlayerCombat player, string source)
    {
        if (source != "town" && source != "gate") return;
        if (Entering(player)) return;
        PlayerCombat[] members = { player };
        if (parties.TryGetValue(player, out var party))
        {
            if (party.members[0] != player) { player.SendProgressError("파티장이 도전을 시작할 수 있습니다."); return; }
            members = party.members.ToArray();
        }
        foreach (var member in members)
        {
            if (Entering(member) || HasUnfinishedRoom(member))
            {
                player.SendProgressError("참가자의 이전 보스 도전이 진행 중입니다.");
                return;
            }
            if (GameProgressBridge.CanJoinBoss(member, source)) continue;
            if (member != player) player.SendProgressError("파티원이 입장 조건을 충족하지 않았습니다.");
            return;
        }
        var check = new Preparation { members = members, source = source, expires = Time.unscaledTime + 60f };
        check.ready.Add(player);
        foreach (var member in members)
        {
            ClearInvites(member);
            preparations[member] = check;
        }
        foreach (var member in members) Publish(member);
        if (members.Length == 1) Submit(check);
    }

    private static bool HasUnfinishedRoom(PlayerCombat player)
    {
        string account = GameProgressBridge.BossAccountId(player);
        return rooms.Values.Any(r => Array.IndexOf(r.accounts, account) >= 0);
    }

    private static void Cancel(Preparation check, string message)
    {
        if (check.startRequest != null)
        {
            GameProgressBridge.CancelBossStart(check.startRequest);
            check.startRequest = null;
        }
        foreach (var member in check.members)
        {
            if (!preparations.TryGetValue(member, out var current) || current != check) continue;
            preparations.Remove(member);
            if (!member || !member.IsSpawned) continue;
            Publish(member);
            if (!string.IsNullOrEmpty(message)) member.SendProgressError(message);
        }
    }

    internal static void Expire()
    {
        if (preparations.Count == 0) return;
        foreach (var check in preparations.Values.Distinct().Where(p => Time.unscaledTime >= p.expires).ToArray())
            Cancel(check, "파티 도전 준비 시간이 끝났습니다.");
    }

    private static void Submit(Preparation check)
    {
        if (check.submitting) return;
        var accounts = check.members.Select(GameProgressBridge.BossAccountId).ToArray();
        bool Valid() => check.members.All(p => p && p.IsSpawned && preparations.TryGetValue(p, out var current)
            && current == check && GameProgressBridge.CanJoinBoss(p, check.source));
        if (accounts.Any(string.IsNullOrEmpty) || !ServerMonsterSpawner.Arena || !Valid())
        {
            Cancel(check, "입장 조건이 변경되어 준비를 취소했습니다.");
            return;
        }
        check.submitting = true;
        check.expires = Time.unscaledTime + 60f;
        check.startRequest = GameProgressBridge.StartBossAttempt(check.token, accounts, (ok, reason) =>
        {
            check.startRequest = null;
            if (!ok || !ServerMonsterSpawner.Arena || !Valid())
            {
                Cancel(check, ok ? "입장 조건이 변경되었습니다." : reason);
                return;
            }
            int number = 1;
            while (rooms.ContainsKey(number)) number++;
            var room = new Room { number = number, attempt = check.token, accounts = accounts, opening = true };
            rooms.Add(number, room);
            if (SceneWorld.IsPlaced(SceneWorld.Boss)) SceneWorld.OpenRoom(number);
            else BossArenaLandmarks.ShowRoom(number);
            movingGroup = true;
            try
            {
                for (int slot = 0; slot < check.members.Length; slot++)
                {
                    var member = check.members[slot];
                    BossArena.Enter(member, check.source, number, slot);
                    GameProgressBridge.DiscoverAfterEntry(member);
                }
            }
            finally { movingGroup = false; }
            RefreshObservers(check.members[0].NetworkManager);
            room.boss = ServerMonsterSpawner.Arena.SpawnMonster(number);
            room.opening = false;
            Cancel(check, null);
        });
    }

    internal static void RoomChanged(PlayerCombat player, int previous)
    {
        viewerRooms[player.OwnerClientId] = player.AreaId;
        if (!movingGroup) RefreshObservers(player.NetworkManager);
        CheckRoomAfterChange(previous);
    }

    private static void RefreshObservers(NetworkManager manager)
    {
        if (!manager || !manager.IsServer || manager.ShutdownInProgress) return;
        foreach (var entity in NetworkHealth.Spawned)
        {
            if (!entity || !entity.IsSpawned) continue;
            var obj = entity.NetworkObject;
            foreach (var client in manager.ConnectedClientsIds)
            {
                if (client == NetworkManager.ServerClientId || (obj.IsPlayerObject && obj.OwnerClientId == client)) continue;
                bool show = entity.VisibleTo(client);
                if (show == obj.IsNetworkVisibleTo(client)) continue;
                if (show) obj.NetworkShow(client);
                else obj.NetworkHide(client);
            }
        }
    }

    internal static void Disconnected(PlayerCombat player)
    {
        int previous = ViewerRoom(player.OwnerClientId);
        viewerRooms.Remove(player.OwnerClientId);
        LeaveParty(player);
        CheckRoomAfterChange(previous);
    }

    internal static bool SpectateOnDeath(PlayerCombat player)
    {
        if (!rooms.TryGetValue(player.BossRoomId, out var room) || room.accounts.Length < 2) return false;
        CheckRoomAfterChange(room.number);
        return true;
    }

    private static void CheckRoomAfterChange(int number)
    {
        if (!rooms.TryGetValue(number, out var room) || room.opening || room.clearing) return;
        if (room.accounts.Length == 1)
        {
            if (!viewerRooms.ContainsValue(number)) Close(room);
            return;
        }
        var spawner = ServerMonsterSpawner.Arena;
        if (room.checkingSurvivors || !spawner || !spawner.IsSpawned || spawner.NetworkManager.ShutdownInProgress) return;
        room.checkingSurvivors = true;
        spawner.StartCoroutine(CheckPartySurvivors(room, spawner.NetworkManager));
    }

    private static System.Collections.IEnumerator CheckPartySurvivors(Room room, NetworkManager manager)
    {
        // An area hit may down multiple players in the same loop. Finish that hit before moving
        // players, changing observers, or despawning the boss that is performing the attack.
        yield return null;
        room.checkingSurvivors = false;
        if (!manager || !manager.IsServer || manager.ShutdownInProgress || room.clearing ||
            !rooms.TryGetValue(room.number, out var current) || current != room) yield break;
        foreach (var client in manager.ConnectedClients.Values)
        {
            if (ViewerRoom(client.ClientId) != room.number || !client.PlayerObject) continue;
            if (client.PlayerObject.TryGetComponent<NetworkHealth>(out var health) && health.IsActive && !health.IsDead)
                yield break;
        }
        room.clearing = true;
        FinishRoom(room, false, "파티가 전멸했습니다. 야영지로 복귀했습니다.");
    }

    private static void Close(Room room)
    {
        rooms.Remove(room.number);
        var spawner = ServerMonsterSpawner.Arena;
        if (room.boss && room.boss.IsSpawned && spawner && !spawner.NetworkManager.ShutdownInProgress)
            spawner.ReturnToPool(room.boss);
        if (SceneWorld.IsPlaced(SceneWorld.Boss)) SceneWorld.CloseRoom(room.number);
        else BossArenaLandmarks.RemoveRoom(room.number);
    }

    internal static void Defeated(NetworkHealth defeated)
    {
        int number = defeated.CombatRoom;
        if (!rooms.TryGetValue(number, out var room) || room.clearing ||
            !defeated.IsDead || room.boss != defeated.NetworkObject) return;
        room.clearing = true;
        var combat = defeated.GetComponent<BossCombat>();
        double showDeathUntil = Time.timeAsDouble + (combat ? combat.PhaseDuration : 0f);
        GameProgressBridge.ClearBossAttempt(room.attempt, (ok, reason) =>
        {
            var spawner = ServerMonsterSpawner.Arena;
            if (spawner && spawner.IsSpawned)
                spawner.StartCoroutine(FinishAfterDeath(room, ok, reason, showDeathUntil));
        });
    }

    private static System.Collections.IEnumerator FinishAfterDeath(Room room, bool ok, string reason, double showDeathUntil)
    {
        while (Time.timeAsDouble < showDeathUntil) yield return null;
        if (!rooms.TryGetValue(room.number, out var live) || live != room) yield break;
        if (ok)
        {
            var manager = NetworkManager.Singleton;
            if (!manager || !manager.IsServer || manager.ShutdownInProgress) yield break;
            room.celebrating = true;
            foreach (var player in PlayersInRoom(manager, room))
                player.AnnounceBossVictoryRpc(room.number);
            yield return new WaitForSecondsRealtime(VictoryReturnSeconds);
            if (!rooms.TryGetValue(room.number, out live) || live != room) yield break;
        }
        FinishRoom(room, ok, reason);
    }

    private static IEnumerable<PlayerCombat> PlayersInRoom(NetworkManager manager, Room room)
    {
        foreach (var client in manager.ConnectedClients.Values)
            if (ViewerRoom(client.ClientId) == room.number && client.PlayerObject &&
                client.PlayerObject.TryGetComponent<PlayerCombat>(out var player))
                yield return player;
    }

    private static void FinishRoom(Room room, bool victory, string reason)
    {
        var manager = NetworkManager.Singleton;
        if (manager && manager.IsServer && !manager.ShutdownInProgress)
        {
            movingGroup = true;
            try
            {
                foreach (var player in PlayersInRoom(manager, room))
                {
                    player.ReturnFromBossAttempt();
                    if (!victory) player.SendProgressError(reason);
                }
            }
            finally { movingGroup = false; }
            RefreshObservers(manager);
        }
        Close(room);
    }
}
