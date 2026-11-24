using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class NetworkHealth : NetworkBehaviour
{
    public enum Kind { Player, Monster }
    [System.Serializable]
    private struct MonsterSkin
    {
        public string definition;
        public Material material;
    }

    [SerializeField] private Kind kind;
    [SerializeField, Min(1)] private int maxHealth = 100;
    [SerializeField] private string monsterDefinition = "A1";
    private string prefabMonsterDefinition;
    [SerializeField] private string monsterNameOverride;
    [SerializeField] private Renderer skinRenderer;
    [SerializeField] private MonsterSkin[] monsterSkins;
    [SerializeField] private AudioClip enemyHitSound;
    private AudioSource enemyHitSource;
    private int spawnRoom;
    private readonly NetworkVariable<int> monsterRoom = new(0);
    private readonly NetworkVariable<FixedString32Bytes> spawnedDefinition = new();
    public bool IsArenaBoss => kind == Kind.Monster && monsterRoom.Value > 0;
    public string MonsterDefinition => IsArenaBoss ? BossArena.BossId : monsterDefinition;
    public string MonsterDisplayName => !string.IsNullOrEmpty(monsterNameOverride) ? monsterNameOverride : DefaultMonsterName(MonsterDefinition);
    public static string DefaultMonsterName(string definition) => definition switch
    {
        "A1" => "영혼 포식자 드래곤",
        "A2" => "화염의 영혼 포식자 드래곤",
        "A3" => "맹독의 영혼 포식자 드래곤",
        "A4" => "빙결의 영혼 포식자 드래곤",
        "B1" => "용비늘 멧돼지",
        "B1G" => "황금 용비늘 멧돼지",
        BossArena.BossId => "능선의 수문장",
        _ => "몬스터"
    };
    internal Collider AttackHitBox { get; private set; }
    internal FieldMonsterBalance FieldBalance { get; private set; }
    private PlayerCombat player;
    private BossCombat boss;
    private int defense;
    private bool equipmentApplied;
    private readonly NetworkVariable<int> maximum = new(100);
    private readonly NetworkVariable<int> health = new(100,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private static readonly List<NetworkHealth> spawned = new();

    public event System.Action<NetworkHealth> ServerDamaged;
    public static IReadOnlyList<NetworkHealth> Spawned => spawned;
    public Kind EntityKind => kind;
    private int localHealth;
    public bool IsLocalTest => LocalCombatTest.IsLocal(this);
    public bool IsActive => isActiveAndEnabled && (IsSpawned || IsLocalTest);
    public int Current => IsLocalTest ? localHealth : health.Value;
    private int SpawnMaximum => Mathf.Max(1, boss ? MonsterBalanceCatalog.Boss.maxHealth : maxHealth);
    public int Maximum => IsSpawned && !IsLocalTest ? maximum.Value : SpawnMaximum;

    private void Awake()
    {
        prefabMonsterDefinition = monsterDefinition;
        player = GetComponent<PlayerCombat>();
        boss = GetComponent<BossCombat>();
        if (kind == Kind.Monster) AttackHitBox = transform.Find("HitBox")?.GetComponent<Collider>();
        GetComponent<NetworkObject>().CheckObjectVisibility = VisibleTo;
    }

    internal bool VisibleTo(ulong clientId)
    {
        if (player && player.OwnerClientId == clientId) return true;
        int room = kind == Kind.Monster ? spawnRoom : (player ? player.AreaId : 0);
        return room == BossPartyServer.ViewerRoom(clientId);
    }

    public int CombatRoom => Mathf.Max(0, AreaId);
    private int AreaId => kind == Kind.Monster ? (IsSpawned ? monsterRoom.Value : spawnRoom) : player ? player.AreaId : 0;

    internal bool SharesCombatArea(NetworkHealth other)
    {
        if (!other || IsLocalTest != other.IsLocalTest || AreaId != other.AreaId) return false;
        return IsLocalTest || (BossArena.ValidPosition(transform.position, AreaId) &&
            BossArena.ValidPosition(other.transform.position, AreaId));
    }

    internal void ReviveOnServer()
    {
        if (IsSpawned && IsServer && kind == Kind.Player && IsDead) health.Value = maximum.Value;
    }

    internal void ConfigureMonster(string definition, int hp, int room = 0)
    {
        monsterDefinition = definition;
        spawnRoom = room;
        maxHealth = Mathf.Max(1, hp);
    }

    internal void ApplyEquipment(int newMaximum, int newDefense)
    {
        if (!IsServer || !IsSpawned || kind != Kind.Player) return;
        int previous = maximum.Value;
        maximum.Value = Mathf.Max(1, newMaximum);
        defense = Mathf.Max(0, newDefense);
        if (!equipmentApplied) health.Value = maximum.Value;
        else if (Current > 0) health.Value = Mathf.Clamp(Current + maximum.Value - previous, 1, maximum.Value);
        equipmentApplied = true;
    }
    public bool IsDead => IsActive && Current <= 0;
    public bool CanBeTargeted => IsActive && !IsDead && (!boss || boss.CanBeTargeted);

    internal void SetMonsterDefense(int value)
    {
        if (IsServer && kind == Kind.Monster) defense = Mathf.Max(0, value);
    }

    internal int HealOnAuthority(int amount)
    {
        if (!IsActive || (!IsLocalTest && !IsServer) || IsDead || amount <= 0) return 0;
        int restored = Mathf.Min(amount, Maximum - Current);
        if (IsLocalTest) localHealth += restored;
        else health.Value += restored;
        return restored;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => spawned.Clear();

    private void OnEnable()
    {
        if (!IsLocalTest) return;
        ResolveFieldBalance();
        defense = FieldBalance?.defense ?? 0;
        localHealth = Maximum;
        ApplyMonsterSkin();
        if (!spawned.Contains(this)) spawned.Add(this);
    }
    private void OnDisable() => spawned.Remove(this);

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            ResolveFieldBalance();
            if (kind == Kind.Monster) spawnedDefinition.Value = new FixedString32Bytes(monsterDefinition);
            monsterRoom.Value = spawnRoom;
            equipmentApplied = false;
            defense = FieldBalance?.defense ?? 0;
            maximum.Value = SpawnMaximum;
            health.Value = maximum.Value;
        }
        if (IsClient && kind == Kind.Monster)
        {
            spawnedDefinition.OnValueChanged += OnMonsterDefinitionChanged;
            OnMonsterDefinitionChanged(default, spawnedDefinition.Value);
        }
        if (!spawned.Contains(this)) spawned.Add(this);
    }

    private void OnMonsterDefinitionChanged(FixedString32Bytes previous, FixedString32Bytes current)
    {
        if (current.Length > 0) monsterDefinition = current.ToString();
        ResolveFieldBalance();
        ApplyMonsterSkin();
    }

    private void ResolveFieldBalance()
    {
        if (kind == Kind.Monster && TryGetComponent<DragonMonster>(out _))
        {
            // A field prefab can stand in as the boss in prototype encounters.
            string id = monsterDefinition == BossArena.BossId ? prefabMonsterDefinition : monsterDefinition;
            FieldBalance = MonsterBalanceCatalog.FindField(id);
        }
    }

    private void ApplyMonsterSkin()
    {
        if (!skinRenderer || monsterSkins == null) return;
        foreach (var skin in monsterSkins)
        {
            if (skin.definition != monsterDefinition || !skin.material) continue;
            skinRenderer.sharedMaterial = skin.material;
            return;
        }
    }

    // Damage is called by server gameplay code, never supplied directly by a client RPC.
    public int ApplyDamage(int damage, PlayerCombat attacker = null)
    {
        if (!AcceptsDamage || damage <= 0) return 0;
        NetworkHealth attackingPlayer = attacker ? attacker.GetComponent<NetworkHealth>() : null;
        if (attacker && !SharesCombatArea(attackingPlayer)) return 0;
        if (boss && boss.TryBlockHit(attacker)) return 0;
        damage = Mathf.Max(1, Mathf.FloorToInt(damage * 100f / (100f + defense)));
        return LoseHealth(damage, attacker, attackingPlayer);
    }

    // Boss flame and reflection use the player's equipped maximum HP and bypass defense.
    internal int ApplyMaxHealthPercentDamage(int percent)
    {
        if (kind != Kind.Player || !AcceptsDamage || percent <= 0) return 0;
        int damage = (int)((long)Maximum * Mathf.Min(percent, 100) / 100);
        return LoseHealth(Mathf.Max(1, damage), null, null);
    }

    private bool AcceptsDamage => IsActive && !IsDead &&
        (IsLocalTest || (IsServer && (kind != Kind.Player || equipmentApplied)));

    private int LoseHealth(int finalDamage, PlayerCombat attacker, NetworkHealth attackingPlayer)
    {
        int before = Current;
        int after = Mathf.Max(0, before - finalDamage);
        if (IsLocalTest) localHealth = after;
        else health.Value = after;
        if (kind == Kind.Monster && before > after)
        {
            if (IsLocalTest) PlayEnemyHitSound(after == 0);
            else EnemyHitSoundRpc(after == 0);
        }
        bool received = kind == Kind.Player;
        // The player's object survives even when a killing hit despawns the monster.
        NetworkHealth recipient = received ? this : attackingPlayer;
        if (recipient && before > after)
        {
            Vector3 hitPosition = TryGetComponent<CharacterController>(out var body)
                ? transform.TransformPoint(body.center + Vector3.up * (body.height * .5f))
                : transform.position + Vector3.up;
            if (IsLocalTest) DamageNumberLayer.Show(hitPosition, finalDamage, received);
            else if (received) recipient.ShowReceivedDamageNumberRpc(hitPosition, finalDamage);
            else recipient.ShowDamageNumberRpc(hitPosition, finalDamage);
        }
        if (boss) boss.OnHealthLost(before, after);
        if (!IsLocalTest && kind == Kind.Monster && Current == 0 && attacker != null)
            GameProgressBridge.Kill(attacker, this);
        ServerDamaged?.Invoke(attackingPlayer);
        return before - after;
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    private void EnemyHitSoundRpc(bool lethal)
    {
        // NGO always executes the host's local RPC even when its player is in another room.
        if (!IsHost || VisibleTo(NetworkManager.LocalClientId)) PlayEnemyHitSound(lethal);
    }

    private void PlayEnemyHitSound(bool lethal)
    {
        if (!enemyHitSound) return;
        if (!enemyHitSource)
        {
            var emitter = new GameObject("Enemy Hit Audio");
            emitter.transform.SetParent(transform, false);
            enemyHitSource = emitter.AddComponent<AudioSource>();
            enemyHitSource.playOnAwake = false;
            enemyHitSource.spatialBlend = 1f;
            enemyHitSource.rolloffMode = AudioRolloffMode.Linear;
            enemyHitSource.minDistance = 5f;
            enemyHitSource.maxDistance = 25f;
            enemyHitSource.dopplerLevel = 0f;
            enemyHitSource.volume = 0.085f;
        }
        enemyHitSource.PlayOneShot(enemyHitSound);
        if (lethal)
        {
            // A killing blow can immediately despawn or pool this monster.
            enemyHitSource.transform.SetParent(null, true);
            Destroy(enemyHitSource.gameObject, enemyHitSound.length);
            enemyHitSource = null;
        }
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void ShowDamageNumberRpc(Vector3 hitPosition, int damage)
        => DamageNumberLayer.Show(hitPosition, damage);

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void ShowReceivedDamageNumberRpc(Vector3 hitPosition, int damage)
        => DamageNumberLayer.Show(hitPosition, damage, received: true);

    public override void OnNetworkDespawn()
    {
        spawnedDefinition.OnValueChanged -= OnMonsterDefinitionChanged;
        spawned.Remove(this);
    }
}
