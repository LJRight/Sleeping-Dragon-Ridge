using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// NGO synchronizes WorldSession. The dedicated server keeps the three collision maps loaded,
// while each client opens only the landscape belonging to its authoritative player.
public sealed class WorldSceneCoordinator : MonoBehaviour
{
    public const string SessionName = "WorldSession";
    public static bool IsLoading => WorldLoadingScreen.Visible;
    [SerializeField] private AudioClip villageMusic, fieldMusic, bossMusic;
    private AudioSource music;
    private Scene visible;
    private int visibleRoom;
    private bool switching;
    internal bool ServerMapsReady { get; private set; }

    private void Awake()
    {
        music = gameObject.AddComponent<AudioSource>();
        music.playOnAwake = false;
        music.loop = true;
        music.spatialBlend = 0f;
        music.volume = .7f;
    }

    private void OnEnable() => SceneManager.sceneLoaded += DisablePreviewCameras;
    private void OnDisable() => SceneManager.sceneLoaded -= DisablePreviewCameras;

    internal IEnumerator PrepareServer()
    {
        foreach (string map in new[] { SceneWorld.Village, SceneWorld.Field, SceneWorld.Boss })
        {
            if (!Application.CanStreamedLevelBeLoaded(map))
            {
                Debug.LogError("Missing world scene in build: " + map);
                yield break;
            }
            yield return SceneManager.LoadSceneAsync(map, LoadSceneMode.Additive);
            var scene = SceneManager.GetSceneByName(map);
            if (!scene.isLoaded) yield break;
            SceneWorld.Place(scene);
        }
        ServerMapsReady = true;
    }

    internal static void ConfigureServerSync(NetworkManager server)
    {
        // Collision maps have no scene-placed NetworkObjects. Sending their load events
        // to all clients would move unrelated parties and load three landscapes on mobile.
        server.SceneManager.VerifySceneBeforeLoading = (_, name, _) => name == SessionName;
        server.SceneManager.DisableValidationWarnings(true);
        server.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Additive);
    }

    private void Update()
    {
        if (switching) return;
        var manager = NetworkManager.Singleton;
        if (!manager || manager.IsServer || !manager.IsConnectedClient) return;
        var player = manager.LocalClient?.PlayerObject;
        if (!player || !player.TryGetComponent<PlayerCombat>(out var combat)) return;
        if (!visible.IsValid() || visible.name != combat.MapSceneName || visibleRoom != combat.BossRoomId)
            StartCoroutine(ShowMap(combat));
    }

    private IEnumerator ShowMap(PlayerCombat player)
    {
        switching = true;
        while (player && player.IsSpawned)
        {
            string destination = player.MapSceneName;
            int room = player.BossRoomId;
            WorldLoadingScreen.Show(destination, room);
            // Draw the destination before starting any expensive scene activation/unloading.
            yield return null;
            if (visible.IsValid() && visible.isLoaded && visible.name != destination)
            {
                music.Stop();
                yield return SceneManager.UnloadSceneAsync(visible);
                visible = default;
                // Additive scene unload removes objects, but leaves meshes and textures resident.
                yield return Resources.UnloadUnusedAssets();
            }
            if (!visible.IsValid() || !visible.isLoaded)
            {
                if (!Application.CanStreamedLevelBeLoaded(destination))
                {
                    LoginSession.Instance.ReturnAfterMapError(destination);
                    yield break;
                }
                var load = SceneManager.LoadSceneAsync(destination, LoadSceneMode.Additive);
                while (!load.isDone)
                {
                    WorldLoadingScreen.SetProgress(load.progress);
                    yield return null;
                }
                visible = SceneManager.GetSceneByName(destination);
            }
            SceneWorld.Place(visible, room);
            visibleRoom = room;
            WorldLoadingScreen.SetProgress(1f);
            while (player && player.IsSpawned && WorldLoadingScreen.WithinMinimumDisplayTime)
                yield return null;
            // A wipe or victory may occur while a slow device is still loading the arena.
            if (!player || !player.IsSpawned) break;
            if (player.MapSceneName != destination || player.BossRoomId != room) continue;
            ApplyEnvironment(visible);
            music.clip = destination == SceneWorld.Field ? fieldMusic :
                destination == SceneWorld.Boss ? bossMusic : villageMusic;
            music.Play();
            break;
        }
        switching = false;
        WorldLoadingScreen.Hide();
    }

    private static void DisablePreviewCameras(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Additive || (scene.name != SceneWorld.Village &&
            scene.name != SceneWorld.Field && scene.name != SceneWorld.Boss)) return;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var camera in root.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
        }
    }

    private void ApplyEnvironment(Scene scene)
    {
        // Keep network spawns in the persistent shell so unloading a landscape cannot destroy them.
        SceneManager.SetActiveScene(scene);
        var skybox = RenderSettings.skybox;
        var sun = RenderSettings.sun;
        var fog = RenderSettings.fog;
        var fogMode = RenderSettings.fogMode;
        var fogColor = RenderSettings.fogColor;
        var fogStart = RenderSettings.fogStartDistance;
        var fogEnd = RenderSettings.fogEndDistance;
        var density = RenderSettings.fogDensity;
        var ambient = RenderSettings.ambientMode;
        var sky = RenderSettings.ambientSkyColor;
        var equator = RenderSettings.ambientEquatorColor;
        var ground = RenderSettings.ambientGroundColor;
        SceneManager.SetActiveScene(gameObject.scene);
        RenderSettings.skybox = skybox;
        RenderSettings.sun = sun;
        RenderSettings.fog = fog;
        RenderSettings.fogMode = fogMode;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;
        RenderSettings.fogDensity = density;
        RenderSettings.ambientMode = ambient;
        RenderSettings.ambientSkyColor = sky;
        RenderSettings.ambientEquatorColor = equator;
        RenderSettings.ambientGroundColor = ground;
    }
}
