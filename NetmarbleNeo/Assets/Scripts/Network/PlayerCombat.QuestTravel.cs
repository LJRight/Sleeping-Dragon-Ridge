using Unity.Netcode;
using UnityEngine;

public partial class PlayerCombat
{
    private string questDestination;
    private Transform destinationMarker;
    private bool followingRoad;
    private int roadIndex, roadEnd;
    private Vector2[] questRoad;
    private Vector2 roadOrigin;
    private NetworkHealth huntingApproach;
    private double nextHuntSearch;

    public uint ManualMovementRevision => movement.ManualMovementRevision;
    internal bool HasQuestTravel => destinationMarker;
    private bool Hunting => questDestination is "A1" or "A2" or "A3" or "A4" or "B1";

    public void RequestQuestTravel(string destination)
    {
        if (IsSpawned && IsOwner && IsClient && !health.IsDead && !movement.ManualMovementHeld)
            QuestTravelRpc(destination, TravelEpoch, ManualMovementRevision);
    }

    public void StopQuestTravel()
    {
        if (IsSpawned && IsOwner && IsClient) StopQuestTravelRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void StopQuestTravelRpc() => StopAutomaticActions();

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void QuestTravelRpc(string destination, uint epoch, uint inputRevision)
    {
        if (epoch != TravelEpoch || inputRevision != ManualMovementRevision || movement.ManualMovementHeld
            || !CanActOnAuthority || InBossArena || questDestination == destination) return;
        string marker = AreaId == 0 ? destination switch
        {
            "warden" => "Warden approach", "smith" => "Smith approach", "field" => "Field departure", _ => null
        } : destination switch
        {
            "village" => "Village Return", "boss_gate" => "Boss Gate",
            "A1" => "01 A1 - Echo Hollow", "A2" => "02 A2 - Ember Scree", "A3" => "03 A3 - Venom Mire",
            "A4" => "04 A4 - Frost Crown", "B1" => "05 B1 and B1G - Tusk Heath", _ => null
        };
        if (marker == null) return;
        var target = SceneWorld.FindMarker(MapSceneName, marker);
        if (!target) return;
        StopAutoCombat();
        questDestination = destination;
        destinationMarker = target;
        huntingApproach = null;
        nextHuntSearch = 0;
        questRoad = AreaId == 0 ? QuestTravelRoute.Village : QuestTravelRoute.Field;
        roadOrigin = XZ(SceneWorld.Origin(MapSceneName));
        roadIndex = QuestTravelRoute.Nearest(questRoad, XZ(transform.position) - roadOrigin);
        roadEnd = QuestTravelRoute.Nearest(questRoad, XZ(target.position) - roadOrigin);
        followingRoad = true;
    }

    internal void StopAutomaticActions()
    {
        CancelManualApproach();
        questDestination = null;
        destinationMarker = null;
        huntingApproach = null;
        StopAutoCombat();
    }

    internal Vector3 QuestTravelDirection()
    {
        Vector2 position = XZ(transform.position);
        if (followingRoad)
        {
            Vector2 delta = roadOrigin + questRoad[roadIndex] - position;
            // The next waypoint may lie inside an actor's capsule. Pass it within the
            // tested road clearance instead of trying to walk through that actor.
            bool occupied = delta.sqrMagnitude <= 2.5f * 2.5f && delta.sqrMagnitude > .65f * .65f
                && PathBlocked(new Vector3(delta.x, 0f, delta.y).normalized, delta.magnitude);
            if (delta.sqrMagnitude > .65f * .65f && !occupied) return FollowRoadHeading(delta);
            if (roadIndex != roadEnd)
            {
                roadIndex += roadIndex < roadEnd ? 1 : -1;
                return FollowRoadHeading(roadOrigin + questRoad[roadIndex] - position);
            }
            followingRoad = false;
        }

        if (Hunting) return QuestHuntDirection();
        if ((questDestination == "village" || questDestination == "field") && CanUseMapGate)
        {
            UseMapGateOnServer(TravelEpoch);
            return Vector3.zero;
        }
        Vector2 remaining = XZ(destinationMarker.position) - position;
        return remaining.sqrMagnitude > .35f * .35f ? FollowRoadHeading(remaining) : Vector3.zero;
    }

    private Vector3 QuestHuntDirection()
    {
        if (!AutoCombatEnabled) SetAutoCombat(true);
        Vector3 direction = AutoCombatDirection();
        if (autoTarget || IsAttacking) return direction;

        // Approach an active monster in this clearing until ordinary auto combat detects it.
        // Pooled and airborne/dead targets must not pin the route to an obsolete spawn.
        if (EstimatedServerTime >= nextHuntSearch || (huntingApproach && !EligibleQuestMonster(huntingApproach)))
        {
            huntingApproach = null;
            nextHuntSearch = EstimatedServerTime + .25;
            float nearest = float.PositiveInfinity;
            var actors = NetworkHealth.Spawned;
            for (int i = 0; i < actors.Count; i++)
            {
                var candidate = actors[i];
                if (!EligibleQuestMonster(candidate)) continue;
                float distance = (candidate.transform.position - transform.position).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                huntingApproach = candidate;
            }
        }
        SetHudTarget(huntingApproach, automatic: true);
        return huntingApproach ? FollowRoadHeading(XZ(huntingApproach.transform.position) - XZ(transform.position)) : Vector3.zero;
    }

    private bool EligibleQuestMonster(NetworkHealth target) => target && target.EntityKind == NetworkHealth.Kind.Monster
        && target.CanBeTargeted && MatchesQuestMonster(target) && health.SharesCombatArea(target);

    private bool MatchesQuestMonster(NetworkHealth target) => target.MonsterDefinition == questDestination
        || (questDestination == "B1" && target.MonsterDefinition == "B1G");

    private static Vector2 XZ(Vector3 p) => new(p.x, p.z);

    private Vector3 FollowRoadHeading(Vector2 delta)
    {
        Vector3 direction = new Vector3(delta.x, 0f, delta.y).normalized;
        if (direction == Vector3.zero) return direction;
        // Monsters and other players can stand on the authored centre line. Look ahead
        // by one capsule width and steer around them without bypassing the CharacterController.
        if (!PathBlocked(direction, Mathf.Min(delta.magnitude, 1.5f))) return direction;
        for (int angle = 40; angle <= 80; angle += 40)
        {
            Vector3 right = Quaternion.AngleAxis(angle, Vector3.up) * direction;
            if (!PathBlocked(right, 1f)) return right;
            Vector3 left = Quaternion.AngleAxis(-angle, Vector3.up) * direction;
            if (!PathBlocked(left, 1f)) return left;
        }
        return direction;
    }

    private bool PathBlocked(Vector3 direction, float distance)
    {
        int count = Physics.SphereCastNonAlloc(transform.position, .43f, direction, sightHits,
            distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var obstacle = sightHits[i].collider;
            if (!(obstacle is TerrainCollider) && !obstacle.transform.IsChildOf(transform)) return true;
        }
        return false;
    }
}
