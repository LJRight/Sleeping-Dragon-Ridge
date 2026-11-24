using System;
using UnityEngine;
using static CombatAnimationTiming;

public partial class PlayerCombat
{
    private struct BasicAttackTiming
    {
        public float hitOpen, recoveryOpen;
        public double hitEndExclusive;
    }

    // Clip marker seconds are serialized as floats, whereas the server clock uses doubles.
    private const double MarkerTimeTolerance = 1e-6d;
    private readonly BasicAttackTiming[] basicTimings = new BasicAttackTiming[3];
    private double lastBasicHitSample;

    private void ReadAttackEvents()
    {
        for (int i = 0; i < basicTimings.Length; i++)
        {
            var clip = attackClips != null && i < attackClips.Length ? attackClips[i] : null;
            var markers = clip ? clip.events : Array.Empty<AnimationEvent>();
            float duration = clip ? clip.length : 0f;
            float open = MarkerTime(markers, nameof(ComboAnimationEvents.HitWindowOpen), duration);
            float close = MarkerTime(markers, nameof(ComboAnimationEvents.HitWindowClose), duration);
            float recovery = MarkerTime(markers, nameof(ComboAnimationEvents.RecoveryOpen), duration);
            bool validHit = clip && clip.frameRate > 0f && open <= close && !float.IsInfinity(close);
            basicTimings[i] = new BasicAttackTiming
            {
                hitOpen = validHit ? open : float.PositiveInfinity,
                // HitWindowClose names the last active frame, including one-frame windows.
                hitEndExclusive = validHit ? close + 1d / clip.frameRate : 0d,
                recoveryOpen = recovery
            };
            if (!validHit)
                Debug.LogWarning($"Attack {i + 1}: assign one HitWindowOpen and one HitWindowClose in order on the clip to enable hit detection.", this);
            if (float.IsInfinity(recovery))
                Debug.LogWarning($"Attack {i + 1}: assign one RecoveryOpen on the clip for early movement and skill input.", this);

            if (i + 1 == basicTimings.Length) continue;
            transitionTimes[i] = MarkerTime(markers, nameof(ComboAnimationEvents.ComboTransitionOpen), duration);
            inputCloseTimes[i] = MarkerTime(markers, nameof(ComboAnimationEvents.ComboInputClose), duration);
            validWindows[i] = transitionTimes[i] < inputCloseTimes[i] && !float.IsInfinity(inputCloseTimes[i]);
            if (!validWindows[i])
                Debug.LogWarning($"Attack {i + 1}: add one ComboTransitionOpen followed by one ComboInputClose on the clip to enable chaining.", this);
        }
    }

    internal float HitWindowStart(int stage) => basicTimings[stage - 1].hitOpen;

    private void SampleBasicHit(int stage, double elapsed)
    {
        var timing = basicTimings[stage - 1];
        if (elapsed + MarkerTimeTolerance < timing.hitOpen || elapsed <= lastBasicHitSample) return;
        // A short window may fall entirely between updates. Sample it once on crossing.
        if (elapsed + MarkerTimeTolerance >= timing.hitEndExclusive && !double.IsNegativeInfinity(lastBasicHitSample)) return;
        lastBasicHitSample = elapsed;
        ApplyAttackHit();
    }

    internal bool CanInterruptBasic(ComboState state, double now) =>
        state.Skill == PlayerSkill.None && state.Stage >= 1 && state.Stage <= basicTimings.Length
        && now - state.StartedAt + MarkerTimeTolerance >= basicTimings[state.Stage - 1].recoveryOpen;

    internal bool TryInterruptBasic(ComboState observed, double now)
    {
        var state = State;
        if (!CanActOnAuthority || state.SwingId != observed.SwingId || state.Stage != observed.Stage
            || !CanInterruptBasic(state, now)) return false;

        // Input can arrive before Update. Resolve the old swing up to this instant first.
        AdvanceAttack(now);
        state = State;
        if (health.IsDead || state.SwingId != observed.SwingId || state.Skill != PlayerSkill.None) return false;
        if (state.Stage != 0) EndAttack(state);
        return true;
    }

    internal void CancelBasicForMovement()
    {
        TryInterruptBasic(State, EstimatedServerTime);
    }
}
