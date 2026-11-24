using System;
using Unity.Netcode;
using UnityEngine;
using static CombatAnimationTiming;

public enum PlayerSkill : byte
{
    None, SpinningSlash, Uppercut, JumpAttack
}

public partial class PlayerCombat
{
    [SerializeField, Min(1f)] private float spinningSlashCooldown = 5f;

    [Header("Uppercut")]
    [SerializeField] private AnimationClip uppercutClip;
    [SerializeField, Min(0f)] private float uppercutHitTime = 0.65f;
    [SerializeField, Min(0.1f)] private float uppercutLength = 3.5f;
    [SerializeField, Min(0.1f)] private float uppercutWidth = 2f;
    [SerializeField, Min(0.1f)] private float uppercutCooldown = 6f;

    [Header("Jump attack")]
    [SerializeField] private AnimationClip jumpAttackClip;
    [SerializeField] private Transform jumpAttackEffectAnchor;
    [SerializeField, Min(0.1f)] private float jumpAttackRadius = 2f;
    [SerializeField, Min(0.1f)] private float jumpAttackCooldown = 8f;

    private float jumpAttackHitTime;
    private Vector3 jumpImpactOffsetAtCast;
    private float jumpTravelTime;
    private Vector3 lastJumpOffset;

    private void ReadSkillTiming()
    {
        const float defaultLandingPhase = 106f / 220f;
        float marker = jumpAttackClip
            ? MarkerTime(jumpAttackClip.events, nameof(JumpAttack.PlayJumpAttackEffect), jumpAttackClip.length)
            : float.PositiveInfinity;
        jumpAttackHitTime = float.IsInfinity(marker)
            ? GetSkillDuration(PlayerSkill.JumpAttack) * defaultLandingPhase : marker;
    }

    public PlayerSkill CurrentSkill => IsActive ? State.Skill : PlayerSkill.None;
    public bool IsSpinningSlash => CurrentSkill == PlayerSkill.SpinningSlash;
    public float SpinningSlashDuration => GetSkillDuration(PlayerSkill.SpinningSlash);
    public float SpinningSlashCooldownDuration => SkillCooldownDuration(PlayerSkill.SpinningSlash);
    public float SpinningSlashCooldownRemaining => SkillCooldownRemaining(PlayerSkill.SpinningSlash);
    public bool CanRequestSpinningSlash => CanRequestSkill(PlayerSkill.SpinningSlash);
    public void RequestSpinningSlash() => RequestSkill(PlayerSkill.SpinningSlash);

    public float GetSkillDuration(PlayerSkill skill) => skill switch
    {
        PlayerSkill.SpinningSlash => 1f,
        PlayerSkill.Uppercut => uppercutClip ? uppercutClip.length : 1.9f,
        PlayerSkill.JumpAttack => jumpAttackClip ? jumpAttackClip.length : 220f / 60f,
        _ => 0f
    };

    public float GetSkillHitTime(PlayerSkill skill) => skill switch
    {
        PlayerSkill.SpinningSlash => 0.37f,
        PlayerSkill.Uppercut => Mathf.Clamp(uppercutHitTime, 0f, GetSkillDuration(skill)),
        PlayerSkill.JumpAttack => Mathf.Min(jumpAttackHitTime, GetSkillDuration(skill)),
        _ => 0f
    };

    public float SkillCooldownDuration(PlayerSkill skill) => skill switch
    {
        PlayerSkill.SpinningSlash => Mathf.Max(SpinningSlashDuration, spinningSlashCooldown),
        PlayerSkill.Uppercut => uppercutCooldown,
        PlayerSkill.JumpAttack => jumpAttackCooldown,
        _ => 0f
    };

    public float SkillCooldownRemaining(PlayerSkill skill) => IsActive
        ? (float)Math.Max(0d, State.ReadyAt(skill) - EstimatedServerTime) : 0f;

    public bool CanRequestSkill(PlayerSkill skill) => IsUsableSkill(skill) && CanRequestAttack
        && (State.Stage == 0 || AutoCombatEnabled || CanInterruptBasic(State, EstimatedServerTime))
        && (IsLocalTest || GameProgressBridge.Ready) && !ExpeditionUI.BlocksGameplay
        && SkillCooldownRemaining(skill) <= 0f;

    private static bool IsUsableSkill(PlayerSkill skill) => skill is
        PlayerSkill.SpinningSlash or PlayerSkill.Uppercut or PlayerSkill.JumpAttack;

