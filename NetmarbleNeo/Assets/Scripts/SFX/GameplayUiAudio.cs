using UnityEngine;

public sealed class GameplayUiAudio : ScriptableObject
{
    [SerializeField] private AudioClip uiDefault, dropOrSell, equipItem, bossVictory, potionDrink;

    private static GameplayUiAudio clips;
    private static AudioSource source;

    private static GameplayUiAudio Clips
    {
        get
        {
            if (!clips) clips = Resources.Load<GameplayUiAudio>(nameof(GameplayUiAudio));
            return clips;
        }
    }

    internal static void PlayClick() => Play(Clips?.uiDefault);
    internal static void PlayDropOrSell() => Play(Clips?.dropOrSell);
    internal static void PlayEquip() => Play(Clips?.equipItem);
    internal static void PlayBossVictory() => Play(Clips?.bossVictory);
    internal static void PlayPotionDrink() => Play(Clips?.potionDrink);

    private static void Play(AudioClip clip)
    {
        if (!clip) return;
        if (!source)
        {
            var emitter = new GameObject("UI Audio");
            DontDestroyOnLoad(emitter);
            source = emitter.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }
        source.PlayOneShot(clip);
    }
}
