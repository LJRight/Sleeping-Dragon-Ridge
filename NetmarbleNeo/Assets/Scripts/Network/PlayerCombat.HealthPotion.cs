using System;
using Unity.Netcode;
using UnityEngine;

public partial class PlayerCombat
{
    private readonly NetworkVariable<double> potionReadyAt = new(0d, NetworkVariableReadPermission.Owner);
    private double localPotionReadyAt;
    private double PotionReadyAt => IsLocalTest ? localPotionReadyAt : potionReadyAt.Value;

    private void OnPotionCooldownChanged(double previous, double current)
    {
        // The server advances this owner-only cooldown only after restoring health.
        if (current > previous) GameplayUiAudio.PlayPotionDrink();
    }

    public float HealthPotionCooldownRemaining => IsActive
        ? (float)Math.Max(0d, PotionReadyAt - EstimatedServerTime) : 0f;

    public bool CanUseHealthPotion => IsActive && (IsLocalTest || IsClient && IsOwner && GameProgressBridge.Ready)
        && !ExpeditionUI.BlocksGameplay && !health.IsDead && health.Current < health.Maximum
        && HealthPotionCooldownRemaining <= 0f;

    public void RequestHealthPotion()
    {
        if (!CanUseHealthPotion) return;
        if (IsLocalTest) UseHealthPotion();
        else UseHealthPotionRpc(TravelEpoch, PotionReadyAt);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void UseHealthPotionRpc(uint epoch, double observedReadyAt)
    {
        if (epoch == TravelEpoch && observedReadyAt == PotionReadyAt) UseHealthPotion();
    }

    private void UseHealthPotion()
    {
        if (!CanActOnAuthority || IsSpectatingBoss) return;
        double now = EstimatedServerTime;
        if (now < PotionReadyAt) return;
        int amount = Mathf.Max(1, Mathf.RoundToInt(health.Maximum * .4f));
        if (health.HealOnAuthority(amount) <= 0) return;

        double nextUse = now + (InBossArena ? 30d : 10d);
        if (IsLocalTest)
        {
            localPotionReadyAt = nextUse;
            GameplayUiAudio.PlayPotionDrink();
        }
        else
        {
            potionReadyAt.Value = nextUse;
            GameProgressBridge.Tutorial(this, "use_potion");
        }
    }
}
