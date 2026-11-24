using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NetworkObject), typeof(CharacterController), typeof(NetworkTransform))]
public class InputHandler : NetworkBehaviour
{
    [SerializeField] private Joystick joystick;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private CharacterController controller;
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField, Min(0f)] private float rotationSpeed = 10f;
    [SerializeField, Range(0f, 1f)] private float inputDeadzone = 0.1f;
    private readonly NetworkVariable<float> moveAmount = new(0f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private float localMoveAmount;
    private bool IsLocalTest => LocalCombatTest.IsLocal(this);
    private float MovementAmount { set { if (IsLocalTest) localMoveAmount = value; else moveAmount.Value = value; } }
    public float MoveAmount => IsLocalTest ? localMoveAmount : moveAmount.Value;
    private PlayerCombat combat;
    private const float InputInterval = 1f / 20f;
    private const float InputTimeout = 0.5f;
    private Vector3 serverMoveDirection;
    private float nextInputTime;
    private float lastServerInputTime;
    private float verticalVelocity;
    private bool groundedAfterGravity;
    private int spawnSlot = -1;
    private bool hasFocus = true;
    private bool modalWasOpen;
    private bool wasMoving;
    private readonly System.Collections.Generic.HashSet<string> nearbyNpcs = new();
    private bool isPaused;
    private FollowCamera followCamera;
    private PlayerInput playerInput;
    private NetworkHealth health;
    private bool spectatorStickHidden;
    private bool ownerStickHeld;
    internal uint ManualMovementRevision { get; private set; }
    internal bool ManualMovementHeld => IsClient && IsOwner ? ownerStickHeld : serverMoveDirection != Vector3.zero;

    private void Awake()
    {
        health = GetComponent<NetworkHealth>();
        combat = GetComponent<PlayerCombat>();
        if (controller == null)
            controller = GetComponent<CharacterController>();
        // Physics runs on the server, input only on the owning client.
        controller.enabled = false;
        playerInput = GetComponent<PlayerInput>();
        if (playerInput != null)
            playerInput.enabled = false;
    }

    private void Start()
    {
        if (!IsLocalTest) return;
        controller.enabled = true;
        BindLocalInput();
    }

    public override void OnNetworkSpawn()
    {
        ownerStickHeld = false;
        ManualMovementRevision = 0;
        if (IsServer)
        {
            // Players submit motion through SubmitMoveRpc. Only server gameplay sets a world position.
            GetComponent<NetworkTransform>().OnClientRequestChange =
                (_, _, _) => (transform.position, transform.rotation, transform.localScale);
            spawnSlot = FindFreeSpawnSlot();
            // Player pivot is at the center of the two-metre capsule.
            transform.position = SceneWorld.IsPlaced(SceneWorld.Village)
                ? BossArena.FreeLanding(BossArena.Town, combat) : new Vector3(spawnSlot * 3f, 1f, 0f);
            controller.enabled = true;
            serverMoveDirection = Vector3.zero;
            verticalVelocity = 0f;
            MovementAmount = 0f;
        }

        if (!IsClient || !IsLocalPlayer)
            return;

        BindLocalInput();
    }

    private void BindLocalInput()
    {
        hasFocus = Application.isFocused;
        if (playerInput != null)
            playerInput.enabled = true;
        // One scene-local joystick and camera per client in this prototype.
        if (joystick == null)
            joystick = FindFirstObjectByType<Joystick>();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        if (cameraTransform != null)
        {
            followCamera = cameraTransform.GetComponent<FollowCamera>();
            if (followCamera != null)
                followCamera.SetTarget(transform);
        }
        if (joystick == null || cameraTransform == null || followCamera == null)
            Debug.LogWarning("Local Player needs a scene Joystick and a MainCamera with FollowCamera.", this);
        nextInputTime = 0f;
    }

    protected override void OnNetworkPostSpawn()
    {
        // All NetworkBehaviours are initialized before setting the initial network position.
        if (IsServer)
            GetComponent<NetworkTransform>().Teleport(transform.position, transform.rotation, transform.localScale);
    }

    private int FindFreeSpawnSlot()
    {
        for (int slot = 0; ; slot++)
        {
            bool occupied = false;
            foreach (NetworkObject networkObject in NetworkManager.SpawnManager.SpawnedObjectsList)
            {
                if (networkObject.TryGetComponent(out InputHandler other) && other != this
                    && other.IsSpawned && other.spawnSlot == slot)
                {
                    occupied = true;
                    break;
                }
            }
            if (!occupied)
                return slot;
        }
    }

    private void Update()
    {
        if (!IsSpawned && !IsLocalTest)
            return;
        if (IsClient && IsLocalPlayer && joystick && spectatorStickHidden != combat.IsSpectatingBoss)
        {
            spectatorStickHidden = combat.IsSpectatingBoss;
            ReleaseTravelInput();
            joystick.gameObject.SetActive(!spectatorStickHidden);
        }
        if ((IsLocalTest || (IsClient && IsLocalPlayer)) && (health == null || !health.IsDead))
        {
            bool modal = ExpeditionUI.BlocksGameplay;
            if (modal && !modalWasOpen) StopLocalInput();
            modalWasOpen = modal;
            Vector3 direction = ReadMoveDirection();
            if (IsLocalTest)
            {
                ApplyMoveInput(direction);
                return;
            }
            bool held = direction.magnitude > inputDeadzone;
            bool changed = held != ownerStickHeld;
            if (held && changed) ManualMovementRevision = unchecked(ManualMovementRevision + 1);
            ownerStickHeld = held;
            if (Time.unscaledTime >= nextInputTime || changed)
            {
                SubmitMoveRpc(direction, combat.TravelEpoch, ManualMovementRevision);
                nextInputTime = Time.unscaledTime + InputInterval;
            }
        }
    }

    private Vector3 ReadMoveDirection()
    {
        if (!hasFocus || isPaused || ExpeditionUI.BlocksGameplay || joystick == null || cameraTransform == null)
            return Vector3.zero;
        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;
        forward.y = right.y = 0f;
        return Vector3.ClampMagnitude(
            forward.normalized * joystick.Vertical + right.normalized * joystick.Horizontal, 1f);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitMoveRpc(Vector3 direction, uint epoch, uint manualRevision, bool cancelApproach = false)
    {
        if (epoch != combat.TravelEpoch) return;
        if (cancelApproach) combat.CancelManualApproach();
        ManualMovementRevision = manualRevision;
        ApplyMoveInput(direction);
    }

    internal void ResetMovementOnServer()
    {
        serverMoveDirection = Vector3.zero;
        verticalVelocity = 0f;
        groundedAfterGravity = false;
        MovementAmount = 0f;
        wasMoving = false;
        nearbyNpcs.Clear();
    }

    internal void ReleaseTravelInput()
    {
        joystick?.OnPointerUp(null);
        ownerStickHeld = false;
        nextInputTime = 0f;
    }

    internal void TeleportOnServer(Vector3 destination) => TeleportOnServer(destination, Quaternion.identity);

    private void TeleportOnServer(Vector3 destination, Quaternion rotation)
    {
        groundedAfterGravity = false;
        controller.enabled = false;
        GetComponent<NetworkTransform>().Teleport(destination, rotation, transform.localScale);
        controller.enabled = true;
    }

    internal bool TryMoveForJump(Vector3 displacement)
    {
        if ((!IsLocalTest && (!IsSpawned || !IsServer)) || !controller.enabled) return false;
        float minimum = controller.minMoveDistance;
        if (displacement.sqrMagnitude < minimum * minimum) return false;
        MoveInCurrentArea(displacement);
        return true;
    }

    private void MoveInCurrentArea(Vector3 displacement)
    {
        Vector3 before = transform.position;
        controller.Move(displacement);
        if (IsLocalTest || BossArena.ValidPosition(transform.position, combat.AreaId)) return;

        // Physical walls are the primary boundary. Resolve pushes into the other zone too.
        Vector3 fallback = combat.InBossArena ? BossArena.PlayerArrival(combat.BossRoomId) :
            combat.AreaId == SceneWorld.FieldArea ? SceneWorld.Position(SceneWorld.Field, "Field Arrival") + Vector3.up : BossArena.Town;
        Quaternion facing = combat.CurrentSkill == PlayerSkill.JumpAttack ? transform.rotation : Quaternion.identity;
        TeleportOnServer(BossArena.ValidPosition(before, combat.AreaId) ? before : BossArena.FreeLanding(fallback, combat), facing);
        MovementAmount = 0f;
    }

    private void ApplyMoveInput(Vector3 direction)
    {
        if (health != null && health.IsDead) return;
        if (float.IsNaN(direction.x) || float.IsInfinity(direction.x)
            || float.IsNaN(direction.z) || float.IsInfinity(direction.z))
            return;
        direction.y = 0f;
        serverMoveDirection = direction.magnitude <= inputDeadzone
            ? Vector3.zero : Vector3.ClampMagnitude(direction, 1f);
        if (serverMoveDirection != Vector3.zero && combat) combat.StopAutomaticActions();
        lastServerInputTime = Time.unscaledTime;
    }

    private void FixedUpdate()
    {
        if (!IsLocalTest && (!IsSpawned || !IsServer)) return;
        if (health != null && health.IsDead)
        {
            if (combat) combat.StopAutomaticActions();
            MovementAmount = 0f;
            serverMoveDirection = Vector3.zero;
            return;
        }
        // Lost focus, paused clients and interrupted input streams must not move forever.
        if (Time.unscaledTime - lastServerInputTime > InputTimeout)
            serverMoveDirection = Vector3.zero;
        // A separate horizontal jump sweep can overwrite CharacterController.isGrounded.
        if (groundedAfterGravity && verticalVelocity < 0f)
            verticalVelocity = -2f;
        verticalVelocity += Physics.gravity.y * Time.fixedDeltaTime;
        // Still receive joystick changes during attacks so movement resumes with the current input.
        Vector3 direction;
        if (combat && combat.HasManualApproach)
            direction = combat.ManualApproachDirection();
        else if (combat && combat.HasQuestTravel)
            direction = combat.QuestTravelDirection();
        else if (combat && combat.AutoCombatEnabled)
            direction = combat.AutoCombatDirection();
        else
        {
            if (combat && combat.CurrentCombo.Stage > 0 && serverMoveDirection != Vector3.zero)
                combat.CancelBasicForMovement();
            direction = serverMoveDirection;
        }
        if (combat && combat.IsAttacking) direction = Vector3.zero;
        MovementAmount = direction.magnitude;
        Vector3 before = transform.position;
        MoveInCurrentArea((direction * moveSpeed + Vector3.up * verticalVelocity)
            * Time.fixedDeltaTime);
        groundedAfterGravity = controller.isGrounded;
        if (!IsLocalTest && GameProgressBridge.Restored(combat))
        {
            bool moving = direction.sqrMagnitude > 0.001f && Vector3.ProjectOnPlane(transform.position - before, Vector3.up).sqrMagnitude > 0.000001f;
            if (moving && !wasMoving) GameProgressBridge.Tutorial(combat, "move");
            wasMoving = moving;
            foreach (string npc in QuestNpcLocations.Ids)
            {
                bool near = combat.AreaId == 0 && QuestNpcLocations.TryPosition(npc, out var position) &&
                    Vector3.Distance(transform.position, position) <= QuestNpcLocations.Radius;
                if (!near) nearbyNpcs.Remove(npc);
                else if (nearbyNpcs.Add(npc)) GameProgressBridge.Tutorial(combat, "visit_" + npc);
            }
        }
        if (direction.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(direction), rotationSpeed * Time.fixedDeltaTime);
        }
    }

    private void OnApplicationFocus(bool focused)
    {
        hasFocus = focused;
        if (!focused)
            StopLocalInput();
    }

    private void OnApplicationPause(bool paused)
    {
        isPaused = paused;
        if (paused)
            StopLocalInput();
    }

    private void StopLocalInput()
    {
        if (!IsLocalTest && (!IsSpawned || !IsClient || !IsLocalPlayer))
            return;
        if (joystick != null)
            joystick.OnPointerUp(null);
        if (IsLocalTest)
        {
            combat.CancelManualApproach();
            ApplyMoveInput(Vector3.zero);
        }
        else SubmitMoveRpc(Vector3.zero, combat.TravelEpoch, ManualMovementRevision, cancelApproach: true);
        ownerStickHeld = false;
    }

    public override void OnNetworkDespawn()
    {
        // Release only this player's camera binding.
        if (followCamera != null)
            followCamera.ClearTarget(transform);
        if (followCamera != null && joystick != null)
            joystick.OnPointerUp(null);
        if (spectatorStickHidden && joystick) joystick.gameObject.SetActive(true);
        spectatorStickHidden = false;
        if (playerInput != null)
            playerInput.enabled = false;
        controller.enabled = false;
        groundedAfterGravity = false;
        serverMoveDirection = Vector3.zero;
        spawnSlot = -1;
        wasMoving = false;
        nearbyNpcs.Clear();
    }
}
