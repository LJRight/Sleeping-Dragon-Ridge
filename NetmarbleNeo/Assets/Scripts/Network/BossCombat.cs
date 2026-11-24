using System;
using Unity.Netcode;
using UnityEngine;

// These values are also the integer values of the boss Animator's Phase parameter.
public enum BossPhase { Idle, Run, Basic, Claw, Scream, Flame, TakeOff, Float, FlyFlame, Land, Hit, Defend, Die }

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkHealth), typeof(CharacterController))]
public sealed class BossCombat : NetworkBehaviour
{
    public const float DeathDuration = 64f / 30f;
    private const float FramesPerSecond = 30f;
    [SerializeField] private AnimationClip defendClip;
    private float reflectOpen, reflectClose;
    private BossMonsterBalance balance;
    private float FlameTickInterval => balance.flameTickIntervalFrames / FramesPerSecond;

    private struct ActionState : INetworkSerializable, IEquatable<ActionState>
    {
        public BossPhase phase;
        public uint sequence;
        public double start;
        public Vector3 origin, forward, aim;
        public float range, width, arc, hitAt, duration;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref phase);
            serializer.SerializeValue(ref sequence);
            serializer.SerializeValue(ref start);
            serializer.SerializeValue(ref origin);
            serializer.SerializeValue(ref forward);
            serializer.SerializeValue(ref aim);
            serializer.SerializeValue(ref range);
            serializer.SerializeValue(ref width);
            serializer.SerializeValue(ref arc);
            serializer.SerializeValue(ref hitAt);
            serializer.SerializeValue(ref duration);
        }

        public bool Equals(ActionState other) =>
            phase == other.phase && sequence == other.sequence && start.Equals(other.start) &&
            origin.Equals(other.origin) && forward.Equals(other.forward) && aim.Equals(other.aim) &&
            range.Equals(other.range) && width.Equals(other.width) && arc.Equals(other.arc) && hitAt.Equals(other.hitAt) &&
            duration.Equals(other.duration);
    }

    private readonly NetworkVariable<ActionState> action = new();
    private readonly NetworkVariable<int> defenseBreaks = new();
    private CharacterController body;
    private NetworkHealth health;
    private double clawReady, flameReady, defendReady, flightReady, basicReady;
    private int fireTicksDone, healRemainder;
    private bool strikeDone, hitAfterFlame;
    private float fallSpeed;

    public BossPhase Phase => action.Value.phase;
    public uint PhaseSequence => action.Value.sequence;
    public double PhaseStartedAt => action.Value.start;
    public float PhaseElapsed => IsSpawned ? Mathf.Max(0f, (float)(Now - PhaseStartedAt)) : 0f;
    public float PhaseDuration => action.Value.duration;
    public int DefenseBreaks => defenseBreaks.Value;
    public int CurrentDefense => (int)Math.Max(0L, balance.initialDefense - (long)balance.defenseLossPerBreak * DefenseBreaks);
    public Vector3 AttackOrigin => action.Value.origin;
    public Vector3 AttackForward => action.Value.forward;
    public Vector3 AimPoint => action.Value.aim;
    public bool IsAirborneUntargetable => Phase == BossPhase.TakeOff || Phase == BossPhase.Float ||
        Phase == BossPhase.FlyFlame || Phase == BossPhase.Land;
    public bool CanBeTargeted => !IsAirborneUntargetable && Phase != BossPhase.Die;
    private double Now => NetworkManager.ServerTime.Time;

    private void Awake()
    {
        balance = MonsterBalanceCatalog.Boss;
        body = GetComponent<CharacterController>();
        health = GetComponent<NetworkHealth>();
        body.enabled = false;
        if (defendClip)
        {
            var markers = defendClip.events;
            reflectOpen = CombatAnimationTiming.MarkerTime(markers, nameof(BossAnimationView.BossReflectOpen), defendClip.length);
            float close = CombatAnimationTiming.MarkerTime(markers, nameof(BossAnimationView.BossReflectClose), defendClip.length);
            reflectClose = float.IsInfinity(close) ? 0f : close;
        }
    }

    public override void OnNetworkSpawn()
    {
        body.enabled = IsServer;
        if (!IsServer) return;
        defenseBreaks.Value = 0;
        healRemainder = 0;
        fallSpeed = 0f;
        hitAfterFlame = false;
        double now = Now;
        basicReady = now;
        clawReady = now + balance.clawCooldown;
        flameReady = now + balance.flameCooldown;
        defendReady = now + balance.reflectCooldown;
        flightReady = now + balance.flightCooldown;
        Enter(BossPhase.Idle);
    }

    protected override void OnNetworkPostSpawn()
    {
        if (IsServer) health.SetMonsterDefense(CurrentDefense);
    }

    public override void OnNetworkDespawn()
    {
        body.enabled = false;
    }

    private float Duration(BossPhase phase) => phase switch
    {
        BossPhase.Idle => 40f / 30f,
        BossPhase.Run => 20f / 30f,
        BossPhase.Basic => 35f / 30f,
        BossPhase.Claw => 90f / 30f,
        BossPhase.Scream => 100f / 30f,
        BossPhase.Flame => 80f / 30f,
        BossPhase.TakeOff or BossPhase.Land => 120f / 30f,
        BossPhase.Float => 2f, // Fly Float has 30 frames, played twice.
        BossPhase.FlyFlame => 90f / 30f,
        BossPhase.Hit => 40f / (30f * 1.3f),
        BossPhase.Defend => defendClip ? defendClip.length : 0f,
        BossPhase.Die => DeathDuration,
        _ => 0f
    };

    private static Vector3 Flat(Vector3 value) => new(value.x, 0f, value.z);

    private void Enter(BossPhase phase, NetworkHealth target = null, bool keepAim = false)
    {
        var next = action.Value;
        next.phase = phase;
        next.start = Now;
        next.sequence++;
        if (!keepAim)
        {
            var origin = transform.position;
            origin.y = body.bounds.min.y;
            Vector3 aim = target ? target.transform.position : origin + transform.forward;
            Vector3 direction = Flat(aim - origin);
            if (direction.sqrMagnitude > .0001f)
                transform.rotation = Quaternion.LookRotation(direction);
            next.origin = origin;
            next.forward = Flat(transform.forward).normalized;
            next.aim = aim;
            next.range = phase == BossPhase.Basic ? balance.basicRadius : phase == BossPhase.Claw ? balance.clawLength : balance.flameRadius;
            next.width = phase == BossPhase.Claw ? balance.clawWidth : 0f;
            next.arc = phase == BossPhase.Basic ? balance.basicArc : balance.flameArc;
        }
        next.duration = Duration(phase);
        next.hitAt = phase switch
        {
            BossPhase.Basic => next.duration * balance.basicHitFraction,
            BossPhase.Claw => balance.clawHitFrame / FramesPerSecond,
            _ => balance.flameFirstHitFrame / FramesPerSecond
        };
        if (phase == BossPhase.Flame || phase == BossPhase.FlyFlame)
            next.duration = Mathf.Max(next.duration, next.hitAt + (balance.flameTickCount - 1) * FlameTickInterval);
        strikeDone = false;
        fireTicksDone = 0;
        action.Value = next;
    }

    internal bool TryBlockHit(PlayerCombat attacker)
    {
        if (!IsServer || !IsSpawned) return false;
        if (!CanBeTargeted) return true;
        if (Phase != BossPhase.Defend) return false;
        float elapsed = PhaseElapsed;
        if (elapsed >= reflectOpen && elapsed < reflectClose && attacker &&
            attacker.TryGetComponent<NetworkHealth>(out var source) && IsOpponent(source))
            source.ApplyMaxHealthPercentDamage(balance.reflectionMaxHealthPercent);
        return true;
    }

    internal void OnHealthLost(int before, int after)
    {
        if (!IsServer || !IsSpawned || before <= after) return;
        if (after <= 0)
        {
            hitAfterFlame = false;
            if (Phase != BossPhase.Die) Enter(BossPhase.Die);
            return;
        }
        int steps = balance.CountDefenseBreaks(after, health.Maximum);
        // A healed boss keeps the defense breaks already earned in this encounter.
        if (steps <= DefenseBreaks) return;
        defenseBreaks.Value = steps;
        health.SetMonsterDefense(CurrentDefense);
        if (Phase == BossPhase.Flame) hitAfterFlame = true;
        else Enter(BossPhase.Hit);
    }

    private bool IsOpponent(NetworkHealth other) =>
        other && other.IsSpawned && other.NetworkObject.IsPlayerObject &&
        other.EntityKind == NetworkHealth.Kind.Player && !other.IsDead &&
        health.SharesCombatArea(other);

    private NetworkHealth NearestOpponent(bool inBasicArc = false)
    {
        NetworkHealth nearest = null;
        float best = float.PositiveInfinity;
        foreach (var actor in NetworkHealth.Spawned)
        {
            if (!IsOpponent(actor)) continue;
            if (inBasicArc && !InsideSector(actor.transform.position, action.Value.range, action.Value.arc)) continue;
            float distance = Flat(actor.transform.position - transform.position).sqrMagnitude;
            if (distance < best || (distance == best && nearest && actor.OwnerClientId < nearest.OwnerClientId))
            {
                best = distance;
                nearest = actor;
            }
        }
        return nearest;
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer || Phase == BossPhase.Die) return;
        if (health.IsDead) { Enter(BossPhase.Die); return; }
        Vector3 velocity = Vector3.zero;
        float elapsed = PhaseElapsed;
        switch (Phase)
        {
            case BossPhase.Idle:
            case BossPhase.Run:
                velocity = Decide();
                break;
            case BossPhase.Basic:
                if (!strikeDone && elapsed >= action.Value.hitAt)
                {
                    strikeDone = true;
                    var victim = NearestOpponent(inBasicArc: true);
                    if (victim) victim.ApplyDamage(balance.basicDamage);
                }
                if (elapsed >= PhaseDuration) { basicReady = Now + balance.basicRecovery; Enter(BossPhase.Idle); }
                break;
            case BossPhase.Claw:
                if (!strikeDone && elapsed >= action.Value.hitAt)
                {
                    strikeDone = true;
                    foreach (var actor in NetworkHealth.Spawned)
                        if (IsOpponent(actor) && InClawArea(actor.transform.position)) actor.ApplyDamage(balance.clawDamage);
                }
                if (elapsed >= PhaseDuration) Enter(BossPhase.Idle);
                break;
            case BossPhase.Scream:
                if (elapsed >= PhaseDuration) Enter(BossPhase.Flame, NearestOpponent());
                break;
            case BossPhase.Flame:
            case BossPhase.FlyFlame:
                while (fireTicksDone < balance.flameTickCount && elapsed >= action.Value.hitAt + fireTicksDone * FlameTickInterval)
                {
                    fireTicksDone++;
                    FireTick();
                }
                if (elapsed >= PhaseDuration)
                {
                    BossPhase next = Phase == BossPhase.FlyFlame ? BossPhase.Land :
                        hitAfterFlame ? BossPhase.Hit : BossPhase.Idle;
                    hitAfterFlame = false;
                    Enter(next);
                }
                break;
            case BossPhase.TakeOff:
                if (elapsed >= PhaseDuration) Enter(BossPhase.Float, NearestOpponent());
                break;
            case BossPhase.Float:
                if (elapsed >= PhaseDuration) Enter(BossPhase.FlyFlame, keepAim: true);
                break;
            case BossPhase.Land:
            case BossPhase.Defend:
            case BossPhase.Hit:
                if (elapsed >= PhaseDuration) Enter(BossPhase.Idle);
                break;
        }
        Move(velocity);
    }

    private Vector3 Decide()
    {
        var target = NearestOpponent();
        if (!target)
        {
            if (Phase != BossPhase.Idle) Enter(BossPhase.Idle);
            return Vector3.zero;
        }
        Vector3 delta = Flat(target.transform.position - transform.position);
        float distance = delta.magnitude;
        double now = Now;
        if (now >= flightReady)
        {
            flightReady = now + balance.flightCooldown;
            Enter(BossPhase.TakeOff, target);
        }
        else if (defendClip && now >= defendReady)
        {
            defendReady = now + balance.reflectCooldown;
            Enter(BossPhase.Defend, target);
        }
        else if (now >= flameReady && distance <= balance.flameRadius)
        {
            flameReady = now + balance.flameCooldown;
            Enter(BossPhase.Scream, target);
        }
        else if (now >= clawReady && distance <= balance.clawLength)
        {
            clawReady = now + balance.clawCooldown;
            Enter(BossPhase.Claw, target);
        }
        else if (distance <= balance.basicRadius)
        {
            if (now >= basicReady) Enter(BossPhase.Basic, target);
            else if (Phase != BossPhase.Idle) Enter(BossPhase.Idle);
        }
        else
        {
            if (Phase != BossPhase.Run) Enter(BossPhase.Run, target);
            transform.rotation = Quaternion.LookRotation(delta);
            return delta.normalized * Mathf.Min(balance.chaseSpeed, distance / Time.fixedDeltaTime);
        }
        return Vector3.zero;
    }

    private void Move(Vector3 velocity)
    {
        if (body.isGrounded && fallSpeed < 0f) fallSpeed = -2f;
        fallSpeed += Physics.gravity.y * Time.fixedDeltaTime;
        var before = transform.position;
        body.Move((velocity + Vector3.up * fallSpeed) * Time.fixedDeltaTime);
        if (BossArena.ValidPosition(transform.position, health.CombatRoom)) return;
        var fallback = BossArena.BossSpawn + BossArena.Offset(health.CombatRoom);
        body.enabled = false;
        transform.position = BossArena.ValidPosition(before, health.CombatRoom) ? before : fallback;
        body.enabled = true;
        fallSpeed = 0f;
    }

    public bool InClawArea(Vector3 point)
    {
        Vector3 delta = Flat(point - AttackOrigin);
        float depth = Vector3.Dot(delta, AttackForward);
        float lateral = Vector3.Dot(delta, Vector3.Cross(Vector3.up, AttackForward));
        return depth >= 0f && depth <= action.Value.range && Mathf.Abs(lateral) <= action.Value.width * .5f;
    }

    private bool InsideSector(Vector3 point, float range, float arc)
    {
        Vector3 delta = Flat(point - AttackOrigin);
        if (delta.sqrMagnitude > range * range) return false;
        return delta.sqrMagnitude < .0001f || Vector3.Dot(delta.normalized, AttackForward) >= Mathf.Cos(arc * .5f * Mathf.Deg2Rad);
    }

    public bool InFlameArea(Vector3 point) => InsideSector(point, action.Value.range, action.Value.arc);

    private void FireTick()
    {
        bool damaged = false;
        foreach (var actor in NetworkHealth.Spawned)
        {
            if (!IsOpponent(actor) || !InFlameArea(actor.transform.position)) continue;
            if (actor.ApplyMaxHealthPercentDamage(balance.flameMaxHealthPercent) > 0) damaged = true;
        }
        if (!damaged) return;
        // Carry fractional healing between successful ticks, once per tick regardless of target count.
        long hundredths = healRemainder + (long)health.Maximum * balance.healPercentPerTick;
        int heal = (int)(hundredths / 100);
        healRemainder = (int)(hundredths % 100);
        if (heal > 0) health.HealOnAuthority(heal);
        if (health.Current == health.Maximum) healRemainder = 0;
    }

    public bool TryGetTelegraph(out BossAttackCue cue)
    {
        cue = default;
        if (!IsSpawned || !health || health.IsDead) return false;
        var phase = Phase;
        float elapsed = PhaseElapsed;
        bool claw = phase == BossPhase.Claw;
        bool flame = phase == BossPhase.Flame;
        bool hover = phase == BossPhase.Float;
        bool flyingFlame = phase == BossPhase.FlyFlame;
        if ((!claw && !flame && !hover && !flyingFlame) || (claw && elapsed >= action.Value.hitAt)) return false;
        float chargeTime = hover ? PhaseDuration : action.Value.hitAt;
        cue = new BossAttackCue {
            origin = AttackOrigin, forward = AttackForward, rectangle = claw,
            range = action.Value.range, width = action.Value.width, angle = action.Value.arc,
            fill = flyingFlame || chargeTime <= 0f ? 1f : Mathf.Clamp01(elapsed / chargeTime),
            danger = (flame || flyingFlame) && elapsed >= action.Value.hitAt
        };
        return true;
    }
}
