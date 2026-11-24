using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public sealed partial class ExpeditionUI
{
    private bool trackingFieldTap;
    private Vector2 fieldPress;
    private readonly List<RaycastResult> tapHits = new();

    private void ReadFieldTap(NetworkObject localPlayer)
    {
        if (page != Page.Field || !Application.isFocused || !GameProgressBridge.Ready)
        {
            trackingFieldTap = false;
            return;
        }

        var screen = Touchscreen.current;
        var touch = screen?.primaryTouch;
        bool pressed, released;
        Vector2 point;
        if (touch != null && (touch.press.isPressed || touch.press.wasReleasedThisFrame))
        {
            int fingers = 0;
            foreach (var contact in screen.touches)
                if (contact.press.isPressed) fingers++;
            if (fingers > 1)
            {
                trackingFieldTap = false;
                return;
            }
            point = touch.position.ReadValue();
            pressed = touch.press.wasPressedThisFrame;
            released = touch.press.wasReleasedThisFrame;
        }
        else
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            pressed = mouse.leftButton.wasPressedThisFrame;
            released = mouse.leftButton.wasReleasedThisFrame;
            if (!pressed && !released && !trackingFieldTap) return;
            point = mouse.position.ReadValue();
        }

        if (pressed)
        {
            fieldPress = point;
            trackingFieldTap = !TapHitsUI(point);
        }
        float dragLimit = Mathf.Max(8f, Screen.height * .016f);
        if (trackingFieldTap && (point - fieldPress).sqrMagnitude > dragLimit * dragLimit)
            trackingFieldTap = false;
        if (!trackingFieldTap || !released) return;
        trackingFieldTap = false;
        if (TapHitsUI(point) || !localPlayer.TryGetComponent(out PlayerCombat owner)
            || owner.InBossArena || localPlayer.GetComponent<NetworkHealth>().IsDead) return;
        var target = PickFieldPlayer(owner, point);
        if (!target) return;
        selectedPlayer = target;
        SetPage(Page.InvitePlayer);
    }

    private bool TapHitsUI(Vector2 position)
    {
        var system = EventSystem.current;
        if (!system) return false;
        var pointer = new PointerEventData(system) { position = position };
        system.RaycastAll(pointer, tapHits);
        foreach (var result in tapHits)
            if (result.module is UnityEngine.UI.GraphicRaycaster) return true;
        return false;
    }

    // Remote characters' controllers are disabled on clients; project their two-metre capsules.
    private static PlayerCombat PickFieldPlayer(PlayerCombat owner, Vector2 position)
    {
        var camera = Camera.main;
        if (!camera) return null;
        PlayerCombat best = null;
        float bestDistance = float.PositiveInfinity;
        float bestDepth = float.PositiveInfinity;
        foreach (var actor in NetworkHealth.Spawned)
        {
            if (!actor || actor.EntityKind != NetworkHealth.Kind.Player || actor.IsDead ||
                !actor.TryGetComponent(out PlayerCombat other) || !CanSelect(owner, other)) continue;
            Vector3 center = other.transform.position;
            Vector3 screenCenter = camera.WorldToScreenPoint(center);
            if (screenCenter.z <= camera.nearClipPlane) continue;
            Vector2 low = camera.WorldToScreenPoint(center - .72f * Vector3.up);
            Vector2 high = camera.WorldToScreenPoint(center + .72f * Vector3.up);
            Vector2 axis = high - low;
            float fraction = Mathf.Clamp01(Vector2.Dot(position - low, axis) / Mathf.Max(1, axis.sqrMagnitude));
            float distance = (position - low - fraction * axis).magnitude;
            float radius = Vector2.Distance((Vector2)screenCenter,
                camera.WorldToScreenPoint(center + .5f * camera.transform.right));
            if (distance > Mathf.Max(Screen.height * .022f, radius)) continue;
            if (distance > bestDistance || (Mathf.Approximately(distance, bestDistance) && screenCenter.z >= bestDepth)) continue;
            bestDistance = distance;
            bestDepth = screenCenter.z;
            best = other;
        }
        return best;
    }
}
