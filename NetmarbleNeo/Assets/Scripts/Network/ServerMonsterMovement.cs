using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// Server-only decisions/movement. The replicated state can later drive a model's Animator.
[RequireComponent(typeof(CharacterController), typeof(NetworkTransform), typeof(NetworkHealth))]
public class ServerMonsterMovement : NetworkBehaviour
{
    public enum BehaviourState : byte { Idle, Chasing, Windup, Recovery, Returning, Dead }

    [SerializeField, Min(0f)] private float moveSpeed = 2f;
    [SerializeField, Min(0.1f)] private float detectionRadius = 8f;
    [SerializeField, Min(0.1f)] private float leashRadius = 12f;
    [SerializeField, Min(0.1f)] private float attackStartRange = 2f;
    [SerializeField, Min(0.1f)] private float attackHitRange = 2.5f;
    [SerializeField, Range(1f, 360f)] private float attackAngle = 100f;
    [SerializeField, Min(1)] private int attackDamage = 10;
    [SerializeField, Min(0.05f)] private float windupDuration = 0.65f;
    [SerializeField, Min(0.05f)] private float recoveryDuration = 0.85f;
    [SerializeField, Min(0.05f)] private float targetScanInterval = 0.2f;
    [SerializeField, Min(0.01f)] private float returnTolerance = 0.15f;
    [SerializeField, Min(0.1f)] private float blockedChaseTimeout = 3f;
    [SerializeField] private LayerMask obstacleLayers = ~0;

    private bool IsLocalTest => LocalCombatTest.IsLocal(this);
    private BehaviourState localState = BehaviourState.Idle;
    private BehaviourState StateValue { get => IsLocalTest ? localState : state.Value; set { if (IsLocalTest) localState = value; else state.Value = value; } }
    private void Start() { if (IsLocalTest) InitializeGameplay(); }
    private readonly NetworkVariable<BehaviourState> state = new(BehaviourState.Idle,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public BehaviourState CurrentState => StateValue;

    private CharacterController controller;
    private NetworkHealth health;
    private NetworkHealth target;
    private Vector3 homePosition;
    private Quaternion homeRotation;
    private float verticalVelocity;
    private double nextScanTime;
    private double stateEndsAt;
    private float blockedTime;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<NetworkHealth>();
        controller.enabled = false;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        InitializeGameplay();
    }
    private void InitializeGameplay()
    {
        controller.enabled = true;
        homePosition = transform.position;
        homeRotation = transform.rotation;
        verticalVelocity = 0f;
        target = null;
        blockedTime = 0f;
        nextScanTime = 0;
        StateValue = BehaviourState.Idle;
    }

    private void FixedUpdate()
    {
        if (!IsLocalTest && (!IsSpawned || !IsServer)) return;
        if (health.IsDead)
        {
            target = null;
            StateValue = BehaviourState.Dead;
            return;
        }

        double now = (IsLocalTest ? Time.timeAsDouble : NetworkManager.ServerTime.Time);
        Vector3 velocity = Vector3.zero;
        if (StateValue != BehaviourState.Returning && StateValue != BehaviourState.Idle
            && (!ValidTarget(target) || OutsideLeash()))
            BeginReturn();

        switch (StateValue)
        {
            case BehaviourState.Idle:
                if (now >= nextScanTime)
                {
                    nextScanTime = now + targetScanInterval;
                    target = FindNearestPlayer();
                    if (target != null) StateValue = BehaviourState.Chasing;
                }
                break;
            case BehaviourState.Chasing:
                // Keep the selected target until it becomes invalid to avoid rapid target switching.
                Vector3 offset = Horizontal(target.transform.position - transform.position);
                Face(offset);
                if (InAttackRange(target, attackStartRange) && HasLineOfSight(target))
                {
                    // Facing is locked for the entire windup: the attack does not track dodging players.
                    StateValue = BehaviourState.Windup;
                    stateEndsAt = now + windupDuration;
                    blockedTime = 0f;
                }
                else
                    velocity = offset.normalized * moveSpeed;
                break;
            case BehaviourState.Windup:
                if (now >= stateEndsAt)
                {
                    if (ValidTarget(target) && InAttackRange(target, attackHitRange)
                        && InAttackArc(target) && HasLineOfSight(target))
                        target.ApplyDamage(attackDamage);
                    // The state change ensures a swing deals damage at most once, including on a miss.
                    StateValue = BehaviourState.Recovery;
                    stateEndsAt = now + recoveryDuration;
                }
                break;
            case BehaviourState.Recovery:
                if (now >= stateEndsAt) StateValue = BehaviourState.Chasing;
                break;
            case BehaviourState.Returning:
                Vector3 homeOffset = Horizontal(homePosition - transform.position);
                if (homeOffset.magnitude <= returnTolerance)
                {
                    transform.rotation = homeRotation;
                    StateValue = BehaviourState.Idle;
                    nextScanTime = now + targetScanInterval;
                }
                else
                {
                    Face(homeOffset);
                    float speed = Mathf.Min(moveSpeed, homeOffset.magnitude / Time.fixedDeltaTime);
                    velocity = homeOffset.normalized * speed;
                }
                break;
        }

        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += Physics.gravity.y * Time.fixedDeltaTime;
        velocity.y = verticalVelocity;
        Vector3 before = transform.position;
        controller.Move(velocity * Time.fixedDeltaTime);
        // This prototype has direct steering, not pathfinding. Abandon a blocked chase.
        if (StateValue == BehaviourState.Chasing && Horizontal(velocity).sqrMagnitude > 0.001f)
        {
            float travelled = Horizontal(transform.position - before).magnitude;
            blockedTime = travelled < moveSpeed * Time.fixedDeltaTime * 0.1f
                ? blockedTime + Time.fixedDeltaTime : 0f;
            if (blockedTime >= blockedChaseTimeout) BeginReturn();
        }
        else blockedTime = 0f;
    }

