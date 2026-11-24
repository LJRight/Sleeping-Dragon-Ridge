using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkHealth))]
public partial class PlayerCombat : NetworkBehaviour
{
    // One snapshot keeps stage and start time consistent, including for late joiners.
    public struct ComboState : INetworkSerializable, IEquatable<ComboState>
    {
        public int Stage;
        public uint SwingId;
        public double StartedAt;
        public bool NextQueued;
        public PlayerSkill Skill;
        public Vector3 JumpCenter;
        public bool JumpLanded;
        private double spinningSlashReadyAt, uppercutReadyAt, jumpAttackReadyAt;

        public double ReadyAt(PlayerSkill skill) => skill switch
        {
            PlayerSkill.SpinningSlash => spinningSlashReadyAt,
            PlayerSkill.Uppercut => uppercutReadyAt,
            PlayerSkill.JumpAttack => jumpAttackReadyAt,
            _ => 0d
        };

        internal void SetReadyAt(PlayerSkill skill, double time)
        {
            switch (skill)
            {
                case PlayerSkill.SpinningSlash: spinningSlashReadyAt = time; break;
                case PlayerSkill.Uppercut: uppercutReadyAt = time; break;
                case PlayerSkill.JumpAttack: jumpAttackReadyAt = time; break;
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Stage);
            serializer.SerializeValue(ref SwingId);
            serializer.SerializeValue(ref StartedAt);
            serializer.SerializeValue(ref NextQueued);
            serializer.SerializeValue(ref Skill);
            serializer.SerializeValue(ref spinningSlashReadyAt);
            serializer.SerializeValue(ref uppercutReadyAt);
            serializer.SerializeValue(ref jumpAttackReadyAt);
            if (Skill == PlayerSkill.JumpAttack)
            {
                serializer.SerializeValue(ref JumpCenter);
                serializer.SerializeValue(ref JumpLanded);
            }
            else if (serializer.IsReader)
            {
                JumpCenter = default;
                JumpLanded = false;
            }
        }

        public bool Equals(ComboState other) => Stage == other.Stage && SwingId == other.SwingId
            && StartedAt.Equals(other.StartedAt) && NextQueued == other.NextQueued
            && Skill == other.Skill && spinningSlashReadyAt.Equals(other.spinningSlashReadyAt)
            && uppercutReadyAt.Equals(other.uppercutReadyAt) && jumpAttackReadyAt.Equals(other.jumpAttackReadyAt)
            && (Skill != PlayerSkill.JumpAttack || (JumpLanded == other.JumpLanded && JumpCenter.Equals(other.JumpCenter)));
    }

    [SerializeField, Min(1)] private int damage = 10;
    [SerializeField, Min(0.1f)] private float attackRange = 2.5f;
    [SerializeField, Range(1f, 360f)] private float attackAngle = 120f;
    [SerializeField] private LayerMask targetLayers = ~0;
    [Tooltip("Seconds at the configured Animator state speed. Set from Attack 1/2/3 clips.")]
    [SerializeField] private Vector3 attackDurations = new(2.633333f, 2.766667f, 2.166667f);

