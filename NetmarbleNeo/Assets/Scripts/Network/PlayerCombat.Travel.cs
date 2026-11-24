using Unity.Netcode;
using UnityEngine;

public partial class PlayerCombat
{
    private const float RespawnDelay = 2.5f;
    private readonly NetworkVariable<int> bossRoom = new(0);
    private readonly NetworkVariable<uint> travelEpoch = new(0);
    private readonly NetworkVariable<bool> spectatingBoss = new(false);
    private double reviveAt;
    private int displayedRoom;
    // Zero is the village, -1 is the public field, and positive values identify separate boss attempts.
    internal int AreaId => IsSpawned ? bossRoom.Value : 0;
    public int BossRoomId => Mathf.Max(0, AreaId);
    public string MapSceneName => SceneWorld.NameOf(AreaId);
    internal int ReturnArea { get; set; }
    public bool CanUseMapGate => !InBossArena && health && !health.IsDead && SceneWorld.AtMarker(MapSceneName,
        AreaId == SceneWorld.FieldArea ? "Village Return" : "Field departure", transform.position);
    public bool CanRecallToVillage => AreaId == SceneWorld.FieldArea && health && !health.IsDead;

    public void RequestMapGate()
    {
        if (IsSpawned && IsOwner && CanUseMapGate) MapGateRpc(TravelEpoch);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void MapGateRpc(uint epoch) => UseMapGateOnServer(epoch);

    public void RequestVillageRecall()
    {
        if (IsSpawned && IsOwner && CanRecallToVillage) RecallToVillageRpc(TravelEpoch);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RecallToVillageRpc(uint epoch) => UseMapGateOnServer(epoch, recallToVillage: true);

    private void UseMapGateOnServer(uint epoch, bool recallToVillage = false)
    {
        if (TravelEpoch != epoch || !GameProgressBridge.Restored(this) || BossPartyServer.Entering(this)) return;
        if (recallToVillage ? !CanRecallToVillage : !CanUseMapGate) return;
        bool departingVillage = AreaId == 0;
        int area = departingVillage ? SceneWorld.FieldArea : 0;
        var arrival = SceneWorld.Position(SceneWorld.NameOf(area), departingVillage ? "Field Arrival" : "Arrival from the field");
        TravelOnServer(arrival + Vector3.up, area);
    }

    public bool InBossArena => BossRoomId != 0;
    public bool IsSpectatingBoss => InBossArena && spectatingBoss.Value;
    public uint TravelEpoch => travelEpoch.Value;
    internal Vector3 FieldReturnPoint { get; set; }

    private void ObserveServerDamage(NetworkHealth _)
    {
        if (!health.IsDead || reviveAt > 0 || IsSpectatingBoss) return;
        ResetMovementAndCombo();
        if (BossPartyServer.SpectateOnDeath(this)) spectatingBoss.Value = true;
        else reviveAt = EstimatedServerTime + RespawnDelay;
    }

    private void RespawnIfDue(double now)
    {
        if (reviveAt <= 0 || now < reviveAt || !health.IsDead) return;
        reviveAt = 0;
        TravelOnServer(BossArena.Town, 0);
        health.ReviveOnServer();
    }

    internal void TravelOnServer(Vector3 destination, int room)
    {
        if (!IsServer || !IsSpawned) return;
        int previous = bossRoom.Value;
        if (previous != room) spectatingBoss.Value = false;
        bossRoom.Value = room;
        ResetMovementAndCombo();
        GetComponent<InputHandler>().TeleportOnServer(BossArena.FreeLanding(destination, this));
        if (previous != room) BossPartyServer.RoomChanged(this, previous);
    }

    // Bump both input stamps so delayed RPCs from the previous position or life cannot replay.
    private void ResetMovementAndCombo()
    {
        SetHudTarget(null);
        StopAutomaticActions();
        GameProgressBridge.InterruptTrade(this);
        travelEpoch.Value = unchecked(travelEpoch.Value + 1);
        var state = combo.Value;
        state.SwingId = unchecked(state.SwingId + 1);
        state.Stage = 0;
        state.StartedAt = 0d;
        state.NextQueued = false;
        state.Skill = PlayerSkill.None;
        state.JumpCenter = default;
        state.JumpLanded = false;
        combo.Value = state; // Cooldowns survive region changes and death.
        queuedAt = 0;
        hitApplied = false;
        hitTargets.Clear();
        GetComponent<InputHandler>().ResetMovementOnServer();
    }

    private void OnBossRoomChanged(int previous, int current)
    {
        if (!IsOwner || !IsClient) return;
        if (VictoryRoom != 0 && previous == VictoryRoom && current != VictoryRoom)
        {
            VictoryRoom = 0;
            GameProgressBridge.SetStatus("수문장 처치! 마을로 복귀했습니다.");
        }
        if (IsServer) return;
        BossArenaLandmarks.RemoveRoom(displayedRoom);
        displayedRoom = current;
        BossArenaLandmarks.ShowRoom(displayedRoom);
    }

    internal void ReturnFromBossAttempt()
    {
        reviveAt = 0;
        health.ReviveOnServer();
        TravelOnServer(BossArena.Town, 0);
    }

    internal int VictoryRoom { get; private set; }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    internal void AnnounceBossVictoryRpc(int room)
    {
        if (VictoryRoom == room) return;
        VictoryRoom = room;
        GameplayUiAudio.PlayBossVictory();
    }

    private void OnTravelEpochChanged(uint previous, uint current)
    {
        if (IsOwner && IsClient) GetComponent<InputHandler>().ReleaseTravelInput();
    }
}
