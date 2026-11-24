using UnityEngine;
using UnityEngine.EventSystems;

// The screens inside this stage share an authored size and keep their proportions.
[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
public sealed class FitUiStage : UIBehaviour
{
    private RectTransform stage;
    private RectTransform parent;
    private Vector2 lastAvailable, lastDesign;

    protected override void OnEnable()
    {
        base.OnEnable();
        stage = (RectTransform)transform;
        parent = stage.parent as RectTransform;
        lastAvailable = Vector2.zero;
        Fit();
    }

    protected override void OnTransformParentChanged()
    {
        base.OnTransformParentChanged();
        parent = transform.parent as RectTransform;
        lastAvailable = Vector2.zero;
    }

    // A fixed-size stage need not receive a dimensions callback when its safe-area parent changes.
    private void LateUpdate() => Fit();

    private void Fit()
    {
        if (!parent || !stage) return;
        Vector2 available = parent.rect.size, design = stage.rect.size;
        if (available.x <= 0 || available.y <= 0 || design.x <= 0 || design.y <= 0) return;
        if (available == lastAvailable && design == lastDesign) return;
        lastAvailable = available;
        lastDesign = design;
        float scale = Mathf.Min(available.x / design.x, available.y / design.y);
        stage.localScale = new Vector3(scale, scale, 1f);
    }
}
