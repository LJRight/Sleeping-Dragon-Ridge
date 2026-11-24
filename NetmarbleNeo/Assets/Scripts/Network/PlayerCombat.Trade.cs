using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

internal enum TradeAction : byte { Accept, Edit, Confirm, Withdraw, Cancel }

[Serializable]
internal sealed class TradeGear
{
    public string id, name, rank;
    public int enhancement;
    public string Label => $"{rank} {name} +{enhancement}";
}

[Serializable]
internal sealed class TradeOffer
{
    public TradeGear[] gear = Array.Empty<TradeGear>();
    public int gold, ore, rareOre;
    public bool Empty => gear.Length == 0 && gold == 0 && ore == 0 && rareOre == 0;
}

[Serializable]
internal sealed class TradeView
{
    public string token;
    public ulong peer;
    public int version;
    public bool invitation, outgoing, committed, mineReady, peerReady;
    public TradeOffer mine, theirs;
}

public partial class PlayerCombat
{
    internal TradeView Trade { get; private set; }
    internal string TradeNotice { get; private set; }
    internal float TradeNoticeUntil { get; private set; }

    internal void InviteToTrade(ulong peer)
    {
        if (IsSpawned && IsOwner) TradeInviteRpc(peer, TravelEpoch);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void TradeInviteRpc(ulong peer, uint epoch)
    {
        if (TravelEpoch == epoch) GameProgressBridge.InviteTrade(this, peer);
    }

    internal void RequestTrade(TradeAction action, TradeView view, int revision, string[] ids = null,
        int gold = 0, int ore = 0, int rareOre = 0)
    {
        if (IsSpawned && IsOwner && view != null)
            TradeCommandRpc(action, view.token, view.version, revision, string.Join(",", ids ?? Array.Empty<string>()), gold, ore, rareOre);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void TradeCommandRpc(TradeAction action, string token, int version, int revision, string gear, int gold, int ore, int rareOre)
    {
        if (token?.Length != 32 || gear == null || gear.Length > 4 * 65) return;
        string[] ids = gear.Length == 0 ? Array.Empty<string>() : gear.Split(',');
        if (ids.Length > 4 || ids.Any(id => id.Length == 0 || id.Length > 64)) return;
        GameProgressBridge.TradeCommand(this, action, token, version, revision, ids, gold, ore, rareOre);
    }

    internal void SendTradeView(TradeView view, string message = "")
    {
        if (IsSpawned && NetworkManager && !NetworkManager.ShutdownInProgress &&
            NetworkManager.ConnectedClients.ContainsKey(OwnerClientId))
            TradeViewRpc(view == null ? "" : JsonUtility.ToJson(view), message);
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void TradeViewRpc(string json, string message)
    {
        Trade = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<TradeView>(json);
        if (!string.IsNullOrEmpty(message))
        {
            TradeNotice = message;
            TradeNoticeUntil = Time.unscaledTime + 5f;
            GameProgressBridge.SetStatus(message);
        }
    }
}
