using UnityEngine;

public static class BossArena
{
    public const string BossId = "BOSS";
    public static Vector3 Gate => SceneWorld.IsPlaced(SceneWorld.Field)
        ? SceneWorld.Position(SceneWorld.Field, "Boss Gate") + Vector3.up : new Vector3(12f, 1f, -8f);
    public static Vector3 Town => SceneWorld.IsPlaced(SceneWorld.Village)
        ? SceneWorld.Position(SceneWorld.Village, "Village arrival and revival") + Vector3.up : new Vector3(-3f, 1f, -4f);
    public static readonly Vector3 Center = new(56f, 1f, -30f);
    public static readonly Vector3 Arrival = new(56f, 1f, -36f);
    public static Vector3 BossSpawn => SceneWorld.IsPlaced(SceneWorld.Boss)
        ? SceneWorld.Position(SceneWorld.Boss, "Boss Spawn") : new Vector3(56f, 1f, -27f);
    internal const float HalfSize = 12f;
    private const float GateRadius = 3f;
    private const float TownRadius = 8f;

    public static bool AtGate(Vector3 position) => SceneWorld.IsPlaced(SceneWorld.Field)
        ? SceneWorld.AtMarker(SceneWorld.Field, "Boss Gate", position) : (position - Gate).sqrMagnitude <= GateRadius * GateRadius;
    public static bool InTown(Vector3 position) => SceneWorld.IsPlaced(SceneWorld.Village)
        ? SceneWorld.Contains(0, position) : (position - Town).sqrMagnitude <= TownRadius * TownRadius;

    public static bool CanEnter(PlayerCombat player, string source)
    {
        if (!player || !player.IsServer || !player.IsSpawned || player.InBossArena ||
            player.GetComponent<NetworkHealth>().IsDead || !GameProgressBridge.Restored(player)) return false;
        return source switch
        {
            "gate" => (!SceneWorld.IsPlaced(SceneWorld.Field) || player.AreaId == SceneWorld.FieldArea) && AtGate(player.transform.position),
            "town" => player.AreaId == 0 && InTown(player.transform.position),
            _ => false
        };
    }

    // The caller checks the account's discovery flag before authorizing a town entry.
    public static bool Enter(PlayerCombat player, string source, int room, int partySlot)
    {
        if (!CanEnter(player, source)) return false;
        player.ReturnArea = player.AreaId;
        player.FieldReturnPoint = source == "town" ? Town : Gate;
        player.TravelOnServer(PlayerArrival(room, partySlot), room);
        return true;
    }

    public static bool Leave(PlayerCombat player)
    {
        if (!player || !player.IsServer || !player.IsSpawned || !player.InBossArena ||
            player.GetComponent<NetworkHealth>().IsDead || BossPartyServer.IsCelebrating(player.BossRoomId)) return false;
        player.TravelOnServer(player.FieldReturnPoint, player.ReturnArea);
        return true;
    }

    internal static Vector3 PlayerArrival(int room, int slot = 0) => SceneWorld.IsPlaced(SceneWorld.Boss)
        ? SceneWorld.Position(SceneWorld.Boss, "Party Spawn " + (slot + 1), room) + Vector3.up
        : Arrival + Offset(room);

    internal static Vector3 FreeLanding(Vector3 destination, PlayerCombat traveler)
    {
        if (Clear(destination)) return destination;
        for (int ring = 1; ring <= 2; ring++)
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI / 4f;
            var offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (1.8f * ring);
            if (Clear(destination + offset)) return destination + offset;
        }
        return destination;

        bool Clear(Vector3 spot)
        {
            foreach (var entity in NetworkHealth.Spawned)
            {
                if (!entity || !entity.IsSpawned || entity.transform == traveler.transform) continue;
                var delta = entity.transform.position - spot;
                if (delta.x * delta.x + delta.z * delta.z < 1.5f * 1.5f) return false;
            }
            return true;
        }
    }

    // The walls stop normal motion. The server also guards the logical boundary independently.
    internal static Vector3 Offset(int room) => SceneWorld.IsPlaced(SceneWorld.Boss)
        ? SceneWorld.Origin(SceneWorld.Boss, room) - SceneWorld.Origin(SceneWorld.Boss)
        : Vector3.right * (Mathf.Max(1, room) - 1) * 96f;

    internal static bool ValidPosition(Vector3 point, int room)
    {
        if (SceneWorld.IsPlaced(SceneWorld.NameOf(room))) return SceneWorld.Contains(room, point);
        int nearest = Mathf.Max(1, Mathf.RoundToInt((point.x - Center.x) / 96f) + 1);
        Vector3 center = Center + Offset(nearest);
        bool inside = Mathf.Abs(point.x - center.x) <= HalfSize && Mathf.Abs(point.z - center.z) <= HalfSize;
        return room == 0 ? !inside : inside && nearest == room && point.y >= -2f;
    }
}
