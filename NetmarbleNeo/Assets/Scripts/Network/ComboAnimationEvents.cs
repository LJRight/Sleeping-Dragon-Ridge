using UnityEngine;

// Must live on the Animator's GameObject so clip events have a receiver.
// The authoritative clock in PlayerCombat reads these markers from AnimationClip.events.
// Callbacks intentionally do not mutate combat: blended-out clips can still emit events,
// and dedicated servers do not evaluate the Animator at all.
[DisallowMultipleComponent]
public sealed class ComboAnimationEvents : MonoBehaviour
{
    public void HitWindowOpen() { }
    public void HitWindowClose() { }
    public void RecoveryOpen() { }
    public void ComboTransitionOpen() { }
    public void ComboInputClose() { }
}

internal static class CombatAnimationTiming
{
    // Infinity disables a missing, duplicated, or out-of-clip marker.
    internal static float MarkerTime(AnimationEvent[] markers, string name, float duration)
    {
        float time = float.PositiveInfinity;
        foreach (var marker in markers)
        {
            if (marker.functionName != name) continue;
            if (!float.IsInfinity(time) || !(marker.time >= 0f && marker.time <= duration))
                return float.PositiveInfinity;
            time = marker.time;
        }
        return time;
    }
}
