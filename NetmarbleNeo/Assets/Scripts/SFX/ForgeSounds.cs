using System;
using System.Collections;
using UnityEngine;

public sealed class ForgeSounds : MonoBehaviour
{
    [SerializeField] private AudioClip enforce, success, keep, downgrade, broken;
    private AudioSource voice;
    internal string PendingItem { get; private set; }

    internal bool Busy => PendingItem != null;

    internal bool Begin(string item)
    {
        if (Busy) return false;
        PendingItem = item;
        if (!voice)
        {
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
        }
        voice.Stop();
        voice.clip = enforce;
        if (enforce) voice.Play();
        return true;
    }

    internal void Finish(ExpeditionEnhanceResult result, Action showResult)
    {
        if (PendingItem != result.instanceId) { showResult(); return; }
        var clip = result.outcome switch
        {
            "success" => success, "keep" => keep, "downgrade" => downgrade, "destroy" => broken, _ => null
        };
        StartCoroutine(PlayResult(clip, showResult));
    }

    private IEnumerator PlayResult(AudioClip clip, Action showResult)
    {
        // A local backend can answer before the first hammer strike is audible.
        while (voice.isPlaying) yield return null;
        PendingItem = null;
        voice.clip = clip;
        if (clip) voice.Play();
        showResult();
    }

    internal void Clear()
    {
        StopAllCoroutines();
        if (voice) voice.Stop();
        PendingItem = null;
    }

    private void OnDisable() => Clear();
}
