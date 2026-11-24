using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using FingerTouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

[ExecuteAlways]
public class FollowCamera : MonoBehaviour
{
    private const float NearDistance = 3.5f;
    private const float FarDistance = 8f;
    private const float NearRollAngle = 40f;
    private const float FarRollAngle = 45f;
    [SerializeField] private Transform target;
    [SerializeField, InspectorName("Zoom Distance"), Range(NearDistance, FarDistance)]
    [Tooltip("카메라 줌 미리보기: 3.5에서 40°, 8에서 45°. Target이 지정되면 편집 중에도 적용됩니다.")]
    private float distance = 5f;
    private readonly List<RaycastResult> uiHits = new();
    private PointerEventData pointer;
    private EventSystem pointerSystem;
    private bool touchEnabled;
    private int firstId, secondId;
    private float previousSpan;
    private PlayerCombat owner;
    private NetworkHealth watchedPlayer;
    private ulong? watchedId;
    internal int SpectatorTargetCount { get; private set; }
    internal NetworkHealth WatchedPlayer => watchedPlayer;
    public Transform Target => owner && owner.IsSpectatingBoss
        ? (watchedPlayer ? watchedPlayer.transform : null) : target;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        owner = target ? target.GetComponent<PlayerCombat>() : null;
        watchedPlayer = null;
        watchedId = null;
        SpectatorTargetCount = 0;
        previousSpan = 0f;
    }

    public void ClearTarget(Transform previousTarget)
    {
        if (target == previousTarget) SetTarget(null);
    }

    private void OnDisable()
    {
        previousSpan = 0f;
        if (touchEnabled) EnhancedTouchSupport.Disable();
        touchEnabled = false;
    }

    private void LateUpdate()
    {
        if (!target) return;
        if (owner && owner.IsSpectatingBoss) SelectSpectatorTarget(false);
        else if (watchedId.HasValue) SetTarget(target);
        var subject = Target;
        if (!subject) return;
        distance = Mathf.Clamp(distance, NearDistance, FarDistance);
        if (Application.IsPlaying(gameObject)) UpdatePinch();
        float angle = Mathf.Lerp(NearRollAngle, FarRollAngle, Mathf.InverseLerp(NearDistance, FarDistance, distance));
        var rotation = Quaternion.Euler(angle, transform.eulerAngles.y, 0f);
        transform.SetPositionAndRotation(subject.position + rotation * Vector3.back * distance, rotation);
    }

    internal void NextSpectatorTarget()
    {
        if (owner && owner.IsSpectatingBoss) SelectSpectatorTarget(true);
    }

    private void SelectSpectatorTarget(bool advance)
    {
        NetworkHealth first = null, next = null, current = null;
        SpectatorTargetCount = 0;
        var actors = NetworkHealth.Spawned;
        for (int i = 0; i < actors.Count; i++)
        {
            var actor = actors[i];
            if (!actor || !actor.IsSpawned || !actor.NetworkObject.IsPlayerObject || actor.IsDead
                || actor.EntityKind != NetworkHealth.Kind.Player || actor.OwnerClientId == owner.OwnerClientId
                || actor.CombatRoom != owner.BossRoomId) continue;
            SpectatorTargetCount++;
            ulong id = actor.OwnerClientId;
            if (!first || id < first.OwnerClientId) first = actor;
            if (id == watchedId) current = actor;
            if (watchedId.HasValue && id > watchedId.Value && (!next || id < next.OwnerClientId)) next = actor;
        }
        if (current && !advance) watchedPlayer = current;
        else watchedPlayer = next ? next : first;
        if (watchedPlayer) watchedId = watchedPlayer.OwnerClientId;
    }

    private void UpdatePinch()
    {
        if (!touchEnabled)
        {
            EnhancedTouchSupport.Enable();
            touchEnabled = true;
        }
        var touches = FingerTouch.activeTouches;
        if (touches.Count != 2 || !touches[0].isInProgress || !touches[1].isInProgress)
        {
            previousSpan = 0f;
            return;
        }
        var a = touches[0];
        var b = touches[1];
        if (a.touchId > b.touchId) (a, b) = (b, a);
        bool newPair = previousSpan <= 0f || firstId != a.touchId || secondId != b.touchId;
        if ((newPair && (HitsUI(a.startScreenPosition) || HitsUI(b.startScreenPosition)))
            || HitsUI(a.screenPosition) || HitsUI(b.screenPosition))
        {
            previousSpan = 0f;
            return;
        }
        float span = Vector2.Distance(a.screenPosition, b.screenPosition);
        if (!newPair && span >= 1f)
            distance = Mathf.Clamp(distance * previousSpan / span, NearDistance, FarDistance);
        previousSpan = span >= 1f ? span : 0f;
        firstId = a.touchId;
        secondId = b.touchId;
    }

    private bool HitsUI(Vector2 position)
    {
        var system = EventSystem.current;
        if (!system) return false;
        if (pointer == null || pointerSystem != system)
        {
            pointerSystem = system;
            pointer = new PointerEventData(system);
        }
        pointer.Reset();
        pointer.position = position;
        system.RaycastAll(pointer, uiHits);
        foreach (var hit in uiHits)
            if (hit.module is GraphicRaycaster) return true;
        return false;
    }
}
