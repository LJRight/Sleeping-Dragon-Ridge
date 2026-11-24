using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BossCombat))]
public sealed class BossBreathView : MonoBehaviour
{
    [SerializeField] private Transform flameEmitter;
    private BossCombat combat;
    private ParticleSystem flame;
    private bool firing;

    private void Awake()
    {
        combat = GetComponent<BossCombat>();
        if (!flameEmitter) return;
        flame = flameEmitter.GetComponent<ParticleSystem>();
        StopFlame();
    }

    private void LateUpdate()
    {
        if (!flame) return;
        bool shouldFire = combat.IsClient && combat.TryGetTelegraph(out var cue) && cue.danger
            && combat.PhaseElapsed < combat.PhaseDuration;
        if (shouldFire == firing) return;
        if (shouldFire)
        {
            flameEmitter.gameObject.SetActive(true);
            flame.Play(withChildren: true);
            firing = true;
        }
        else StopFlame();
    }

    private void StopFlame()
    {
        if (flame && flameEmitter.gameObject.activeSelf)
        {
            flame.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
            flameEmitter.gameObject.SetActive(false);
        }
        firing = false;
    }

    private void OnDisable() => StopFlame();
}