    private NetworkHealth FindNearestPlayer()
    {
        NetworkHealth closest = null;
        float bestDistance = detectionRadius * detectionRadius;
        foreach (var candidate in NetworkHealth.Spawned)
        {
            if (!ValidTarget(candidate)) continue;
            if (Horizontal(candidate.transform.position - homePosition).sqrMagnitude > leashRadius * leashRadius)
                continue;
            float distance = (candidate.transform.position - transform.position).sqrMagnitude;
            if (distance <= bestDistance && HasLineOfSight(candidate))
            {
                bestDistance = distance;
                closest = candidate;
            }
        }
        return closest;
    }

    private bool ValidTarget(NetworkHealth candidate) => candidate != null && candidate.IsActive && health.SharesCombatArea(candidate)
        && !candidate.IsDead && candidate.EntityKind == NetworkHealth.Kind.Player;

    private bool OutsideLeash() => Horizontal(transform.position - homePosition).sqrMagnitude > leashRadius * leashRadius
        || (target != null && Horizontal(target.transform.position - homePosition).sqrMagnitude > leashRadius * leashRadius);

    private bool InAttackRange(NetworkHealth candidate, float range) =>
        (candidate.transform.position - transform.position).sqrMagnitude <= range * range;

    private bool InAttackArc(NetworkHealth candidate)
    {
        Vector3 offset = Horizontal(candidate.transform.position - transform.position);
        return offset.sqrMagnitude < 0.0001f
            || Vector3.Angle(Horizontal(transform.forward), offset) <= attackAngle * 0.5f;
    }

    private bool HasLineOfSight(NetworkHealth candidate)
    {
        Vector3 from = controller.bounds.center;
        var targetController = candidate.GetComponent<CharacterController>();
        Vector3 to = targetController != null ? targetController.bounds.center : candidate.transform.position;
        Vector3 ray = to - from;
        if (ray.sqrMagnitude < 0.0001f) return true;
        foreach (var hit in Physics.RaycastAll(from, ray.normalized, ray.magnitude,
                     obstacleLayers, QueryTriggerInteraction.Ignore))
        {
            // Ignore character shapes; world geometry blocks detection and melee attacks.
            if (hit.collider.GetComponentInParent<NetworkHealth>() == null) return false;
        }
        return true;
    }

    private void BeginReturn()
    {
        target = null;
        blockedTime = 0f;
        StateValue = BehaviourState.Returning;
    }

    private static Vector3 Horizontal(Vector3 value) => new(value.x, 0f, value.z);
    private void Face(Vector3 direction)
    {
        if (direction.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(direction);
    }

    public override void OnNetworkDespawn()
    {
        controller.enabled = false;
        target = null;
    }
}
