using System.Collections.Generic;
using UnityEngine;

// Static scene landmarks and collision exist on every peer, independently of network visibility.
public sealed class BossArenaLandmarks : MonoBehaviour
{
    [SerializeField] private Material stone;
    private static readonly Color Basalt = new(.23f, .28f, .33f);
    private static readonly Color Silver = new(.65f, .73f, .78f);
    private static readonly Color Amber = new(1f, .57f, .14f);
    private static readonly Color Blue = new(.22f, .71f, .89f);

    private static BossArenaLandmarks instance;
    private readonly Dictionary<int, GameObject> rooms = new();
    private Transform parent;

    private void Awake()
    {
        instance = this;
        BuildArena(1);
        parent = transform;
#if !UNITY_SERVER
        Vector3 gate = BossArena.Gate;
        for (int side = -1; side <= 1; side += 2)
            Beacon(gate + Vector3.right * side * 1.55f, Amber);
        Block("Broken gate lintel", gate + Vector3.up * 1.7f,
            new Vector3(4.1f,.42f,.85f), Basalt);
        Block("Gate seal", gate + Vector3.up * 1.92f, new Vector3(.55f,.55f,.55f), Amber)
            .transform.Rotate(0f, 45f, 45f);
        Beacon(BossArena.Town + Vector3.left * 1.6f, Blue);
#endif
    }

    internal static void ShowRoom(int room)
    {
        if (instance && room > 1 && !instance.rooms.ContainsKey(room)) instance.BuildArena(room);
    }

    internal static void RemoveRoom(int room)
    {
        if (instance && room > 1 && instance.rooms.Remove(room, out var geometry))
            Destroy(geometry);
    }

    private void BuildArena(int room)
    {
        var geometry = new GameObject("Boss arena " + room);
        geometry.transform.SetParent(transform, false);
        rooms[room] = geometry;
        parent = geometry.transform;
        var c = BossArena.Center + BossArena.Offset(room);
        if (room > 1)
        {
            var ground = geometry.AddComponent<BoxCollider>();
            ground.center = new Vector3(c.x, -.25f, c.z) - geometry.transform.position;
            ground.size = new Vector3(2 * BossArena.HalfSize + 1, .5f, 2 * BossArena.HalfSize + 1);
        }
        float r = BossArena.HalfSize;
        Wall(new Vector3(c.x, 0, c.z - r), new Vector3(2 * r + .8f, .55f, .8f));
        Wall(new Vector3(c.x, 0, c.z + r), new Vector3(2 * r + .8f, .55f, .8f));
        Wall(new Vector3(c.x - r, 0, c.z), new Vector3(.8f, .55f, 2 * r));
        Wall(new Vector3(c.x + r, 0, c.z), new Vector3(.8f, .55f, 2 * r));
#if !UNITY_SERVER
        Block("Arena stone", new Vector3(c.x,.025f,c.z), new Vector3(2*r,.04f,2*r), Basalt);
        for (int x = -1; x <= 1; x += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = c + new Vector3(x*(r-.45f),0,z*(r-.45f));
            Beacon(corner, Amber);
        }
#endif
    }

    private void Wall(Vector3 foot, Vector3 rim)
    {
        var boundary = new GameObject("Arena boundary");
        boundary.transform.SetParent(parent, false);
        boundary.transform.position = foot;
        var collider = boundary.AddComponent<BoxCollider>();
        collider.center = Vector3.up * 3f;
        collider.size = new Vector3(rim.x, 6f, rim.z);
#if !UNITY_SERVER
        Block("Stone parapet", foot + Vector3.up * rim.y * .5f, rim, Basalt);
        Block("Parapet rim", foot + Vector3.up * rim.y,
            new Vector3(rim.x, .12f, rim.z), Silver);
#endif
    }

#if !UNITY_SERVER
    private void Beacon(Vector3 origin, Color accent)
    {
        Block("Stone plinth", new Vector3(origin.x, .22f, origin.z), new Vector3(1.3f,.44f,1.3f), Basalt);
        Block("Carved pillar", new Vector3(origin.x, 1.23f, origin.z), new Vector3(.76f,1.65f,.76f), Basalt);
        Block("Metal band", new Vector3(origin.x, 1.86f, origin.z), new Vector3(.85f,.2f,.85f), Silver);
        Block("Waystone crystal", new Vector3(origin.x, 2.4f, origin.z),
            new Vector3(.47f,.7f,.47f), accent).transform.Rotate(0f, 45f, 30f);
    }

    private GameObject Block(string label, Vector3 position, Vector3 size, Color tint)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = label;
        block.transform.SetParent(parent, false);
        block.transform.position = position;
        block.transform.localScale = size;
        block.GetComponent<BoxCollider>().enabled = false;
        var renderer = block.GetComponent<MeshRenderer>();
        if (stone) renderer.sharedMaterial = stone;
        renderer.renderingLayerMask = 129; // Main light and attack-range decals, matching the Prototype plane.
        var properties = new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", tint);
        properties.SetColor("_Color", tint);
        renderer.SetPropertyBlock(properties);
        return block;
    }
#endif
}
