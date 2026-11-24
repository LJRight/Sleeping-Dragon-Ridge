using UnityEngine;

public class UpperCut : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip sfx, voice;

    void PlayUpperCutVoice()
    {
        PlaySound(voice);
    }
    void PlayUpperCutSFX()
    {
        PlaySound(sfx);
    }
    void PlaySound(AudioClip clip)
    {
        if (clip == null || audioSource == null) return;
        audioSource.PlayOneShot(clip);
    }
}
