using Unity.Netcode;
using UnityEngine;

public partial class PlayerCombat
{
    [SerializeField, Min(0.1f)] private float autoCombatDetectionRadius = 15f;
    private const float AutoScanInterval = 0.2f;
    private readonly NetworkVariable<bool> autoCombat = new(false, NetworkVariableReadPermission.Owner);
    private readonly RaycastHit[] sightHits = new RaycastHit[16];
    private bool localAutoCombat;
    private PlayerSkill manualSkillPending;
    private NetworkHealth autoTarget;
    private double nextAutoScan;

    public bool AutoCombatEnabled => IsLocalTest ? localAutoCombat : IsSpawned && autoCombat.Value;
    private bool InAutoCombatArea => IsLocalTest || AreaId == SceneWorld.FieldArea || InBossArena;
    public bool CanToggleAutoCombat => IsActive && (IsLocalTest || IsOwner && IsClient)
        && InAutoCombatArea && !health.IsDead && !IsSpectatingBoss && !ExpeditionUI.BlocksGameplay;

    public void ToggleAutoCombat()
    {
        if (!CanToggleAutoCombat) return;
        if (IsLocalTest) SetAutoCombat(!localAutoCombat);
        else ToggleAutoCombatRpc(TravelEpoch);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void ToggleAutoCombatRpc(uint epoch)
    {
        if (epoch == TravelEpoch && CanActOnAuthority && InAutoCombatArea && !IsSpectatingBoss)
            SetAutoCombat(!autoCombat.Value);
    }

    private void SetAutoCombat(bool enabled)
    {
        if (IsLocalTest) localAutoCombat = enabled;
        else autoCombat.Value = enabled;
        autoTarget = null;
        SetHudTarget(null);
        nextAutoScan = 0;
        manualSkillPending = PlayerSkill.None;
    }

    internal void StopAutoCombat()
    {
        if (AutoCombatEnabled) SetAutoCombat(false);
    }

    // Called only by the authoritative movement step, including the offline test scene.
    internal Vector3 AutoCombatDirection()
    {
        var state = State;
        double now = EstimatedServerTime;
        if (manualSkillPending != PlayerSkill.None)
        {
            if (state.Stage == 0 || CanInterruptBasic(state, now))
            {
                var skill = manualSkillPending;
                manualSkillPending = PlayerSkill.None;
                ProcessSkillRequest(skill, state.SwingId);
            }
            return Vector3.zero;
        }
        if (state.Skill != PlayerSkill.None) return Vector3.zero;

        if (autoTarget && !InDetectionRange(autoTarget, autoCombatDetectionRadius, Hunting))
        {
            autoTarget = null;
            nextAutoScan = 0;
        }
        if (now >= nextAutoScan)
        {
            if (!autoTarget || !HasClearApproach(autoTarget))
                autoTarget = FindNearestTarget(autoCombatDetectionRadius, Hunting);
            nextAutoScan = now + AutoScanInterval;
        }
        // Quest travel can select a destination monster beyond the combat detection radius.
        if (autoTarget || !Hunting) SetHudTarget(autoTarget, automatic: true);
        if (!autoTarget) return Vector3.zero;

        Vector3 step = GetApproachDirection(autoTarget, PlayerSkill.None, out var facing);
        if (step != Vector3.zero) return state.Stage == 0 ? step : Vector3.zero;
        if (state.Stage == 0)
        {
            transform.rotation = facing;
            ProcessAttack(state.SwingId, 0);
        }
        else if (CanQueueNext(state, now) && OverlapsAttack(autoTarget, PlayerSkill.None,
            transform.position, transform.forward, attackRange, 0f))
        {
            int index = state.Stage - 1;
            float hitTime = HitWindowStart(state.Stage);
            // Reserve at the transition itself, after this hit when the authored window allows it.
            float queueTime = hitTime < inputCloseTimes[index]
                ? Mathf.Max(transitionTimes[index], hitTime) : transitionTimes[index];
            if (now - state.StartedAt >= queueTime) ProcessAttack(state.SwingId, state.Stage);
        }
        return Vector3.zero;
    }

    private bool InDetectionRange(NetworkHealth candidate, float radius, bool questTargetsOnly) =>
        candidate && candidate.EntityKind == NetworkHealth.Kind.Monster && candidate.CanBeTargeted
        && (!questTargetsOnly || MatchesQuestMonster(candidate))
        && (!candidate.AttackHitBox || ActiveHitBox(candidate.AttackHitBox))
        && (ClosestTargetPoint(candidate, transform.position) - transform.position).sqrMagnitude <= radius * radius
        && health.SharesCombatArea(candidate);

    private NetworkHealth FindNearestTarget(float radius, bool questTargetsOnly)
    {
        NetworkHealth nearest = null;
        float bestDistance = float.PositiveInfinity;
        var actors = NetworkHealth.Spawned;
        for (int i = 0; i < actors.Count; i++)
        {
            var candidate = actors[i];
            if (!InDetectionRange(candidate, radius, questTargetsOnly)) continue;
            float distance = (ClosestTargetPoint(candidate, transform.position) - transform.position).sqrMagnitude;
            if (distance >= bestDistance || !HasClearApproach(candidate)) continue;
            bestDistance = distance;
            nearest = candidate;
        }
        return nearest;
    }

    private bool HasClearApproach(NetworkHealth candidate)
    {
        Vector3 origin = transform.position; // Player pivot is at the centre of its capsule.
        Vector3 sightPoint = ClosestTargetPoint(candidate, origin);
        if (!candidate.AttackHitBox && candidate.TryGetComponent<CharacterController>(out var body) && body.enabled)
            sightPoint = body.bounds.center;
        Vector3 delta = sightPoint - origin;
        float distance = delta.magnitude;
        if (distance < .001f) return true;
        int count = Physics.RaycastNonAlloc(origin, delta / distance, sightHits, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        var hits = sightHits;
        if (count == hits.Length)
        {
            hits = Physics.RaycastAll(origin, delta / distance, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }
        for (int i = 0; i < count; i++)
            if (!hits[i].collider.GetComponentInParent<NetworkHealth>()) return false;
        return true;
    }
}
