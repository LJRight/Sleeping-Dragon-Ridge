using UnityEngine;

public class JumpAttack : MonoBehaviour
{
    [SerializeField] private Animator effectAnim;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip voice, sfx;
    private static readonly int EffectState = Animator.StringToHash("Base Layer.FX_Weapon Effect");
    private PlayerCombat player;
    private ParticleSystem particles;
    private uint? playedSwing;
    private bool playing;
    private Vector3 authoredLocalPosition;
    private Quaternion authoredLocalRotation;
    private Vector3 landingPosition;
    private Quaternion landingRotation;

    private void Awake()
    {
        player = GetComponentInParent<PlayerCombat>();
        if (effectAnim) particles = effectAnim.GetComponent<ParticleSystem>();
    }
    public void PlayJumpAttackVoice()
    {
        if (audioSource == null || voice == null) return;
        audioSource.PlayOneShot(voice);
    }
    // The animation event and the catch-up path for remote observers share one playback per cast.
    public void PlayJumpAttackEffect()
    {
        if (!effectAnim) return;
        float age = 0f;
        if (player)
        {
            if (!player.IsActive || !player.HasPresentation || player.CurrentSkill != PlayerSkill.JumpAttack) return;
            var state = player.CurrentCombo;
            if (!state.JumpLanded || playedSwing == state.SwingId) return;
            playedSwing = state.SwingId;
            age = Mathf.Max(0f, (float)(player.EstimatedServerTime - state.StartedAt)
                - player.GetSkillHitTime(PlayerSkill.JumpAttack));
        }

        FinishPlayback();
        var effect = effectAnim.transform;
        authoredLocalPosition = effect.localPosition;
        authoredLocalRotation = effect.localRotation;
        landingPosition = player ? player.GetJumpImpactCenter() : effect.position;
        landingRotation = effect.rotation;
        playing = true;
        PinLandingPosition();
        effectAnim.enabled = true;
        effectAnim.Play(EffectState, 0, 0f);
        effectAnim.Update(0f);
        if (audioSource != null && sfx != null) audioSource.PlayOneShot(sfx);
        if (age >= effectAnim.GetCurrentAnimatorStateInfo(0).length)
        {
            FinishPlayback();
            return;
        }
        if (age > 0f) effectAnim.Update(age);
        if (particles)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (age > 0f) particles.Simulate(age, true, true, false);
            particles.Play(true);
        }
    }

    private void LateUpdate()
    {
        if (!playing) return;
        if (effectAnim.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f) FinishPlayback();
        else PinLandingPosition();
    }

    private void PinLandingPosition()
    {
        effectAnim.transform.SetPositionAndRotation(landingPosition, landingRotation);
    }

    private void FinishPlayback()
    {
        if (!playing) return;
        playing = false;
        if (particles) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (effectAnim)
        {
            effectAnim.enabled = false;
            effectAnim.transform.SetLocalPositionAndRotation(authoredLocalPosition, authoredLocalRotation);
        }
    }

    public void StopEffect()
    {
        playedSwing = null;
        FinishPlayback();
    }

    private void OnDisable() => StopEffect();
}
