using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public sealed class CircularRaycastFilter : MonoBehaviour, ICanvasRaycastFilter
{
    public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        var rectTransform = (RectTransform)transform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, screenPoint, eventCamera, out var localPoint))
            return false;

        Rect rect = rectTransform.rect;
        float radius = Mathf.Min(rect.width, rect.height) * .5f;
        return (localPoint - rect.center).sqrMagnitude <= radius * radius;
    }
}
