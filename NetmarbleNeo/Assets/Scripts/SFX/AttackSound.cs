using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator), typeof(AudioSource))]
public sealed class AttackSound : MonoBehaviour
{
    [Tooltip("Sword swing sounds in basic attack order (1, 2, 3).")]
    [SerializeField] private AudioClip[] basicAttackClips = new AudioClip[3];
    private PlayerCombat player;
    private AudioSource source;
    private Animator animator;

    private void Awake()
    {
        player = GetComponentInParent<PlayerCombat>();
        source = GetComponent<AudioSource>();
        animator = GetComponent<Animator>();
    }
    public void PlaySwingSound(AnimationEvent swing)
    {
        if (!player || !player.IsActive || !player.HasPresentation || !source) return;

        int stage = player.CurrentCombo.Stage;
        if (basicAttackClips == null || stage < 1 || stage > basicAttackClips.Length) return;

        // A fading-out attack can still emit its marker after the next swing starts.
        if (animator.IsInTransition(0)
            && swing.animatorStateInfo.fullPathHash != animator.GetNextAnimatorStateInfo(0).fullPathHash)
            return;

        AudioClip clip = basicAttackClips[stage - 1];
        if (clip) source.PlayOneShot(clip);
    }
}
