using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class PlayerFootsteps : MonoBehaviour
{
    [SerializeField] private AudioClip[] clips;

    private PlayerCombat player;
    private AudioSource source;

    private void Awake()
    {
        player = GetComponentInParent<PlayerCombat>();
        source = GetComponent<AudioSource>();
    }

    // Both movement clips can emit events during a crossfade.
    public void PlayFootstep(AnimationEvent step)
    {
        if (!player || !player.IsActive || !player.HasPresentation || player.IsAttacking
            || step.animatorClipInfo.weight <= 0.5f || clips == null || clips.Length == 0)
            return;

        source.PlayOneShot(clips[Random.Range(0, clips.Length)]);
    }
}
