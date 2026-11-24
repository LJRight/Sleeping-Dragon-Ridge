using UnityEngine;

[RequireComponent(typeof(BossCombat), typeof(Animator))]
public sealed class BossAnimationView : MonoBehaviour
{
    private static readonly int PhaseParameter = Animator.StringToHash("Phase");
    private static readonly int[] States = {
        State("Idle01"), State("Run"), State("Basic Attack"), State("Claw Attack"),
        State("FlameAttackState.Scream"), State("FlameAttackState.Flame Attack"),
        State("FlyingFlameAttackState.Take Off"), State("FlyingFlameAttackState.Fly Float"),
        State("FlyingFlameAttackState.Fly Flame Attack"), State("FlyingFlameAttackState.Land"),
        State("Get Hit"), State("Defend"), State("Die")
    };

    private BossCombat combat;
    [SerializeField] private AudioClip groundFlameScream;
    [SerializeField] private AudioClip defenseBreakHit;
    [SerializeField] private AudioClip flameSound;
    [SerializeField, Min(0f)] private float flameFadeOutSeconds = .5f;
    private NetworkHealth health;
    private AudioSource voice, flameVoice;
    private Animator animator;
    private uint sequence;
    private BossPhase phase;
    private bool initialized;

    private static int State(string path) => Animator.StringToHash("Base Layer." + path);

    // BossCombat reads these clip markers against server time, including on headless servers.
    public void BossReflectOpen() { }
    public void BossReflectClose() { }

    private void Awake()
    {
        combat = GetComponent<BossCombat>();
        health = GetComponent<NetworkHealth>();
        animator = GetComponent<Animator>();
    }

    private void OnDisable() => ResetView();

    private void ResetView()
    {
        initialized = false;
        if (voice) voice.Stop();
        StopFlameSound();
    }

    private bool AudibleInRoom => !combat.IsHost || health.VisibleTo(combat.NetworkManager.LocalClientId);

    private AudioSource CreateSpatialSource()
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 6f;
        source.maxDistance = 40f;
        source.dopplerLevel = 0f;
        return source;
    }

    private void PlayPhaseSound(BossPhase current, float elapsed)
    {
        if (phase == BossPhase.Scream && voice) voice.Stop();
        var clip = current switch
        {
            BossPhase.Scream => groundFlameScream,
            BossPhase.Hit => defenseBreakHit,
            _ => null
        };
        if (!clip || elapsed >= clip.length || !AudibleInRoom) return;
        if (!voice) voice = CreateSpatialSource();
        voice.clip = clip;
        voice.time = elapsed;
        voice.Play();
    }

    private void StopFlameSound()
    {
        if (!flameVoice || !flameVoice.clip) return;
        flameVoice.Stop();
        flameVoice.clip = null;
    }

    private void UpdateFlameSound(BossPhase current, float elapsed, float duration, bool phaseChanged)
    {
        if (current is not (BossPhase.Flame or BossPhase.FlyFlame) || !flameSound ||
            elapsed >= duration || elapsed >= flameSound.length || !AudibleInRoom)
        {
            StopFlameSound();
            return;
        }

        if (!flameVoice) flameVoice = CreateSpatialSource();
        float end = Mathf.Min(duration, flameSound.length);
        float fade = Mathf.Min(end, flameFadeOutSeconds);
        flameVoice.volume = fade > 0f ? Mathf.SmoothStep(0f, 1f, (end - elapsed) / fade) : 1f;
        if (!phaseChanged && flameVoice.clip) return;
        flameVoice.clip = flameSound;
        flameVoice.time = elapsed;
        flameVoice.Play();
    }

    private void Update()
    {
        if (!combat.IsSpawned)
        {
            if (initialized) ResetView();
            return;
        }
        if (!combat.IsClient) { animator.enabled = false; return; }
        var current = combat.Phase;
        float duration = combat.PhaseDuration;
        // The hover lasts two whole clips. Hit includes its state speed; Defend uses its full clip.
        float cycles = current == BossPhase.Float ? 2f : 1f;
        float elapsed = combat.PhaseElapsed;
        float normalized = duration > 0f ? Mathf.Clamp01(elapsed / duration) * cycles : 0f;
        var state = States[(int)current];
        bool phaseChanged = !initialized || sequence != combat.PhaseSequence;
        UpdateFlameSound(current, elapsed, duration, phaseChanged);
        if (phaseChanged)
        {
            PlayPhaseSound(current, elapsed);
            animator.SetInteger(PhaseParameter, (int)current);
            // For a late observer or another occurrence of the same attack, enter at the server frame.
            if (!initialized || current == phase || (duration > 0f && elapsed > .10f))
                animator.Play(state, 0, normalized);
            initialized = true;
            sequence = combat.PhaseSequence;
            phase = current;
        }
        else if (duration > 0f && elapsed < duration && !animator.IsInTransition(0))
        {
            var showing = animator.GetCurrentAnimatorStateInfo(0);
            if (showing.fullPathHash == state && Mathf.Abs(showing.normalizedTime - normalized) * duration / cycles > .10f)
                animator.Play(state, 0, normalized);
        }
    }
}
