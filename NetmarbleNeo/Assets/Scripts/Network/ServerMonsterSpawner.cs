using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ServerMonsterSpawner : NetworkBehaviour
{
    [SerializeField] private NetworkObject prefab;
    [SerializeField] private NetworkObject boarPrefab;
    [SerializeField] private string monsterId = "A1";
    [SerializeField] private bool useArenaSpawn;

    private readonly Dictionary<NetworkObject, Transform> homes = new();
    internal static ServerMonsterSpawner Arena { get; private set; }

    private float nextCheck;
    private void Update()
    {
        if (Arena != this) return;
        // NGO also registers new player objects as mutual observers after OnNetworkSpawn.
        BossPartyServer.RefreshNewPlayers(NetworkManager);
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 1f;
        BossPartyServer.Expire();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer || !prefab) return;
        if (useArenaSpawn) { Arena = this; return; }
        if (!SceneWorld.IsPlaced(SceneWorld.Field)) { SpawnAt(prefab, transform, 0); return; }

        foreach (var marker in SceneWorld.FieldSpawns)
        {
            var source = marker.name.Contains(" - B1 ") ? boarPrefab : prefab;
            if (!source) throw new System.InvalidOperationException("Missing field monster prefab for " + marker.name);
            SpawnAt(source, marker, SceneWorld.FieldArea);
        }
    }

    // Boss rooms create and dispose their own encounter. Each public-field slot reuses its own instance.
    internal NetworkObject SpawnMonster(int room) => SpawnAt(prefab, transform, room);

    private NetworkObject SpawnAt(NetworkObject source, Transform home, int area)
    {
        var instance = Instantiate(source);
        homes.Add(instance, home);
        Activate(instance, home, area);
        return instance;
    }

    private void Activate(NetworkObject instance, Transform home, int area)
    {
        string id = monsterId;
        Vector3 position = home.position;
        Quaternion rotation = home.rotation;
        if (area > 0)
        {
            id = BossArena.BossId;
            position = BossArena.BossSpawn + BossArena.Offset(area);
            var marker = SceneWorld.FindMarker(SceneWorld.Boss, "Boss Spawn");
            if (marker) rotation = marker.rotation;
        }
        else if (area == SceneWorld.FieldArea)
        {
            // Authored names begin "Spawn N - ID ". Every respawn rolls the boar variant independently.
            int separator = home.name.IndexOf(" - ", System.StringComparison.Ordinal);
            id = home.name.Substring(separator + 3, 2);
            if (id == "B1")
            {
                float chance = MonsterBalanceCatalog.GoldenBoarChance;
                if (chance >= 1f || Random.value < chance) id = "B1G";
            }
        }
        instance.transform.SetPositionAndRotation(position, rotation);
        instance.gameObject.SetActive(true);
        var health = instance.GetComponent<NetworkHealth>();
        int hp = area > 0 ? health.Maximum : MonsterBalanceCatalog.FindField(id).maximumHealth;
        health.ConfigureMonster(id, hp, area);
        if (instance.TryGetComponent<DragonMonster>(out var dragon)) dragon.SetPool(this);
        instance.Spawn();
    }

    public void ReturnToPool(NetworkObject instance)
    {
        if (!IsServer || !homes.TryGetValue(instance, out var home) || !instance.IsSpawned) return;
        if (useArenaSpawn)
        {
            homes.Remove(instance);
            instance.Despawn(true);
            return;
        }
        var health = instance.GetComponent<NetworkHealth>();
        float respawnDelay = MonsterBalanceCatalog.FindField(health.MonsterDefinition).respawnDelay;
        instance.Despawn(false);
        instance.gameObject.SetActive(false);
        StartCoroutine(Respawn(instance, home, respawnDelay));
    }

    private IEnumerator Respawn(NetworkObject instance, Transform home, float respawnDelay)
    {
        yield return new WaitForSeconds(respawnDelay);
        if (IsSpawned && IsServer && !NetworkManager.ShutdownInProgress && instance && home)
            Activate(instance, home, home == transform ? 0 : SceneWorld.FieldArea);
    }

    public override void OnNetworkDespawn()
    {
        if (Arena == this)
        {
            Arena = null;
            BossPartyServer.Reset();
        }
        StopAllCoroutines();
        foreach (var instance in homes.Keys)
        {
            if (!instance) continue;
            if (instance.IsSpawned && NetworkManager && NetworkManager.IsServer && !NetworkManager.ShutdownInProgress) instance.Despawn();
            else if (!instance.IsSpawned) Destroy(instance.gameObject);
        }
        homes.Clear();
    }
}
