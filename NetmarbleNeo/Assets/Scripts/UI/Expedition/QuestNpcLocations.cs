using UnityEngine;
using UnityEngine.SceneManagement;

public static class QuestNpcLocations
{
    public const float Radius = 4f;
    public static readonly string[] Ids = { "warden", "smith" };

    public static bool TryPosition(string id, out Vector3 position)
    {
        var actor = FindVillageActor(id);
        if (actor)
        {
            position = actor.position;
            return true;
        }
        // Directly opening the old prototype still uses its two stand-in NPCs.
        if (SceneManager.GetSceneByName("Prototype").isLoaded)
        {
            if (id == "warden") { position = new Vector3(-2f, 1f, 0f); return true; }
            if (id == "smith") { position = new Vector3(5f, 1f, 2f); return true; }
        }
        position = default;
        return false;
    }

    internal static Transform FindVillageActor(string id) => id switch
    {
        "warden" => SceneWorld.FindMarker(SceneWorld.Village, "NPC_warden"),
        "smith" => SceneWorld.FindMarker(SceneWorld.Village, "NPC_smith"),
        _ => null
    };

    public static string Name(string id) => id switch
    {
        "warden" => "경비대장",
        "smith" => "대장장이",
        _ => id
    };
}
