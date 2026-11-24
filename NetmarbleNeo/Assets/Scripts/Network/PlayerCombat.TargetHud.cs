using Unity.Netcode;

public partial class PlayerCombat
{
    private const ulong NoHudTarget = ulong.MaxValue;
    private readonly NetworkVariable<ulong> hudTargetId =
        new(NoHudTarget, NetworkVariableReadPermission.Owner);
    private NetworkHealth selectedHudTarget;
    private bool automaticHudTarget;

    internal NetworkHealth HudTarget
    {
        get
        {
            if (!IsActive || health.IsDead) return null;
            var target = selectedHudTarget;
            if (!IsLocalTest && !IsServer)
            {
                if (!IsOwner || hudTargetId.Value == NoHudTarget ||
                    !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(hudTargetId.Value, out var actor) ||
                    !actor.TryGetComponent(out target)) return null;
            }
            return target && target.CanBeTargeted ? target : null;
        }
    }

    private void SetHudTarget(NetworkHealth target, bool automatic = false)
    {
        selectedHudTarget = target;
        automaticHudTarget = automatic;
        if (!IsLocalTest && IsServer && IsSpawned)
        {
            ulong id = target ? target.NetworkObjectId : NoHudTarget;
            if (hudTargetId.Value != id) hudTargetId.Value = id;
        }
    }

    private void MaintainHudTarget()
    {
        if (ReferenceEquals(selectedHudTarget, null)) return;
        // Automatic combat and quest travel each maintain their own detection range.
        if (health.IsDead || !selectedHudTarget || !selectedHudTarget.CanBeTargeted ||
            (!automaticHudTarget && !InDetectionRange(selectedHudTarget, manualAttackDetectionRadius, false)))
            SetHudTarget(null);
    }
}
