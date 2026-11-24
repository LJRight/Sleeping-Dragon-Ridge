using UnityEngine;

public partial class PlayerCombat
{
    private static bool ActiveHitBox(Collider box) => box.enabled && box.isTrigger && box.gameObject.activeInHierarchy;

    private static Vector3 ClosestTargetPoint(NetworkHealth target, Vector3 point) =>
        target.AttackHitBox ? target.AttackHitBox.ClosestPoint(point) : target.transform.position;

    private bool OverlapsAttack(NetworkHealth target, PlayerSkill skill, Vector3 origin, Vector3 forward, float range, float width)
    {
        var box = target.AttackHitBox;
        if (!box) return ContainsAttackTarget(skill, target.transform.position - origin, forward, range, width);
        if (!ActiveHitBox(box)) return false;
        if (skill == PlayerSkill.SpinningSlash || (skill == PlayerSkill.None && attackAngle >= 360f))
            return (box.ClosestPoint(origin) - origin).sqrMagnitude <= range * range;

        // The projection below is convex. Larger-than-semicircle attacks are the union of two convex sectors.
        if (skill == PlayerSkill.None && attackAngle > 180f)
        {
            float half = attackAngle * .5f;
            return new AttackVolume(skill, origin, Quaternion.AngleAxis(-half * .5f, Vector3.up) * forward,
                       range, width, half).Intersects(box)
                || new AttackVolume(skill, origin, Quaternion.AngleAxis(half * .5f, Vector3.up) * forward,
                       range, width, half).Intersects(box);
        }
        return new AttackVolume(skill, origin, forward, range, width, attackAngle).Intersects(box);
    }

    private readonly struct AttackVolume
    {
        private readonly PlayerSkill skill;
        private readonly Vector3 origin, forward, right;
        private readonly float range, halfWidth, edgeForward, edgeRight;

        internal AttackVolume(PlayerSkill skill, Vector3 origin, Vector3 forward, float range, float width, float angle)
        {
            this.skill = skill;
            this.origin = origin;
            this.forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            right = Vector3.Cross(Vector3.up, this.forward);
            this.range = range;
            halfWidth = width * .5f;
            edgeForward = Mathf.Cos(angle * .5f * Mathf.Deg2Rad);
            edgeRight = Mathf.Sin(angle * .5f * Mathf.Deg2Rad);
        }

        // Euclidean projection onto the solid used by the attack, including its height restrictions.
        private Vector3 ClosestPoint(Vector3 point)
        {
            Vector3 delta = point - origin;
            float depth = Vector3.Dot(delta, forward);
            float lateral = Vector3.Dot(delta, right);
            float height = Mathf.Clamp(delta.y, -range, range);
            if (skill == PlayerSkill.Uppercut)
                return origin + forward * Mathf.Clamp(depth, 0f, range)
                    + right * Mathf.Clamp(lateral, -halfWidth, halfWidth) + Vector3.up * height;

            Vector3 planar = delta - Vector3.up * delta.y;
            if (skill == PlayerSkill.JumpAttack)
                return origin + Vector3.ClampMagnitude(planar, range) + Vector3.up * height;

            if (depth * edgeRight < Mathf.Abs(lateral) * edgeForward)
            {
                Vector3 side = forward * edgeForward + right * (Mathf.Sign(lateral) * edgeRight);
                planar = side * Mathf.Max(0f, Vector3.Dot(planar, side));
            }
            return origin + Vector3.ClampMagnitude(planar + Vector3.up * delta.y, range);
        }

        internal bool Intersects(Collider box)
        {
            if (box is CapsuleCollider capsule)
            {
                var pose = capsule.transform;
                Vector3 scale = pose.lossyScale;
                int axis = capsule.direction;
                float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale[(axis + 1) % 3]), Mathf.Abs(scale[(axis + 2) % 3]));
                float halfSegment = Mathf.Max(0f, capsule.height * Mathf.Abs(scale[axis]) * .5f - radius);
                Vector3 direction = axis == 0 ? pose.right : axis == 1 ? pose.up : pose.forward;
                Vector3 center = pose.TransformPoint(capsule.center);
                return IntersectsSweptSphere(center, direction, halfSegment, radius);
            }
            if (box is SphereCollider sphere)
            {
                Vector3 scale = sphere.transform.lossyScale;
                float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                Vector3 center = sphere.transform.TransformPoint(sphere.center);
                return (center - ClosestPoint(center)).sqrMagnitude <= radius * radius;
            }

            // A replacement box or convex mesh trigger exposes its own collision surface through ClosestPoint.
            Vector3 point = box.ClosestPoint(origin);
            for (int i = 0; i < 32; i++)
            {
                Vector3 onAttack = ClosestPoint(point);
                if ((point - onAttack).sqrMagnitude <= 0.000001f) return true;
                Vector3 onTarget = box.ClosestPoint(onAttack);
                if ((onTarget - point).sqrMagnitude < 1e-12f) return false;
                point = onTarget;
            }
            return (point - ClosestPoint(point)).sqrMagnitude <= 0.000001f;
        }

        // A capsule touches the attack if any point of its centre segment comes within its radius.
        // The slope of squared distance to a convex solid along a line is monotone, so bisect its minimum.
        private bool IntersectsSweptSphere(Vector3 center, Vector3 axis, float halfSegment, float radius)
        {
            float lo = -halfSegment, hi = halfSegment;
            float radiusSquared = radius * radius;
            for (int i = 0; i < 18; i++)
            {
                float mid = (lo + hi) * .5f;
                Vector3 sample = center + axis * mid;
                Vector3 delta = sample - ClosestPoint(sample);
                if (delta.sqrMagnitude <= radiusSquared) return true;
                float slope = Vector3.Dot(delta, axis);
                // The plane through the projection separates the entire remaining interval in common misses.
                if (delta.sqrMagnitude - Mathf.Abs(slope) * (hi - lo) * .5f > radius * delta.magnitude) return false;
                if (slope > 0f) hi = mid;
                else lo = mid;
            }
            Vector3 nearest = center + axis * ((lo + hi) * .5f);
            float toleranceRadius = radius + (hi - lo) * .5f;
            return (nearest - ClosestPoint(nearest)).sqrMagnitude <= toleranceRadius * toleranceRadius;
        }
    }
}
