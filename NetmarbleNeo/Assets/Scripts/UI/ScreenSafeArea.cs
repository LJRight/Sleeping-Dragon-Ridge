using UnityEngine;
using Screen = UnityEngine.Device.Screen;

// Attach to a stretch RectTransform directly below a full-screen canvas.
[DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
public sealed class ScreenSafeArea : MonoBehaviour
{
    private RectTransform area;
    private Rect lastSafeRect;
    private Vector2Int lastScreenSize;

    private void OnEnable()
    {
        area = (RectTransform)transform;
        lastScreenSize = Vector2Int.zero;
        Fit();
    }

    private void Update() => Fit();

    private void Fit()
    {
        var size = new Vector2Int(Screen.width, Screen.height);
        var safe = Screen.safeArea;
        if (size.x <= 0 || size.y <= 0 || safe.width <= 0f || safe.height <= 0f) return;
        if (size == lastScreenSize && safe == lastSafeRect) return;

        area.anchorMin = new Vector2(safe.xMin / size.x, safe.yMin / size.y);
        area.anchorMax = new Vector2(safe.xMax / size.x, safe.yMax / size.y);
        area.offsetMin = Vector2.zero;
        area.offsetMax = Vector2.zero;
        lastScreenSize = size;
        lastSafeRect = safe;
    }
}
