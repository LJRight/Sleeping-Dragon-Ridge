using System.Collections.Generic;
using UnityEngine;

// The first row and its children are editable in the owning screen prefab.
// Each copy keeps the template's rect and starts at that rect plus its grid offset.
public sealed class UiRepeatingList : MonoBehaviour
{
    [SerializeField] private RectTransform template;
    [SerializeField, Min(1)] private int columns = 1;
    [SerializeField] private Vector2 spacing;
    private readonly List<RectTransform> entries = new();
    private float minimumHeight;
    private bool initialized;

    public IReadOnlyList<RectTransform> Populate(int count)
    {
        var content = (RectTransform)transform;
        if (!initialized)
        {
            minimumHeight = content.rect.height;
            initialized = true;
        }
        template.gameObject.SetActive(false);
        while (entries.Count < count)
            entries.Add(Instantiate(template, content, false));

        int columnCount = Mathf.Max(1, columns);
        Vector2 step = template.rect.size + spacing;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            entry.gameObject.SetActive(i < count);
            if (i < count)
                entry.anchoredPosition = template.anchoredPosition + new Vector2(i % columnCount * step.x, -(i / columnCount) * step.y);
        }

        float bottom = count == 0 ? 0 : -template.anchoredPosition.y + (count - 1) / columnCount * step.y + template.rect.height * template.pivot.y;
        var viewport = (RectTransform)content.parent;
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(Mathf.Max(minimumHeight, viewport.rect.height), bottom));
        return entries;
    }
}
