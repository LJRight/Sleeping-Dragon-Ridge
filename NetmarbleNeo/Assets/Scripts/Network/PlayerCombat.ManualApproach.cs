using UnityEngine;

public partial class PlayerCombat
{
    [SerializeField, Min(0.1f)] private float manualAttackDetectionRadius = 6f;
    private const double ManualApproachTimeout = 3d;
    private NetworkHealth manualTarget;
    private PlayerSkill approachSkill;
    private int approachStage;
    private double approachDeadline;
    private bool nextSwingAssisted;

    internal bool HasManualApproach => approachStage != 0 || approachSkill != PlayerSkill.None;

    private void BeginManualAttack(PlayerSkill skill, int stage)
    {
        // Repeated taps while travelling do not restart the chase or spend a cooldown.
        if (HasManualApproach && skill == approachSkill && stage == approachStage) return;
        manualTarget = null;
        approachSkill = skill;
        approachStage = stage;
        approachDeadline = EstimatedServerTime + ManualApproachTimeout;
        ManualApproachDirection();
    }

    internal void CancelManualApproach(bool keepHudTarget = false)
    {
        if (HasManualApproach && !keepHudTarget) SetHudTarget(null);
        manualTarget = null;
        approachSkill = PlayerSkill.None;
        approachStage = 0;
    }

    // Called on input for an immediate cast, then on authority movement ticks only while approaching.
    internal Vector3 ManualApproachDirection()
    {
        if (!CanActOnAuthority || !HasManualApproach)
        {
            CancelManualApproach();
            return Vector3.zero;
        }
        if (!InDetectionRange(manualTarget, manualAttackDetectionRadius, false) || !HasClearApproach(manualTarget))
            manualTarget = FindNearestTarget(manualAttackDetectionRadius, false);
        SetHudTarget(manualTarget);

        var skill = approachSkill;
        int stage = approachStage;
        double now = EstimatedServerTime;
        if (manualTarget)
        {
            Vector3 direction = GetApproachDirection(manualTarget, skill, out Quaternion facing);
            if (direction != Vector3.zero)
            {
                // Finish the old swing before following the target for a queued combo hit.
                if (State.Stage != 0) EndAttack(State);
                // A blocked approach or a return to manual movement must not spend the skill.
                if (movement.ManualMovementHeld || now >= approachDeadline)
                {
                    CancelManualApproach();
                    return Vector3.zero;
                }
                return direction;
            }
            transform.rotation = facing;
        }
        CancelManualApproach(keepHudTarget: true);
        if (skill == PlayerSkill.None) StartSwing(stage, now);
        else StartSkill(skill, now);
        return Vector3.zero;
    }

    private Vector3 GetApproachDirection(NetworkHealth target, PlayerSkill skill, out Quaternion facing)
    {
        Vector3 aimPoint = ClosestTargetPoint(target, transform.position);
        if (aimPoint == transform.position && target.AttackHitBox) aimPoint = target.AttackHitBox.bounds.center;
        Vector3 toTarget = Vector3.ProjectOnPlane(aimPoint - transform.position, Vector3.up);
        GetAttackArea(skill, out Vector3 origin, out Vector3 forward, out float range, out float width);
        if (toTarget.sqrMagnitude > .0001f) forward = toTarget.normalized;
        facing = Quaternion.LookRotation(forward);
        if (skill == PlayerSkill.JumpAttack)
        {
            // Aim the actual landing centre, including the prefab's effect anchor and lateral root travel.
            Vector3 landingOffset = InitialJumpImpactOffset();
            Vector3 horizontal = Vector3.ProjectOnPlane(landingOffset, Vector3.up);
            if (horizontal.sqrMagnitude > .0001f)
                facing = Quaternion.AngleAxis(Vector3.SignedAngle(horizontal, forward, Vector3.up), Vector3.up) * transform.rotation;
            origin = transform.position + (facing * Quaternion.Inverse(transform.rotation)) * landingOffset;
        }
        if (OverlapsAttack(target, skill, origin, forward, range, width)) return Vector3.zero;
        Vector3 offset = ClosestTargetPoint(target, origin) - origin;
        // For a close jump target the projected landing may overshoot, so the correct step is backward.
        return Vector3.ProjectOnPlane(offset, Vector3.up).normalized;
    }
}
