using UnityEngine;

// A snapshot of the server-timed attack footprint in world space. Width and angle are full spans.
public struct BossAttackCue
{
    public Vector3 origin, forward;
    public float range, width, angle, fill;
    public bool rectangle, danger;
}
