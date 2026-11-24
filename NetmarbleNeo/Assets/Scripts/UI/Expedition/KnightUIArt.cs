using TMPro;
using UnityEngine;

public sealed class KnightUIArt : ScriptableObject
{
    [SerializeField] private Sprite[] sprites;
    private static KnightUIArt catalog;
    private static TMP_FontAsset koreanFont;

    public static Sprite Find(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (!catalog) catalog = Resources.Load<KnightUIArt>("KnightUIArt");
        if (catalog)
            foreach (var sprite in catalog.sprites)
                if (sprite && sprite.name == key) return sprite;
        return null;
    }

    public static TMP_FontAsset Font
    {
        get
        {
            if (koreanFont) return koreanFont;
            // OS-installed Korean typefaces avoid bundling a device-specific licensed font.
            foreach (var family in new[] { "Apple SD Gothic Neo", "AppleGothic", "Noto Sans CJK KR", "Noto Sans KR", "Malgun Gothic" })
            {
                var candidate = TMP_FontAsset.CreateFontAsset(family, "Regular", 48);
                if (candidate && candidate.HasCharacter('장', false, true))
                {
                    koreanFont = candidate;
                    break;
                }
            }
            if (!koreanFont) koreanFont = TMP_Settings.defaultFontAsset;
            return koreanFont;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        catalog = null;
        koreanFont = null;
    }
}
