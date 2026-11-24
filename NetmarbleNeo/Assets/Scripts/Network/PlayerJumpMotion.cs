using UnityEngine;

public sealed class PlayerJumpMotion : ScriptableObject
{
    // Cumulative, evenly spaced samples in the character root's local XZ plane.
    [SerializeField, HideInInspector] private Vector2[] samples;

    internal Vector3 Offset(float phase)
    {
        if (samples == null || samples.Length < 2) return Vector3.zero;
        int last = samples.Length - 1;
        float frame = Mathf.Clamp01(phase) * last;
        int first = Mathf.Min(Mathf.FloorToInt(frame), last - 1);
        Vector2 offset = Vector2.LerpUnclamped(samples[first], samples[first + 1], frame - first);
        return new Vector3(offset.x, 0f, offset.y);
    }

#if UNITY_EDITOR
    internal void SetSamples(Vector2[] baked) => samples = baked;
#endif
}
