using System;
using System.Text;
using Unity.Netcode;
using UnityEngine;

public partial class PlayerCombat
{
    private uint sentSnapshot;
    private uint receivingSnapshot;
    private byte[] receivingBytes;
    private int receivedLength;
    private int restoredAttack;
    private const int ChunkBytes = 768;
    private const int MaxSnapshot = 2 * 1024 * 1024;

    internal void ApplyProgressStats(GameProgressBridge.Stats stats)
    {
        restoredAttack = Mathf.Max(1, stats.attack);
        health.ApplyEquipment(stats.hp, stats.defense);
    }

    internal void SendProgressRequest(string kind, string target, string npc)
    {
        if (IsSpawned && IsOwner && kind != null && kind.Length <= 24 && target != null &&
            target.Length <= 96 && npc != null && npc.Length <= 64)
            ProgressRequestRpc(kind, target, npc);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void ProgressRequestRpc(string kind, string target, string npc)
    {
        if (kind != null && kind.Length <= 24 && target != null && target.Length <= 96 && npc != null && npc.Length <= 64)
            GameProgressBridge.ClientRequest(this, kind, target, npc);
    }

    internal void SendProgressSnapshot(string json)
    {
        if (!IsSpawned) return;
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaxSnapshot) { SendProgressError("보유 목록이 전송 한도를 초과했습니다."); return; }
        uint sequence = ++sentSnapshot;
        for (int offset = 0; offset < bytes.Length; offset += ChunkBytes)
        {
            var chunk = new byte[Math.Min(ChunkBytes, bytes.Length - offset)];
            Buffer.BlockCopy(bytes, offset, chunk, 0, chunk.Length);
            ProgressSnapshotRpc(sequence, offset, bytes.Length, chunk);
        }
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void ProgressSnapshotRpc(uint sequence, int offset, int total, byte[] chunk)
    {
        if (total <= 0 || total > MaxSnapshot || chunk == null || chunk.Length > ChunkBytes) return;
        if (offset == 0)
        {
            receivingSnapshot = sequence;
            receivedLength = 0;
            receivingBytes = new byte[total];
        }
        if (sequence != receivingSnapshot || receivingBytes == null || receivingBytes.Length != total ||
            offset != receivedLength || chunk.Length > total - offset) return;
        Buffer.BlockCopy(chunk, 0, receivingBytes, offset, chunk.Length);
        receivedLength += chunk.Length;
        if (receivedLength < total) return;
        var json = Encoding.UTF8.GetString(receivingBytes);
        receivingBytes = null;
        GameProgressBridge.ReceiveSnapshot(json);
    }

    internal void SendProgressError(string message, bool forgeRejected = false)
    {
        if (IsSpawned) ProgressErrorRpc(message, forgeRejected);
    }

    internal void SendLootReceipt(LootReceipt receipt)
    {
        if (IsSpawned && receipt.entries?.Length > 0)
            LootReceiptRpc(JsonUtility.ToJson(receipt));
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void LootReceiptRpc(string json) => LootNotifications.Receive(JsonUtility.FromJson<LootReceipt>(json));

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    internal void ItemActionCompletedRpc(bool sold)
    {
        if (sold) GameplayUiAudio.PlayDropOrSell();
        else GameplayUiAudio.PlayEquip();
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void ProgressErrorRpc(string message, bool forgeRejected)
    {
        if (forgeRejected) ExpeditionUI.ForgeRejected();
        GameProgressBridge.SetStatus(message);
    }
}
