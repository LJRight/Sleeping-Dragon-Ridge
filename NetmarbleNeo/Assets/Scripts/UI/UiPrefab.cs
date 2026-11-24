using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Layout comes from the saved prefab. Gameplay controllers bind data and actions to its controls.
internal static class UiPrefab
{
    private static TMP_FontAsset authoringFont;

    internal static RectTransform Spawn(string asset, Transform parent)
    {
        var source = Resources.Load<GameObject>("UI/" + asset);
        if (!source) throw new InvalidOperationException($"Missing UI prefab: UI/{asset}");
        var view = UnityEngine.Object.Instantiate(source, parent, false).GetComponent<RectTransform>();
        view.name = source.name;
        // Prefab Mode previews Hangul with a Dynamic OS font. Clients use their installed family.
        if (!authoringFont) authoringFont = Resources.Load<TMP_FontAsset>("ExpeditionLabelFont");
        foreach (var label in view.GetComponentsInChildren<TMP_Text>(true))
        {
            if (label.font == authoringFont)
                label.font = KnightUIArt.Font;
        }
        return view;
    }

    internal static T At<T>(Transform root, string path) where T : Component
    {
        var child = root.Find(path);
        if (child && child.TryGetComponent<T>(out var component)) return component;
        throw new InvalidOperationException($"UI prefab {root.name} requires {path} ({typeof(T).Name}).");
    }

    internal static void Listen(Button button, Action action, bool playClick = true)
    {
        button.onClick.AddListener(() =>
        {
            if (playClick) GameplayUiAudio.PlayClick();
            action();
        });
    }
}
