using System;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public sealed partial class GameProgressBridge
{
    private sealed class Trader
    {
        public Account account;
        public PlayerCombat player;
        public uint epoch;
        public int revision;
        public bool ready;
        public TradeOffer offer = new();

        public Trader(Account account)
        {
            this.account = account;
            player = account.owner;
            epoch = player.TravelEpoch;
            revision = account.snapshot.revision;
        }

        public bool Present => CanTrade(player) && For(player) == account && player.TravelEpoch == epoch;
    }

    private sealed class TradeRoom
    {
        public readonly string token = Guid.NewGuid().ToString("N");
        public Trader first, second;
        public int version = 1;
        public bool accepted, committing;
        public float expires;

        public void Publish()
        {
            Send(first, second);
            Send(second, first);
        }

        private void Send(Trader self, Trader peer)
        {
            if (!self.player || !peer.player) return;
            self.player.SendTradeView(new TradeView {
                token = token, version = version, peer = peer.player.OwnerClientId,
                outgoing = self == first, invitation = !accepted, committed = committing,
                mineReady = self.ready, peerReady = peer.ready, mine = self.offer, theirs = peer.offer
            });
        }
    }

    [Serializable] private sealed class TradeTransfer
    {
        public string userId;
        public int revision, gold, ore, rareOre;
        public string[] itemIds;
        public TradeTransfer(Trader side)
        {
            userId = side.account.id;
            revision = side.revision;
            itemIds = side.offer.gear.Select(g => g.id).ToArray();
            gold = side.offer.gold;
            ore = side.offer.ore;
            rareOre = side.offer.rareOre;
        }
    }

    [Serializable] private sealed class TradeBody { public string tradeId; public TradeTransfer first, second; }
    [Serializable] private sealed class TradeReceipt { public string tradeId; public Snapshot first, second; }

    private static bool CanTrade(PlayerCombat player) =>
        player && player.IsSpawned && player.AreaId == 0 && !BossPartyServer.Entering(player)
        && !player.GetComponent<NetworkHealth>().IsDead;

    internal static void InviteTrade(PlayerCombat player, ulong peer)
    {
        var first = For(player);
        if (!CanTrade(player) || first?.snapshot == null || player.OwnerClientId == peer) return;
        var manager = player.NetworkManager;
        PlayerCombat target = null;
        if (manager.ConnectedClients.TryGetValue(peer, out var client) && client.PlayerObject)
            target = client.PlayerObject.GetComponent<PlayerCombat>();
        var second = For(target);
        if (!CanTrade(target) || second?.snapshot == null || first == second || first.trade != null || second.trade != null)
        {
            player.SendProgressError("지금은 이 기사와 거래를 시작할 수 없습니다.");
            return;
        }
        var room = new TradeRoom { first = new Trader(first), second = new Trader(second), expires = Time.unscaledTime + 45f };
        first.trade = second.trade = room;
        room.Publish();
        instance.StartCoroutine(instance.WatchTrade(room));
    }

    private IEnumerator WatchTrade(TradeRoom room)
    {
        while (room.first.account.trade == room && !room.committing)
        {
            yield return new WaitForSecondsRealtime(1);
            if (room.first.account.trade != room || room.committing) yield break;
            if (!room.first.Present || !room.second.Present || Time.unscaledTime >= room.expires)
                EndTrade(room, "거래가 종료되었습니다.", false);
        }
    }

    internal static void InterruptTrade(PlayerCombat player)
    {
        // Disconnect can remove the transport's authentication mapping before OnNetworkDespawn.
        if (!instance) return;
        foreach (var a in instance.accounts.Values)
            if (a.owner == player && a.trade != null && !a.trade.committing)
            {
                instance.EndTrade(a.trade, "상대가 자리를 떠나 거래가 취소되었습니다.", false);
                break;
            }
    }

    internal static void TradeCommand(PlayerCombat player, TradeAction action, string token, int version,
        int revision, string[] ids, int gold, int ore, int rareOre)
    {
        var account = For(player);
        var room = account?.trade;
        if (room == null || room.token != token || room.committing) return;
        var self = room.first.account == account ? room.first : room.second;
        if (self.player != player) return;
        if (action == TradeAction.Cancel || Time.unscaledTime >= room.expires || !room.first.Present || !room.second.Present)
        {
            instance.EndTrade(room, "거래가 취소되었습니다.", false);
            return;
        }
        if (room.version != version) { room.Publish(); return; }
        if (action == TradeAction.Accept && !room.accepted && self == room.second)
        {
            room.accepted = true;
            room.version++;
        }
        else if (!room.accepted) return;
        else if (action == TradeAction.Withdraw && self.ready)
        {
            room.first.ready = room.second.ready = false;
            room.version++;
        }
        else if (action == TradeAction.Edit || action == TradeAction.Confirm)
        {
            // Keep both confirmations bound to the inventories originally displayed, even if
            // another game server or an in-flight forge operation changes either one.
            if (revision != self.revision || room.first.revision != room.first.account.snapshot.revision ||
                room.second.revision != room.second.account.snapshot.revision)
            {
                instance.EndTrade(room, "보유 목록이 변경되어 거래가 종료되었습니다. 새 목록으로 다시 요청하세요.", true);
                return;
            }
            if (action == TradeAction.Edit)
            {
                var offer = ReadOffer(account.snapshot, ids, gold, ore, rareOre);
                if (offer == null) { player.SendProgressError("거래할 장비 또는 보유 수량을 확인하세요."); return; }
                room.first.ready = room.second.ready = false;
                self.offer = offer;
                room.version++;
            }
            else
            {
                if (self.ready) return;
                if (room.first.offer.Empty && room.second.offer.Empty)
                { player.SendProgressError("한 명 이상 거래 물품을 등록하세요."); return; }
                if (room.first.account.busy || room.second.account.busy)
                { player.SendProgressError("진행 정보를 저장 중입니다. 잠시 후 다시 확정하세요."); return; }
                self.ready = true;
                if (room.first.ready && room.second.ready)
                {
                    room.committing = true;
                    room.Publish();
                    instance.StartCoroutine(instance.CommitTrade(room));
                    return;
                }
            }
        }
        else return;
        room.expires = Time.unscaledTime + 180f;
        room.Publish();
    }

    private static TradeOffer ReadOffer(Snapshot snapshot, string[] ids, int gold, int ore, int rareOre)
    {
        if (gold < 0 || gold > snapshot.gold || ore < 0 || rareOre < 0 || ids.Distinct().Count() != ids.Length) return null;
        var items = snapshot.items ?? Array.Empty<GearState>();
        int Count(string definition) => Array.Find(items, i => i.definitionId == definition)?.quantity ?? 0;
        if (ore > Count("ore") || rareOre > Count("rare_ore")) return null;
        var offer = new TradeOffer { gear = new TradeGear[ids.Length], gold = gold, ore = ore, rareOre = rareOre };
        for (int i = 0; i < ids.Length; i++)
        {
            var item = Array.Find(items, gear => gear.instanceId == ids[i]);
            if (item == null || item.equipped || item.sellGold <= 0) return null;
            string rank = item.rarity == "legendary" ? "전설" : item.rarity == "rare" ? "레어" : "일반";
            offer.gear[i] = new TradeGear { id = item.instanceId, name = item.name, rank = rank, enhancement = item.enhancement };
        }
        return offer;
    }

    private IEnumerator CommitTrade(TradeRoom room)
    {
        // Freeze the bytes, both revisions and the id before the first send. Even a lost reply
        // followed by a disconnect must resolve this same database operation.
        var body = new TradeBody { tradeId = room.token, first = new TradeTransfer(room.first), second = new TradeTransfer(room.second) };
        byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));
        int delay = 1;
        while (true)
        {
            using (var request = new UnityWebRequest(authentication.BaseUrl + "/internal/trade/commit", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(payload);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("X-Game-Server-Key", internalKey);
                request.timeout = 8;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    TradeReceipt receipt = null;
                    try { receipt = JsonUtility.FromJson<TradeReceipt>(request.downloadHandler.text); }
                    catch (ArgumentException) { }
                    if (receipt?.tradeId == room.token && receipt.first?.stats != null && receipt.second?.stats != null)
                    {
                        EndTrade(room, "거래가 완료되었습니다.", true);
                        yield break;
                    }
                }
                if (request.responseCode >= 400 && request.responseCode < 500 && request.responseCode != 408 && request.responseCode != 429)
                {
                    EndTrade(room, "거래가 성립되지 않았습니다. 보유 목록을 새로 불러옵니다.", true);
                    yield break;
                }
            }
            yield return new WaitForSecondsRealtime(delay);
            delay = Mathf.Min(delay * 2, 12);
        }
    }

    private void EndTrade(TradeRoom room, string message, bool refresh)
    {
        Release(room.first);
        Release(room.second);

        void Release(Trader side)
        {
            var account = side.account;
            if (account.trade != room) return;
            account.trade = null;
            if (account.owner)
            {
                account.owner.SendTradeView(null, message);
                if (refresh) account.reload = true;
            }
            if (account.reload || account.mutations.Count > 0) StartWork(account);
            else if (!account.owner && !account.busy) accounts.Remove(account.id);
        }
    }
}
