#ifndef RIDGE_ATTACK_RANGE_MASK_INCLUDED
#define RIDGE_ATTACK_RANGE_MASK_INCLUDED

// BossCue = (shape, filled fraction, full rectangle width, active damage).
// Shape 0 uses the player's existing spherical range; 1 and 2 use planar boss hit tests.
void AttackRangeMask_float(float3 World, float4 OriginRange, float4 ForwardAngle, float4 Tint,
                           float4 BossCue, out float3 Color, out float Alpha)
{
    float3 delta = World - OriginRange.xyz;
    float range = max(OriginRange.w, 0.001);
    float2 forward = ForwardAngle.xz / max(length(ForwardAngle.xz), 0.0001);
    float distanceXZ = length(delta.xz);
    float halfAngle = radians(clamp(ForwardAngle.w, 1.0, 360.0) * 0.5);
    float3 normal = cross(ddy(World), ddx(World));
    float up = abs(normal.y) * rsqrt(max(dot(normal, normal), 1e-16));
    float floorMask = smoothstep(0.45, 0.65, up);
    Color = Tint.rgb;

    if (BossCue.x < 0.5)
    {
        float radial = length(delta);
        float angle = acos(clamp(dot(delta.xz / max(distanceXZ, 0.0001), forward), -1.0, 1.0));
        float inside = step(radial, range) * (distanceXZ < 0.0001 ? 1.0 : step(angle, halfAngle));
        float arcEdge = 1.0 - smoothstep(0.035, 0.085, range - radial);
        float sideEdge = ForwardAngle.w >= 359.9 ? 0.0 :
            1.0 - smoothstep(0.035, 0.085, distanceXZ * sin(max(0.0, halfAngle-angle)));
        Alpha = inside * floorMask * lerp(0.16, 0.85, max(arcEdge, sideEdge)) * Tint.a;
        return;
    }

    float right = dot(delta.xz, float2(forward.y, -forward.x));
    float ahead = dot(delta.xz, forward);
    float fill = saturate(BossCue.y);
    float edgeDistance, frontDistance;
    if (BossCue.x < 1.5)
    {
        edgeDistance = min(min(ahead, range - ahead), BossCue.z * 0.5 - abs(right));
        frontDistance = range * fill - ahead;
    }
    else
    {
        // Positive angles are on the boss' right. Looking down, +halfAngle to -halfAngle goes counterclockwise.
        float angle = distanceXZ > 0.0001 ? atan2(right, ahead) : 0.0;
        float angularMargin = clamp(halfAngle - abs(angle), -1.5707963, 1.5707963);
        float sideDistance = ForwardAngle.w >= 359.9 ? range : distanceXZ * sin(angularMargin);
        edgeDistance = min(range - distanceXZ, sideDistance);
        float sweepAngle = lerp(halfAngle, -halfAngle, fill);
        frontDistance = distanceXZ * sin(clamp(angle - sweepAngle, -1.5707963, 1.5707963));
    }

    float border = 1.0 - smoothstep(0.025, 0.095, edgeDistance);
    float coverage = step(0.0, edgeDistance) * floorMask * Tint.a;
    if (BossCue.w > 0.5)
    {
        // A solid hot field with diagonal stripes remains distinct from a fully charged warning.
        float stripe = smoothstep(0.75, 0.97, cos((right + ahead) * 8.0));
        Color = lerp(float3(1.0, 0.10, 0.012), float3(1.0, 0.48, 0.085), max(stripe * 0.65, border));
        Alpha = coverage * max(0.62 + 0.12 * stripe, 0.92 * border);
        return;
    }

    float leading = (fill > 0.0 && fill < 1.0) ? 1.0 - smoothstep(0.025, 0.115, abs(frontDistance)) : 0.0;
    float filled = (fill >= 1.0) ? 1.0 : (fill <= 0.0 ? 0.0 : smoothstep(-0.035, 0.035, frontDistance));
    Color = lerp(Tint.rgb, float3(1.0, 0.53, 0.31), leading);
    Alpha = coverage * max(lerp(0.065, 0.43, filled), max(border * 0.84, leading * 0.95));
}

#endif
