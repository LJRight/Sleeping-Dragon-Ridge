using TMPro;
using UnityEngine;

// Client presentation only. Damage, healing, death and abilities continue to read NetworkHealth.
[DisallowMultipleComponent]
internal sealed class DisplayedHealth : MonoBehaviour
{
    private NetworkHealth source;
    private ulong spawnId;
    private int lastFrame = -2;
    private float startTime, duration, from, fromMaximum;
    private int target, targetMaximum;
    public float Amount { get; private set; }
    public float Maximum { get; private set; }
    public int Rounded => Mathf.RoundToInt(Amount);
    public int RoundedMaximum => Mathf.Max(1, Mathf.RoundToInt(Maximum));
    public float Fraction => Maximum > 0 ? Mathf.Clamp01(Amount / Maximum) : 0;

    internal static DisplayedHealth For(NetworkHealth source)
    {
        if (!source.TryGetComponent(out DisplayedHealth view))
            view = source.gameObject.AddComponent<DisplayedHealth>();
        view.Sample();
        return view;
    }

    private void Awake() => source = GetComponent<NetworkHealth>();
    private void OnDisable() => lastFrame = -2;

    internal void Sample()
    {
        int frame = Time.frameCount;
        if (lastFrame == frame) return;
        int maximum = Mathf.Max(1, source.Maximum);
        int current = Mathf.Clamp(source.Current, 0, maximum);
        ulong id = source.IsSpawned ? source.NetworkObjectId : 0;
        float now = Time.unscaledTime;
        if (frame != lastFrame + 1 || id != spawnId)
        {
            // A first observation, a re-used pooled actor, or a previously hidden view.
            target = current; targetMaximum = maximum;
            Amount = current; Maximum = maximum;
            duration = 0;
            spawnId = id;
        }
        else
        {
            float t = duration > 0 ? Mathf.Clamp01((now - startTime) / duration) : 1f;
            Amount = Mathf.Lerp(from, target, t);
            Maximum = Mathf.Lerp(fromMaximum, targetMaximum, t);
            if (target != current || targetMaximum != maximum)
            {
                from = Amount; fromMaximum = Maximum;
                duration = current < Amount ? .18f : .26f;
                target = current; targetMaximum = maximum;
                startTime = now;
            }
        }
        lastFrame = frame;
    }
}

internal sealed class HealthGauge
{
    internal readonly RectTransform Track;
    private readonly RectTransform fill;
    private readonly Vector3 fullScale;
    private readonly TMP_Text label;
    private const string AmountFormat = "{0:0}/{1:0}";
    private string displayedName, format = AmountFormat;
    private int displayed = -1, displayedMax = -1;

    internal HealthGauge(RectTransform track, TMP_Text label)
    {
        Track = track;
        this.label = label;
        fill = UiPrefab.At<RectTransform>(track, "Fill");
        fullScale = fill.localScale;
    }

    internal void Render(DisplayedHealth view, string name = null)
    {
        var scale = fullScale;
        scale.x *= view.Fraction;
        if (fill.localScale != scale) fill.localScale = scale;
        int amount = view.Rounded, maximum = view.RoundedMaximum;
        if (displayedName != name)
        {
            displayedName = name;
            format = string.IsNullOrEmpty(name) ? AmountFormat : name + "   " + AmountFormat;
            ResetLabel();
        }
        if (amount == displayed && maximum == displayedMax) return;
        displayed = amount; displayedMax = maximum;
        label.SetText(format, amount, maximum);
    }

    internal void ResetLabel() => displayed = -1;
}
