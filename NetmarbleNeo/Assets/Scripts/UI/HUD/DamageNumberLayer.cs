using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

public sealed class DamageNumberLayer : MonoBehaviour
{
    [SerializeField] private TMP_Text numberTemplate;
    [SerializeField] private Color receivedDamageColor = new Color(1f, .32f, .32f);
    [SerializeField, Min(.05f)] private float lifetime = .85f;
    [SerializeField, Min(0f)] private float rise = 76f;
    [SerializeField, Min(0f)] private float horizontalSpacing = 36f;

    private sealed class Popup
    {
        internal TMP_Text label;
        internal Vector3 hitPosition;
        internal float startedAt;
        internal float horizontalOffset;
        internal float initialAlpha;
        internal bool playing;
    }

    private static DamageNumberLayer current;
    private readonly List<Popup> pool = new();
    private RectTransform layer;

    private void Awake()
    {
        layer = (RectTransform)transform;
        numberTemplate.gameObject.SetActive(false);
        enabled = false;
    }

    internal static void Show(Vector3 hitPosition, int damage, bool received = false)
    {
        if (!current)
        {
            var hud = FindFirstObjectByType<PrototypeCombatHud>();
            if (!hud) return;
            current = UiPrefab.Spawn("Combat/DamageNumbers", hud.SafeControls).GetComponent<DamageNumberLayer>();
        }
        current.Play(hitPosition, damage, received);
    }

    private void Play(Vector3 hitPosition, int damage, bool received)
    {
        Popup available = null;
        int nearby = 0;
        foreach (var entry in pool)
        {
            if (!entry.playing) available ??= entry;
            else if ((entry.hitPosition - hitPosition).sqrMagnitude < 1f) nearby++;
        }
        if (available == null)
        {
            available = new Popup { label = Instantiate(numberTemplate, layer, false) };
            pool.Add(available);
        }
        Color color = received ? receivedDamageColor : numberTemplate.color;
        available.label.color = color;
        available.initialAlpha = color.a;
        available.label.text = damage.ToString(CultureInfo.InvariantCulture);
        available.hitPosition = hitPosition;
        available.horizontalOffset = nearby == 0 ? 0 : (nearby % 2 == 1 ? 1 : -1) * horizontalSpacing;
        available.startedAt = Time.unscaledTime;
        available.playing = true;
        enabled = true;
    }

    private void LateUpdate()
    {
        Camera camera = Camera.main;
        float now = Time.unscaledTime;
        bool anyPlaying = false;
        foreach (var entry in pool)
        {
            if (!entry.playing) continue;
            float progress = Mathf.Clamp01((now - entry.startedAt) / lifetime);
            if (progress >= 1f)
            {
                entry.playing = false;
                entry.label.gameObject.SetActive(false);
                continue;
            }
            anyPlaying = true;
            Vector3 screen = camera ? camera.WorldToScreenPoint(entry.hitPosition) : Vector3.zero;
            bool visible = camera && screen.z >= camera.nearClipPlane && screen.z <= camera.farClipPlane
                && camera.pixelRect.Contains((Vector2)screen) && !ExpeditionUI.BlocksGameplay;
            if (entry.label.gameObject.activeSelf != visible) entry.label.gameObject.SetActive(visible);
            if (!visible) continue;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, screen, null, out var point);
            point += numberTemplate.rectTransform.anchoredPosition;
            point += new Vector2(entry.horizontalOffset, rise * progress);
            entry.label.rectTransform.position = layer.TransformPoint(point);
            entry.label.alpha = entry.initialAlpha * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.4f, 1f, progress)));
        }
        enabled = anyPlaying;
    }
}
