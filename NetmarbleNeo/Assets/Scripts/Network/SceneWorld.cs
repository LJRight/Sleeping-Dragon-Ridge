using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Network transforms use the same world coordinates on the dedicated server and on every client.
public static class SceneWorld
{
    public const string Village = "village", Field = "HuntingField", Boss = "BossRoom";
    internal const int FieldArea = -1;
    private const float BossSpacing = 256f;

    private sealed class Placement
    {
        internal GameObject[] roots;
        internal Vector3 origin;
        internal Scene scene;
        internal Vector3 terrainCorner;
        internal Vector3 terrainSize;
        internal readonly Dictionary<string, Transform> markers = new();
        internal readonly List<Transform> monsterSpawns = new();
    }

    private static readonly Dictionary<string, Placement> placements = new();
    private static readonly Dictionary<int, GameObject> extraRooms = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        placements.Clear();
        extraRooms.Clear();
        SceneManager.sceneUnloaded -= Forget;
        SceneManager.sceneUnloaded += Forget;
    }

    private static void Forget(Scene scene)
    {
        if (!placements.TryGetValue(scene.name, out var map) || map.scene != scene) return;
        placements.Remove(scene.name);
        if (scene.name == Boss) extraRooms.Clear();
    }

    internal static string NameOf(int area) => area == FieldArea ? Field : area > 0 ? Boss : Village;

    public static Vector3 Origin(string scene, int room = 0) => scene switch
    {
        Field => Vector3.right * 512f,
        Boss => Vector3.right * (1024f + (Mathf.Max(1, room) - 1) * BossSpacing),
        _ => Vector3.zero
    };

    public static void Place(Scene scene, int room = 0)
    {
        if (!scene.IsValid() || !scene.isLoaded || (scene.name != Village && scene.name != Field && scene.name != Boss))
            throw new System.ArgumentException("Expected a loaded world map.", nameof(scene));
        if (!placements.TryGetValue(scene.name, out var map) || map.scene != scene)
        {
            map = new Placement { scene = scene, roots = scene.GetRootGameObjects() };
            foreach (var root in map.roots)
            {
                foreach (var item in root.GetComponentsInChildren<Transform>(true))
                {
                    map.markers.TryAdd(item.name, item);
                    if (item.parent && item.parent.name == "Monster Spawns")
                        map.monsterSpawns.Add(item);
                }
                var terrain = root.GetComponentInChildren<TerrainCollider>(true);
                if (terrain && terrain.terrainData)
                {
                    map.terrainCorner = terrain.transform.position;
                    map.terrainSize = terrain.terrainData.size;
                }
            }
            if (map.terrainSize == Vector3.zero)
                throw new System.InvalidOperationException("Missing terrain collider in " + scene.name);
            placements[scene.name] = map;
        }
        Vector3 origin = Origin(scene.name, room);
        if (map.origin == origin) return;
        foreach (var root in map.roots)
            root.transform.position += origin - map.origin;
        map.origin = origin;
        Physics.SyncTransforms();
    }

    private static Placement Map(string name)
    {
        return placements.TryGetValue(name, out var placed) ? placed : null;
    }

    internal static bool IsPlaced(string name) => Map(name) != null;
    internal static IReadOnlyList<Transform> FieldSpawns => Map(Field).monsterSpawns;
    public static Transform FindMarker(string scene, string name)
    {
        var map = Map(scene);
        return map != null && map.markers.TryGetValue(name, out var marker) ? marker : null;
    }

    internal static Vector3 Position(string scene, string marker, int room = 0)
    {
        var map = Map(scene);
        if (map == null || !map.markers.TryGetValue(marker, out var point))
            throw new System.InvalidOperationException($"Missing world travel marker {scene}/{marker}");
        return point.position + (Origin(scene, room) - map.origin);
    }

    internal static bool AtMarker(string scene, string marker, Vector3 point)
    {
        var target = FindMarker(scene, marker);
        return target && (point - target.position).sqrMagnitude <= 16f;
    }

    // Check the authored terrain extents, including the per-attempt translation of the boss terrain.
    internal static bool Contains(int area, Vector3 position)
    {
        string scene = NameOf(area);
        var map = Map(scene);
        if (map == null) return false;
        Vector3 local = position - map.terrainCorner - Origin(scene, area);
        Vector3 size = map.terrainSize;
        return local.x >= .5f && local.z >= .5f && local.x <= size.x - .5f && local.z <= size.z - .5f &&
            local.y >= -1f;
    }

    // Room 1 uses the authored terrain directly. Concurrent attempts need their own physical
    // ground and obstacles even though clients receive only actors from their own attempt.
    internal static void OpenRoom(int room)
    {
        if (room <= 1 || extraRooms.ContainsKey(room)) return;
        var map = Map(Boss);
        var holder = new GameObject("Boss room collision " + room);
        SceneManager.MoveGameObjectToScene(holder, map.roots[0].scene);
        foreach (var root in map.roots)
        {
            if (root.GetComponentInChildren<Collider>(true))
                Object.Instantiate(root, holder.transform, true);
        }
        holder.transform.position = Origin(Boss, room) - map.origin;
        extraRooms.Add(room, holder);
        Physics.SyncTransforms();
    }

    internal static void CloseRoom(int room)
    {
        if (!extraRooms.Remove(room, out var holder) || !holder) return;
        holder.SetActive(false);
        Object.Destroy(holder);
    }
}