    private readonly NetworkVariable<ComboState> combo = new(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    [Tooltip("Attack 1/2/3 clips, in order. Event times are read without running an Animator. State speed must stay 1.")]
    [SerializeField] private AnimationClip[] attackClips = new AnimationClip[3];
    private readonly float[] transitionTimes = new float[3];
    private readonly float[] inputCloseTimes = new float[3];
    private readonly bool[] validWindows = new bool[3];
    private ComboState localCombo;
    public bool IsLocalTest => LocalCombatTest.IsLocal(this);
    public bool IsActive => IsSpawned || IsLocalTest;
    public bool HasPresentation => IsLocalTest || IsClient;
    private ComboState State
    {
        get => IsLocalTest ? localCombo : combo.Value;
        set
        {
            if (IsLocalTest) { var previous = localCombo; localCombo = value; OnComboChanged(previous, value); }
            else combo.Value = value;
        }
    }
    private NetworkHealth health;
    private InputHandler movement;
    private bool hitApplied;
    private double queuedAt;
    private readonly HashSet<NetworkHealth> hitTargets = new();
    private Collider[] hitColliders = new Collider[16];

    public float AttackRadius => attackRange;
    public float AttackArc => IsSpinningSlash ? 360f : attackAngle;
    public ComboState CurrentCombo => State;
    public bool IsAttacking => IsActive && (State.Stage != 0 || State.Skill != PlayerSkill.None);
    public double EstimatedServerTime => IsLocalTest ? Time.timeAsDouble : NetworkManager.ServerTime.Time;

    public float GetAttackDuration(int stage)
    {
        int index = Mathf.Clamp(stage - 1, 0, 2);
        var clip = attackClips != null && index < attackClips.Length ? attackClips[index] : null;
        return Mathf.Max(0.1f, clip != null ? clip.length : attackDurations[index]);
    }

    // Button availability is independent of combo timing or whether a next swing is queued.
    public bool CanRequestAttack => IsActive && (IsLocalTest || (IsClient && IsOwner))
        && !health.IsDead && State.Skill == PlayerSkill.None;

    private bool CanActOnAuthority => IsActive && !health.IsDead
        && (IsLocalTest || (IsServer && GameProgressBridge.Restored(this)));

    private void Awake()
    {
        health = GetComponent<NetworkHealth>();
        movement = GetComponent<InputHandler>();
        ReadAttackEvents();
        ReadSkillTiming();
    }

    public override void OnNetworkSpawn()
    {
        hitApplied = false;
        if (IsServer)
        {
            SetHudTarget(null);
            StopAutomaticActions();
            combo.Value = default;
            potionReadyAt.Value = 0d;
            bossRoom.Value = 0;
            spectatingBoss.Value = false;
            BossPartyServer.NewPlayerJoined = true;
            travelEpoch.Value = 0;
            reviveAt = 0;
            health.ServerDamaged += ObserveServerDamage;
            GameProgressBridge.Register(this);
        }
        bossRoom.OnValueChanged += OnBossRoomChanged;
        travelEpoch.OnValueChanged += OnTravelEpochChanged;
        combo.OnValueChanged += OnComboChanged;
        if (IsClient && IsOwner) potionReadyAt.OnValueChanged += OnPotionCooldownChanged;
        if (IsClient && IsOwner && combo.Value.Stage > 0)
            Debug.Log($"[Combo] {combo.Value.Stage}", this);
    }

    public void RequestAttack()
    {
        if (!CanRequestAttack || ExpeditionUI.BlocksGameplay) return;
        // Keep accepting clicks; the server reserves at most one next swing.
        var state = State;
        if (IsLocalTest) ProcessAttack(state.SwingId, state.Stage, manual: true);
        else AttackRpc(state.SwingId, state.Stage);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void AttackRpc(uint observedSwingId, int observedStage)
        => ProcessAttack(observedSwingId, observedStage, manual: true);

    private void ProcessAttack(uint observedSwingId, int observedStage, bool manual = false)
    {
        if (!CanActOnAuthority || manualSkillPending != PlayerSkill.None) return;
        var state = State;
        // Delayed/duplicate input from a previous swing must not start or queue another swing.
        if (state.Skill != PlayerSkill.None || state.SwingId != observedSwingId || state.Stage != observedStage) return;
        double now = EstimatedServerTime;
        if (state.Stage == 0)
        {
            if (manual) BeginManualAttack(PlayerSkill.None, 1);
            else StartSwing(1, now);
        }
        else if (CanQueueNext(state, now))
        {
            state.NextQueued = true;
            nextSwingAssisted = manual;
            queuedAt = now;
            State = state;
            AdvanceAttack(now);
        }
    }

    private bool CanQueueNext(ComboState state, double now)
    {
        if (state.Stage < 1 || state.Stage >= 3 || state.NextQueued) return false;
        double elapsed = now - state.StartedAt;
        int index = state.Stage - 1;
        return validWindows[index] && elapsed >= 0 && elapsed < inputCloseTimes[index];
    }

    private void OnComboChanged(ComboState previous, ComboState current)
    {
        // Queue updates do not represent a new attack. Log only the owning client's new swing.
        if ((IsLocalTest || (IsClient && IsOwner)) && current.Stage > 0
            && (previous.Stage != current.Stage || previous.SwingId != current.SwingId))
            Debug.Log($"[Combo] {current.Stage}", this);
    }

    private void StartSwing(int stage, double now)
    {
        var state = State;
        state.Stage = stage;
        state.SwingId = unchecked(state.SwingId + 1);
        state.StartedAt = now;
        state.NextQueued = false;
        hitTargets.Clear();
        lastBasicHitSample = double.NegativeInfinity;
        State = state;
        SampleBasicHit(stage, 0d);
    }

    private void Update()
    {
        if (!IsActive || (!IsLocalTest && !IsServer)) return;
        MaintainHudTarget();
        double now = EstimatedServerTime;
        if (!IsLocalTest) RespawnIfDue(now);
        AdvanceAttack(now);
    }

    private void AdvanceAttack(double now)
    {
        var state = State;
        if (state.Stage == 0 && state.Skill == PlayerSkill.None) return;
        if (health.IsDead)
        {
            EndAttack(state);
            return;
        }

        double elapsed = now - state.StartedAt;
        if (state.Skill != PlayerSkill.None)
        {
            AdvanceSkill(state, elapsed);
            return;
        }
        float duration = GetAttackDuration(state.Stage);
        double transitionAt = Math.Max(transitionTimes[state.Stage - 1], queuedAt - state.StartedAt);
        bool transition = state.NextQueued && state.Stage < 3
            && validWindows[state.Stage - 1] && elapsed >= transitionAt;
        SampleBasicHit(state.Stage, transition ? transitionAt : elapsed);
        // A killing hit can move the player to a different room and reset the combo.
        if (State.SwingId != state.SwingId || State.Stage != state.Stage) return;
        if (health.IsDead)
        {
            EndAttack(state);
            return;
        }
        // Accepted input remains queued even if a slow frame crosses both marker times.
        if (transition)
        {
            if (nextSwingAssisted) BeginManualAttack(PlayerSkill.None, state.Stage + 1);
            else StartSwing(state.Stage + 1, now);
        }
        else if (elapsed >= duration)
            EndAttack(state);
    }

    private void EndAttack(ComboState state)
    {
        state.Stage = 0;
        state.NextQueued = false;
        state.Skill = PlayerSkill.None;
        state.JumpCenter = default;
        state.JumpLanded = false;
        State = state;
    }

    private void ApplyAttackHit()
    {
        // Damage and facing come exclusively from the server. No Animator is needed on the server.
        var swing = State;
        int attackDamage = PlayerAttackBalance.Current.DamageBeforeDefense(
            IsLocalTest ? damage : restoredAttack, swing.Skill, swing.Stage);
        if (attackDamage == 0) return;
        Physics.SyncTransforms();
        GetAttackArea(swing.Skill, out var origin, out var forward, out float range, out float width);
        int count = FindAttackColliders(swing.Skill, origin, forward, range, width);
        for (int i = 0; i < count; i++)
        {
            var candidate = hitColliders[i];
            if (!candidate) continue;
            var target = candidate.GetComponentInParent<NetworkHealth>();
            if (target == null || target == health || !target.CanBeTargeted || !health.SharesCombatArea(target)
                || target.EntityKind != NetworkHealth.Kind.Monster) continue;
            // A monster with an authored trigger can only be struck through that collider.
            if (target.AttackHitBox ? candidate != target.AttackHitBox : candidate.isTrigger) continue;
            if (!OverlapsAttack(target, swing.Skill, origin, forward, range, width) || !hitTargets.Add(target)) continue;
            if (!IsLocalTest && (swing.Skill == PlayerSkill.None || hitTargets.Count == 1))
                GameProgressBridge.Tutorial(this, swing.Skill == PlayerSkill.None ? "attack_hit" : "skill_hit");
            target.ApplyDamage(attackDamage, this);
            if (health.IsDead || State.SwingId != swing.SwingId || State.Stage != swing.Stage) break;
        }
        Array.Clear(hitColliders, 0, count);
    }

    private int FindAttackColliders(PlayerSkill skill, Vector3 origin, Vector3 forward, float range, float width)
    {
        bool flatArea = skill == PlayerSkill.Uppercut || skill == PlayerSkill.JumpAttack;
        bool rectangle = skill == PlayerSkill.Uppercut;
        Vector3 center = rectangle ? origin + forward * (range * .5f) : origin;
        Vector3 halfExtents = rectangle
            ? new Vector3(width * .5f, range, range * .5f) : Vector3.one * range;
        Quaternion rotation = rectangle ? Quaternion.LookRotation(forward) : Quaternion.identity;
        while (true)
        {
            int count = flatArea
                ? Physics.OverlapBoxNonAlloc(center, halfExtents, hitColliders, rotation, targetLayers, QueryTriggerInteraction.Collide)
                : Physics.OverlapSphereNonAlloc(origin, range, hitColliders, targetLayers, QueryTriggerInteraction.Collide);
            if (count < hitColliders.Length) return count;
            Array.Resize(ref hitColliders, hitColliders.Length * 2);
        }
    }

    private bool ContainsAttackTarget(PlayerSkill skill, Vector3 offset, Vector3 forward, float range, float width)
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(offset, Vector3.up);
        if (skill == PlayerSkill.Uppercut)
        {
            float depth = Vector3.Dot(horizontal, forward);
            float lateral = Vector3.Dot(horizontal, Vector3.Cross(Vector3.up, forward));
            return Mathf.Abs(offset.y) <= range && depth >= 0f && depth <= range && Mathf.Abs(lateral) <= width * .5f;
        }
        if (skill == PlayerSkill.JumpAttack)
            return Mathf.Abs(offset.y) <= range && horizontal.sqrMagnitude <= range * range;
        return offset.sqrMagnitude <= range * range && (skill == PlayerSkill.SpinningSlash
            || horizontal.sqrMagnitude <= .0001f || Vector3.Angle(forward, horizontal) <= attackAngle * .5f);
    }

    public override void OnNetworkDespawn()
    {
        CancelManualApproach();
        if (IsServer)
        {
            health.ServerDamaged -= ObserveServerDamage;
            GameProgressBridge.Unregister(this);
        }
        BossArenaLandmarks.RemoveRoom(displayedRoom);
        displayedRoom = 0;
        bossRoom.OnValueChanged -= OnBossRoomChanged;
        travelEpoch.OnValueChanged -= OnTravelEpochChanged;
        potionReadyAt.OnValueChanged -= OnPotionCooldownChanged;
        if (IsOwner && IsClient) GameProgressBridge.ClearClient();
        receivingBytes = null;
        combo.OnValueChanged -= OnComboChanged;
        hitApplied = false;
        hitTargets.Clear();
    }
}
