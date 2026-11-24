using System;
using UnityEngine;

public class Slash : MonoBehaviour
{
    [SerializeField] private ParticleSystem effect;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip sfx, voice;
    public void PlaySlash()
    {
        if (!IsNullRef())
        {
            effect.Play();
            audioSource.PlayOneShot(voice);
            audioSource.PlayOneShot(sfx);
        }

    }
    bool IsNullRef()
    {
        return effect == null || audioSource == null || sfx == null || voice == null;
    }

}
