using UnityEngine;

// Presentation only: server movement and combat clocks remain independent of Animator updates.
[RequireComponent(typeof(InputHandler), typeof(PlayerCombat), typeof(NetworkHealth))]
public class PlayerAnimationView : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField, Range(0f, 1f)] private float idleThreshold = 0.1f;
    [SerializeField, Range(0f, 1f)] private float runThreshold = 0.4f;
    [SerializeField, Range(0f, 1f)] private float sprintThreshold = 0.75f;

    [SerializeField, Min(0f)] private float attackBlendSeconds = 0.12f;
    [SerializeField] private AudioClip uppercutSound;

    private static readonly int MoveAmountId = Animator.StringToHash("MoveAmount");
    private static readonly int MoveBandId = Animator.StringToHash("MoveBand");
    private static readonly int AttackStepId = Animator.StringToHash("AttackStep");
    private static readonly int IdleId = Animator.StringToHash("Base Layer.Idle");
    private static readonly int SpinningSlashState = Animator.StringToHash("Base Layer.Skill State.Spinning Slash Skill");
    private static readonly int UppercutState = Animator.StringToHash("Base Layer.Skill State.Uppercut");
    private static readonly int JumpAttackState = Animator.StringToHash("Base Layer.Skill State.Jump Attack");
    private static readonly int[] AttackStates =
    {
        Animator.StringToHash("Base Layer.Attack State.Attack 1"),
        Animator.StringToHash("Base Layer.Attack State.Attack 2"),
        Animator.StringToHash("Base Layer.Attack State.Attack 3")
    };

    private InputHandler movement;
    private PlayerCombat combat;
    private NetworkHealth health;
    private AudioSource skillAudio;
    private JumpAttack jumpEffect;
    private PlayerSkill presentedStrike;
    private bool skillCuePending;
    private bool initialized;
    private bool wasDead;
    private bool hiddenForSpectating;
    private int lastStage;
    private uint lastSwing;

    private void Awake()
    {
        movement = GetComponent<InputHandler>();
        combat = GetComponent<PlayerCombat>();
        health = GetComponent<NetworkHealth>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            animator.applyRootMotion = false;
            skillAudio = animator.GetComponent<AudioSource>();
            jumpEffect = animator.GetComponent<JumpAttack>();
        }
    }

    private void Update()
    {
        if (animator == null) return;
        SetSpectatorHidden(combat.IsClient && combat.IsSpectatingBoss);
        if (hiddenForSpectating) return;
        if (!combat.IsActive)
        {
            StopStrikeAnimation();
            initialized = false;
            wasDead = false;
            return;
        }
        // A dedicated server never needs to evaluate the skeletal animation to deal damage.
        animator.enabled = combat.HasPresentation;
        if (!combat.HasPresentation) return;

        var state = combat.CurrentCombo;
        bool dead = health.IsDead;
        PlayerSkill skill = dead ? PlayerSkill.None : state.Skill;
        float amount = dead || state.Stage != 0 || skill != PlayerSkill.None ? 0f : movement.MoveAmount;
        int band = amount <= idleThreshold ? 0 : amount <= runThreshold ? 1
            : amount <= sprintThreshold ? 2 : 3;
        animator.SetFloat(MoveAmountId, amount);
        animator.SetInteger(MoveBandId, band);
        animator.SetInteger(AttackStepId, dead ? 0 : state.Stage);

        if (skill != presentedStrike) StopStrikeAnimation();
        if (skill != PlayerSkill.None)
        {
            UpdateStrikeAnimation(state);
        }
        else if (dead)
        {
            if (!wasDead) animator.Play(IdleId, 0, 0f);
            animator.speed = 0f;
        }
        else
        {
            animator.speed = 1f;
            bool changed = !initialized || state.Stage != lastStage || state.SwingId != lastSwing;
            if (changed && state.Stage > 0)
            {
                double elapsed = System.Math.Max(0, combat.EstimatedServerTime - state.StartedAt);
                float progress = Mathf.Clamp((float)(elapsed / combat.GetAttackDuration(state.Stage)), 0f, 0.999f);
                if (!initialized)
                    animator.Play(AttackStates[state.Stage - 1], 0, progress);
                else
                    animator.CrossFadeInFixedTime(AttackStates[state.Stage - 1], attackBlendSeconds, 0,
                        progress * combat.GetAttackDuration(state.Stage));
            }
        }
        initialized = true;
        wasDead = dead;
        lastStage = state.Stage;
        lastSwing = state.SwingId;
    }

    private void UpdateStrikeAnimation(PlayerCombat.ComboState state)
    {
        float elapsed = Mathf.Max(0f, (float)(combat.EstimatedServerTime - state.StartedAt));
        float hitTime = combat.GetSkillHitTime(state.Skill);
        if (presentedStrike != state.Skill || state.SwingId != lastSwing)
        {
            presentedStrike = state.Skill;
            animator.speed = 1f;
            float phase = Mathf.Clamp01(elapsed / combat.GetSkillDuration(state.Skill));
            int animState = state.Skill switch
            {
                PlayerSkill.SpinningSlash => SpinningSlashState,
                PlayerSkill.Uppercut => UppercutState,
                _ => JumpAttackState
            };
            animator.Play(animState, 0, phase);
            skillCuePending = state.Skill == PlayerSkill.JumpAttack || (state.Skill == PlayerSkill.Uppercut && elapsed <= hitTime);
        }

        if (!skillCuePending) return;
        if (state.Skill == PlayerSkill.JumpAttack)
        {
            // An observer may have passed the clip event before receiving the landing position.
            if (!state.JumpLanded) return;
            jumpEffect?.PlayJumpAttackEffect();
        }
        else
        {
            // The main discharge in LightningUppercut.wav is 0.29 seconds into the clip.
            if (elapsed < hitTime - 0.29f) return;
            if (skillAudio && uppercutSound) skillAudio.PlayOneShot(uppercutSound);
        }
        skillCuePending = false;
    }

    private void StopStrikeAnimation()
    {
        if (presentedStrike == PlayerSkill.None) return;
        if (presentedStrike == PlayerSkill.JumpAttack && jumpEffect) jumpEffect.StopEffect();
        presentedStrike = PlayerSkill.None;
        skillCuePending = false;
        if (animator && animator.isActiveAndEnabled) animator.Play(IdleId, 0, 0f);
    }

    private void SetSpectatorHidden(bool hidden)
    {
        if (hidden == hiddenForSpectating || !animator || animator.gameObject == gameObject) return;
        if (hidden)
        {
            if (!animator.gameObject.activeSelf) return;
            StopStrikeAnimation();
        }
        // Both player prefabs keep the model, equipment and skill effects on this child.
        animator.gameObject.SetActive(!hidden);
        hiddenForSpectating = hidden;
        initialized = false;
        wasDead = false;
    }

    private void OnDisable()
    {
        StopStrikeAnimation();
        SetSpectatorHidden(false);
    }
}
