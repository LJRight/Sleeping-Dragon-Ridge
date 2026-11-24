using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One overlay covers both the authenticated handoff and subsequent, per-player map changes.
internal sealed class WorldLoadingScreen : MonoBehaviour
{
    private const double MinimumDisplaySeconds = 5d;
    [SerializeField] private string[] tips;
    private static WorldLoadingScreen current;
    private static int nextTip;
    private RawImage illustration;
    private TextMeshProUGUI caption, tip;
    private RectTransform bar;
    private Vector3 fullBarScale;
    private string destination;
    private int destinationRoom;
    private double visibleUntil;
    internal static bool Visible => current;
    internal static bool WithinMinimumDisplayTime => current && Time.realtimeSinceStartupAsDouble < current.visibleUntil;

    internal static void Show(string scene, int room = 0)
    {
        if (!current)
        {
            var overlay = UiPrefab.Spawn("Loading/WorldLoadingScreen", null);
            DontDestroyOnLoad(overlay.gameObject);
            current = overlay.GetComponent<WorldLoadingScreen>();
        }
        if (current.destination == scene && current.destinationRoom == room) return;
        bool field = scene == SceneWorld.Field, boss = scene == SceneWorld.Boss;
        if (current.destination != scene)
        {
            current.ReleaseTexture();
            string key = field ? "HuntingField" : boss ? "BossRoom" : "Village";
            current.illustration.texture = Resources.Load<Texture2D>("WorldLoading/" + key);
            current.FitIllustration();
        }
        current.destination = scene;
        current.destinationRoom = room;
        current.caption.text = field ? "사냥터로 이동 중" : boss ? "보스방으로 이동 중" : "마을로 이동 중";
        current.ShowNextTip();
        current.visibleUntil = double.PositiveInfinity;
        SetProgress(0f);
    }

    private void LateUpdate()
    {
        // Count from the first presentation frame, after synchronous texture and UI setup.
        if (double.IsPositiveInfinity(visibleUntil))
            visibleUntil = Time.realtimeSinceStartupAsDouble + MinimumDisplaySeconds;
    }

    private void ShowNextTip()
    {
        if (tips == null || tips.Length == 0) { tip.text = ""; return; }
        int index = nextTip % tips.Length;
        tip.text = tips[index];
        nextTip = (index + 1) % tips.Length;
    }

    internal static void SetProgress(float progress)
    {
        if (!current) return;
        var scale = current.fullBarScale;
        scale.x *= Mathf.Clamp01(progress);
        current.bar.localScale = scale;
    }

    internal static void Hide()
    {
        if (!current) return;
        current.gameObject.SetActive(false);
        current.ReleaseTexture();
        Destroy(current.gameObject);
        current = null;
    }

    private void ReleaseTexture()
    {
        var previous = illustration.texture;
        illustration.texture = null;
        if (previous) Resources.UnloadAsset(previous);
    }

    private void Awake()
    {
        illustration = UiPrefab.At<RawImage>(transform, "Destination Illustration");
        caption = UiPrefab.At<TextMeshProUGUI>(transform, "Safe Area/Loading Caption/Label");
        tip = UiPrefab.At<TextMeshProUGUI>(transform, "Safe Area/Loading Caption/Tip");
        bar = UiPrefab.At<RectTransform>(transform, "Safe Area/Loading Caption/Progress");
        fullBarScale = bar.localScale;
    }

    private void OnRectTransformDimensionsChange() => FitIllustration();

    private void FitIllustration()
    {
        if (!illustration || !illustration.texture) return;
        var bounds = illustration.rectTransform.rect;
        if (bounds.width <= 0 || bounds.height <= 0) return;
        // Center-crop the texture inside the authored rectangle, including on ultrawide screens.
        float imageAspect = (float)illustration.texture.width / illustration.texture.height;
        float rectAspect = bounds.width / bounds.height;
        float u = Mathf.Min(1f, rectAspect / imageAspect);
        float v = Mathf.Min(1f, imageAspect / rectAspect);
        illustration.uvRect = new Rect((1f - u) * .5f, (1f - v) * .5f, u, v);
    }
}
