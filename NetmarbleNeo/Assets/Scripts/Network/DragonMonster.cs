using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(NetworkObject), typeof(NetworkHealth), typeof(CharacterController))]
[RequireComponent(typeof(NetworkTransform))]
public class DragonMonster : NetworkBehaviour
{
    public enum State { Sleep, Scream, Run, BasicAttack, GetHit, Die, Idle, Walk, ReturnHome, AttackCooldown }
    [SerializeField] private Animator animator;
    [SerializeField] private AudioClip attackSound, screamSound, deathSound;
    [SerializeField] private LayerMask obstacles = ~0;
    private FieldMonsterBalance Balance => health.FieldBalance;
    private bool IsLocalTest => LocalCombatTest.IsLocal(this);
    private int localState = (int)State.Sleep;
    private int StateValue { get => IsLocalTest ? localState : state.Value; set { if (IsLocalTest) localState = value; else state.Value = value; } }
    private void Start() { if (IsLocalTest) InitializeGameplay(); }
    private readonly NetworkVariable<int> state = new((int)State.Sleep);
    private readonly NetworkVariable<double> stateStarted = new();
    private static readonly string[] Names = { "Sleep", "Scream", "Run", "Basic Attack", "Get Hit", "Die", "Idle", "Walk" };
    private CharacterController body;
    private AudioSource voice;
    private NetworkHealth health, target;
    private Vector3 home, destination;
    private float gravity, blocked;
    private double until, nextAttack, nextScan;
    private bool awakened, dealtHit, serverInstance, viewDirty;
    private bool retaliating;
    private ServerMonsterSpawner pool;
    public float AttackRadius => Balance.hitRange;
    public float AttackArc => Balance.attackAngle;
    public State CurrentState => (State)StateValue;
    private double localStarted;
    private double StartedAt { get => IsLocalTest ? localStarted : stateStarted.Value; set { if (IsLocalTest) localStarted = value; else stateStarted.Value = value; } }
    private double Now => (IsLocalTest ? Time.timeAsDouble : NetworkManager.ServerTime.Time);
    public void SetPool(ServerMonsterSpawner owner) => pool = owner;
    private void Awake()
    {
        body = GetComponent<CharacterController>(); health = GetComponent<NetworkHealth>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        body.enabled = false;
    }
    public override void OnNetworkSpawn()
    {
        serverInstance = IsServer;
        state.OnValueChanged += OnState;
    }
    // NetworkHealth resolves the synchronized variant before its AI and animation read the row.
    protected override void OnNetworkPostSpawn() => InitializeGameplay();
    private void InitializeGameplay()
    {
        if (IsLocalTest || IsServer)
        {
            body.enabled = true; home = transform.position; gravity = blocked = 0;
            awakened = dealtHit = false; target = null; nextAttack = nextScan = 0;
            health.ServerDamaged += Damaged;
            if (Balance.startAwake) BeginIdle();
            else Enter(State.Sleep);
        }
        if (animator) { animator.applyRootMotion = false; animator.enabled = IsLocalTest || IsClient; }
        ShowState();
    }
    private void OnState(int previous, int current) => viewDirty = true;
    private void LateUpdate() { if (viewDirty && (IsSpawned || IsLocalTest)) { viewDirty = false; ShowState(); } }
    private void ShowState()
    {
        if ((!IsLocalTest && !IsClient) || !animator) return;
        State motion = CurrentState switch
        {
            State.ReturnHome => State.Run,
            State.AttackCooldown => State.Idle,
            _ => CurrentState
        };
        animator.SetInteger("State", (int)motion);
        // Direct seek also initializes late joiners in the current server action.
        float duration = CurrentState switch { State.Scream => Balance.screamDuration, State.BasicAttack => Balance.attackDuration,
            State.GetHit => Balance.hitDuration, State.Die => Balance.deathDuration, _ => 0f };
        float progress = duration > 0 ? Mathf.Clamp01((float)(Now - StartedAt) / duration) : 0;
        animator.Play("Base Layer." + Names[(int)motion], 0, progress);
    }
    private void Enter(State next, float seconds = 0)
    {
        if (next is State.Sleep or State.Idle or State.ReturnHome) retaliating = false;
        StartedAt = Now; StateValue = (int)next; until = Now + seconds; blocked = 0; viewDirty = true;
        if (next is State.BasicAttack or State.Scream or State.Die)
        {
            if (IsLocalTest) PlayActionSound(next);
            else PlayActionSoundRpc(next);
        }
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    private void PlayActionSoundRpc(State action)
    {
        if (!IsHost || health.VisibleTo(NetworkManager.LocalClientId)) PlayActionSound(action);
    }

    private void PlayActionSound(State action)
    {
        var clip = action switch
        {
            State.BasicAttack => attackSound,
            State.Scream => screamSound,
            State.Die => deathSound,
            _ => null
        };
        float start = action == State.BasicAttack ? 1f : 0f;
        if (!clip || start >= clip.length) return;
        if (!voice)
        {
            var emitter = new GameObject("Monster Voice");
            emitter.transform.SetParent(transform, false);
            voice = emitter.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 1f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = 5f;
            voice.maxDistance = 25f;
            voice.dopplerLevel = 0f;
        }
        voice.Stop();
        voice.clip = clip;
        voice.time = start;
        voice.Play();
        if (action == State.Die)
        {
            // The death clip outlasts the corpse's return to the spawn pool.
            voice.transform.SetParent(null, true);
            Destroy(voice.gameObject, clip.length);
            voice = null;
        }
    }
    private void Damaged(NetworkHealth attackingPlayer)
    {
        if (health.IsDead) { target = null; Enter(State.Die, Balance.deathDuration); return; }
        if (CurrentState is State.BasicAttack or State.Scream or State.GetHit) return;
        if (CurrentState == State.ReturnHome)
        {
            if (!WithinTerritory(transform.position) || !Detected(attackingPlayer)) return;
            target = attackingPlayer;
            retaliating = true;
            awakened = true;
        }
        else if (!target) target = FindPlayer();
        if (!awakened) FirstEncounter();
        else Enter(State.GetHit, Balance.hitDuration);
    }
    private void FirstEncounter()
    {
        awakened = true;
        Enter(State.Scream, Balance.screamDuration);
    }
    private void FixedUpdate()
    {
        if (!IsLocalTest && (!IsSpawned || !IsServer)) return;
        if (health.IsDead && CurrentState != State.Die) { target = null; Enter(State.Die, Balance.deathDuration); }
        if (CurrentState == State.Die)
        {
            if (Now >= until)
            {
                if (IsLocalTest) gameObject.SetActive(false);
                else if (pool) pool.ReturnToPool(NetworkObject);
                else { NetworkObject.Despawn(false); gameObject.SetActive(false); }
            }
            return;
        }
        Vector3 velocity = Vector3.zero;
        if (CurrentState != State.ReturnHome && (!WithinTerritory(transform.position) || LostTarget()))
            EndEncounter();
        switch (CurrentState)
        {
            case State.Sleep:
            case State.Idle:
            case State.Walk:
                if (Now >= nextScan)
                {
                    nextScan = Now + .2; target = FindPlayer();
                    if (target)
                    {
                        if (!awakened) FirstEncounter();
                        else Enter(State.Run);
                        break;
                    }
                }
                if (CurrentState == State.Idle && Now >= until)
                {
                    Vector2 point = Random.insideUnitCircle * Mathf.Min(Balance.wanderRadius, Balance.territoryRadius);
                    destination = home + new Vector3(point.x, 0, point.y);
                    Enter(State.Walk, Random.Range(Balance.walkSeconds.x, Balance.walkSeconds.y));
                }
                if (CurrentState == State.Walk)
                {
                    if (Reached(destination) || Now >= until) BeginIdle();
                    else velocity = MoveToward(destination, Balance.walkSpeed);
                }
                break;
            case State.Scream:
            case State.GetHit:
            case State.AttackCooldown:
                if (Now >= until)
                {
                    // During retaliation, recovery must not replace the attacker with a closer bystander.
                    if (!retaliating) target = FindPlayer();
                    if (target) Enter(State.Run);
                    else EndEncounter();
                }
                break;
            case State.Run:
                Vector3 toTarget = target.transform.position - transform.position;
                Vector3 offset = Flat(toTarget); Face(offset);
                // The player's pivot is above its feet. Close to actual hit range before committing to a swing.
                if (offset.sqrMagnitude <= Balance.attackRange * Balance.attackRange &&
                    toTarget.sqrMagnitude <= Balance.hitRange * Balance.hitRange && Visible(target))
                {
                    if (Now >= nextAttack) { dealtHit = false; Enter(State.BasicAttack, Balance.attackDuration); }
                    // Don't run in place during cooldown: show Idle, but retain combat detection.
                    else Enter(State.AttackCooldown, (float)(nextAttack - Now));
                }
                else velocity = offset.normalized * Balance.runSpeed;
                break;
            case State.BasicAttack:
                if (!dealtHit && Now >= StartedAt + Balance.attackDuration * Balance.attackHitFraction)
                {
                    dealtHit = true;
                    if (Detected(target) && Vector3.Distance(transform.position, target.transform.position) <= Balance.hitRange
                        && Vector3.Angle(transform.forward, Flat(target.transform.position-transform.position)) <= Balance.attackAngle * .5f
                        && Visible(target)) target.ApplyDamage(Balance.damage);
                }
                if (Now >= until) { nextAttack = Now + Balance.attackCooldown; Enter(State.Run); }
                break;
            case State.ReturnHome:
                if (Reached(home))
                {
                    BeginIdle();
                    nextScan = Now + .2;
                }
                else velocity = MoveToward(home, Balance.runSpeed);
                break;
        }
        if (body.isGrounded && gravity < 0) gravity = -2;
        gravity += Physics.gravity.y * Time.fixedDeltaTime;
        var before = transform.position;
        body.Move((velocity + Vector3.up * gravity) * Time.fixedDeltaTime);
        if (velocity.sqrMagnitude > 0)
        {
            float progress = Vector3.Dot(transform.position - before, velocity.normalized);
            float expected = velocity.magnitude * Time.fixedDeltaTime;
            blocked = progress < expected * .1f ? blocked + Time.fixedDeltaTime : 0;
            if (blocked >= 2f) HandleBlockedMovement();
        }
    }
    private bool Reached(Vector3 point) => Flat(point - transform.position).sqrMagnitude <= .2f * .2f;
    private Vector3 MoveToward(Vector3 point, float speed)
    {
        Vector3 offset = Flat(point - transform.position);
        Face(offset);
        return Vector3.ClampMagnitude(offset / Time.fixedDeltaTime, speed);
    }
    private bool WithinTerritory(Vector3 point) => health.IsArenaBoss ||
        Flat(point - home).sqrMagnitude <= Balance.territoryRadius * Balance.territoryRadius;
    private bool LostTarget()
    {
        // The arena boss finishes its roar/reaction and can switch directly to another party member.
        bool needsTarget = CurrentState is State.Run or State.BasicAttack;
        if (!health.IsArenaBoss)
            needsTarget |= CurrentState == State.AttackCooldown ||
                ((CurrentState is State.GetHit or State.Scream) && target is not null);
        return needsTarget && !Detected(target);
    }
    private void EndEncounter()
    {
        if (health.IsArenaBoss) { BeginIdle(); return; }
        target = null;
        Enter(State.ReturnHome);
    }
    private void HandleBlockedMovement()
    {
        if (CurrentState == State.ReturnHome)
        {
            // Direct steering can be obstructed. Recover at the original spawn once, including on clients.
            blocked = 0;
            if (!HomeIsClear()) return;
            body.enabled = false;
            transform.position = home;
            if (!IsLocalTest)
                GetComponent<NetworkTransform>().Teleport(home, transform.rotation, transform.localScale);
            body.enabled = true;
            gravity = 0;
            BeginIdle();
            nextScan = Now + .2;
        }
        else if (CurrentState == State.Run && !health.IsArenaBoss) EndEncounter();
        else { BeginIdle(); nextScan = Now + 2; }
    }
    private bool HomeIsClear()
    {
        Bounds bounds = body.bounds;
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z);
        Vector3 center = home + (bounds.center - transform.position);
        Vector3 halfSegment = Vector3.up * Mathf.Max(0, bounds.extents.y - radius);
        // A small inset tolerates contact with the ground at the original spawn.
        foreach (var other in Physics.OverlapCapsule(center - halfSegment, center + halfSegment,
                     radius * .95f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            if (!other.transform.IsChildOf(transform)) return false;
        return true;
    }
    private void BeginIdle() { target = null; Enter(State.Idle, Random.Range(Balance.idleSeconds.x, Balance.idleSeconds.y)); }
    private bool Detected(NetworkHealth p) => p && p.IsActive && !p.IsDead && p.EntityKind == NetworkHealth.Kind.Player && health.SharesCombatArea(p)
        && WithinTerritory(p.transform.position) && Vector3.Distance(transform.position,p.transform.position) <= Balance.detectionRadius;
    private NetworkHealth FindPlayer()
    {
        NetworkHealth best = null; float distance = float.MaxValue;
        foreach (var p in NetworkHealth.Spawned)
        {
            if (!Detected(p) || !Visible(p)) continue;
            float d = (p.transform.position-transform.position).sqrMagnitude;
            if (d < distance) { best = p; distance = d; }
        }
        return best;
    }
    private bool Visible(NetworkHealth p)
    {
        Vector3 from = body.bounds.center;
        var c = p.GetComponent<CharacterController>();
        Vector3 delta = (c ? c.bounds.center : p.transform.position) - from;
        foreach (var hit in Physics.RaycastAll(from, delta.normalized, delta.magnitude, obstacles, QueryTriggerInteraction.Ignore))
            if (!hit.collider.GetComponentInParent<NetworkHealth>()) return false;
        return true;
    }
    private static Vector3 Flat(Vector3 p) => new(p.x,0,p.z);
    private void Face(Vector3 p) { if (p.sqrMagnitude > .0001f) transform.rotation = Quaternion.LookRotation(p); }
    private void OnDisable() { if (health) health.ServerDamaged -= Damaged; }
    public override void OnNetworkDespawn()
    {
        state.OnValueChanged -= OnState; health.ServerDamaged -= Damaged; body.enabled = false; target = null;
        // Only the server retains instances. Remote clients discard their replicas.
        if (!serverInstance) Destroy(gameObject);
    }
}