    public void RequestSkill(PlayerSkill skill)
    {
        if (!CanRequestSkill(skill)) return;
        uint observedSwingId = State.SwingId;
        if (IsLocalTest) ProcessSkillRequest(skill, observedSwingId);
        else UseSkillRpc(skill, observedSwingId, TravelEpoch);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void UseSkillRpc(PlayerSkill skill, uint observedSwingId, uint epoch)
    {
        if (epoch == TravelEpoch) ProcessSkillRequest(skill, observedSwingId);
    }

    private void ProcessSkillRequest(PlayerSkill skill, uint observedSwingId)
    {
        if (!IsUsableSkill(skill) || !CanActOnAuthority || manualSkillPending != PlayerSkill.None) return;
        var state = State;
        double now = EstimatedServerTime;
        if (state.Skill != PlayerSkill.None || state.SwingId != observedSwingId || now < state.ReadyAt(skill)) return;
        if (state.Stage > 0)
        {
            bool canInterrupt = CanInterruptBasic(state, now);
            if (!canInterrupt && !AutoCombatEnabled) return;
            if (AutoCombatEnabled && state.NextQueued)
            {
                state.NextQueued = false;
                State = state;
            }
            if (!canInterrupt)
            {
                manualSkillPending = skill;
                return;
            }
            if (!TryInterruptBasic(state, now)) return;
        }

        BeginManualAttack(skill, 0);
    }

    private void StartSkill(PlayerSkill skill, double now)
    {
        var state = State;
        state.Stage = 0;
        state.NextQueued = false;
        state.Skill = skill;
        if (skill == PlayerSkill.JumpAttack)
        {
            jumpImpactOffsetAtCast = InitialJumpImpactOffset();
            state.JumpCenter = transform.position + jumpImpactOffsetAtCast;
            state.JumpLanded = false;
            jumpTravelTime = 0f;
            lastJumpOffset = Vector3.zero;
        }
        state.SwingId = unchecked(state.SwingId + 1);
        state.StartedAt = now;
        state.SetReadyAt(skill, now + SkillCooldownDuration(skill));
        hitApplied = false;
        hitTargets.Clear();
        State = state;
        if (!IsLocalTest) GameProgressBridge.Tutorial(this, "use_skill");
    }

    private void AdvanceSkill(ComboState state, double elapsed)
    {
        bool jump = state.Skill == PlayerSkill.JumpAttack;
        float hitTime = GetSkillHitTime(state.Skill);
        float duration = GetSkillDuration(state.Skill);
        if (!hitApplied && elapsed + MarkerTimeTolerance >= hitTime)
        {
            if (jump)
            {
                AdvanceJumpTravel(hitTime);
                RecordJumpLanding(ref state);
            }
            hitApplied = true;
            ApplyAttackHit();
            // Reflection or a boss kill may end this cast or teleport its owner during damage.
            if (health.IsDead || State.SwingId != state.SwingId || State.Skill != state.Skill) return;
        }
        if (jump)
        {
            AdvanceJumpTravel(Mathf.Min((float)elapsed, duration));
            if (!hitApplied) RefreshJumpTelegraph(ref state, jumpTravelTime);
        }
        if (elapsed >= duration)
            EndAttack(state);
    }

    public Vector3 JumpTravelWorldOffset(float elapsed)
    {
        // Normal steering cannot rotate the root during a skill. Match the authored local axes.
        Vector3 offset = transform.TransformVector(JumpPoseOffset(elapsed));
        offset.y = 0f;
        return offset;
    }

    private void AdvanceJumpTravel(float elapsed)
    {
        // Move along the curve even when a server frame spans both a lunge and its recovery.
        // A blocked step is consumed, so it cannot push the player past a wall on later frames.
        while (jumpTravelTime < elapsed)
        {
            jumpTravelTime = Mathf.Min(elapsed, jumpTravelTime + Time.fixedDeltaTime);
            Vector3 offset = JumpTravelWorldOffset(jumpTravelTime);
            // Carry sub-threshold increments into the next sweep without reconfiguring the collider.
            if (movement.TryMoveForJump(offset - lastJumpOffset)) lastJumpOffset = offset;
        }
    }

    // Rectangles extend forward from origin. Circles are centred on origin and use range as radius.
    public bool TryGetSkillArea(out Vector3 origin, out Vector3 forward, out float range, out float width)
    {
        var skill = CurrentSkill;
        GetAttackArea(skill, out origin, out forward, out range, out width);
        return skill != PlayerSkill.None;
    }

    private void GetAttackArea(PlayerSkill skill, out Vector3 origin, out Vector3 forward, out float range, out float width)
    {
        origin = transform.position;
        if (skill == PlayerSkill.JumpAttack)
            origin = GetJumpImpactCenter();
        forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        width = skill == PlayerSkill.Uppercut ? uppercutWidth : 0f;
        range = skill switch
        {
            PlayerSkill.Uppercut => uppercutLength,
            PlayerSkill.JumpAttack => jumpAttackRadius,
            _ => attackRange
        };
    }

    public Vector3 GetJumpImpactCenter() => State.JumpCenter;

    private Vector3 InitialJumpImpactOffset()
    {
        Vector3 anchorOffset = jumpAttackEffectAnchor ? jumpAttackEffectAnchor.position - transform.position : Vector3.zero;
        return JumpTravelWorldOffset(GetSkillHitTime(PlayerSkill.JumpAttack)) + anchorOffset;
    }

    // Aim at the remaining travel plus the effect's authored offset from the landing pose.
    private Vector3 JumpCenterAt(float elapsed) =>
        transform.position + jumpImpactOffsetAtCast - JumpTravelWorldOffset(elapsed);

    private void RefreshJumpTelegraph(ref ComboState state, float elapsed)
    {
        Vector3 center = JumpCenterAt(elapsed);
        if ((center - state.JumpCenter).sqrMagnitude < 0.0001f) return;
        state.JumpCenter = center;
        State = state;
    }

    private void RecordJumpLanding(ref ComboState state)
    {
        state.JumpCenter = JumpCenterAt(GetSkillHitTime(PlayerSkill.JumpAttack));
        state.JumpLanded = true;
        State = state;
    }
}
