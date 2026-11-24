using System.Collections.Generic;
using TMPro;
using UnityEngine;

// One non-interactive overlay canvas for the visible entities in the viewer's room.
internal sealed class ActorHealthBars
{
    private sealed class Actor
    {
        internal Transform anchor;
        internal Vector3 offset;
        internal RectTransform row;
        internal HealthGauge gauge;
        internal Vector2 labelOffset;
        internal int seenFrame;

        internal Actor(NetworkHealth source)
        {
            anchor = source.transform;
            if (source.TryGetComponent(out CharacterController body))
                offset = body.center + Vector3.up * (body.height * .5f);
            else offset = Vector3.up;
            if (!source.IsArenaBoss) return;
            foreach (var bone in source.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name != "Head") continue;
                anchor = bone;
                offset = Vector3.zero;
                break;
            }
        }

        internal void CreateRow(RectTransform parent, NetworkHealth health)
        {
            string prefab = health.IsArenaBoss ? "BossHealthBar" :
                health.EntityKind == NetworkHealth.Kind.Player ? "PlayerHealthBar" : "MonsterHealthBar";
            row = UiPrefab.Spawn("Combat/" + prefab, parent);
            labelOffset = row.anchoredPosition;
            var track = UiPrefab.At<RectTransform>(row, "Health Bar");
            gauge = new HealthGauge(track, UiPrefab.At<TMP_Text>(row, "Health Text"));
        }
    }

    private readonly RectTransform root;
    private readonly Dictionary<NetworkHealth, Actor> actors = new();
    private readonly List<NetworkHealth> removed = new();

    internal ActorHealthBars(Transform parent)
    {
        root = UiPrefab.Spawn("Combat/ActorHealthBars", parent);
        root.SetAsFirstSibling();
    }

    internal void Hide()
    {
        if (root.gameObject.activeSelf) root.gameObject.SetActive(false);
    }

    internal void Draw(PlayerCombat viewer, Camera camera, bool local)
    {
        if (!viewer || !camera || ExpeditionUI.BlocksGameplay)
        {
            Hide();
            return;
        }
        if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
        var active = NetworkHealth.Spawned;
        for (int i = 0; i < active.Count; i++)
        {
            var source = active[i];
            if (!source || !source.IsActive || source.CombatRoom != viewer.BossRoomId) continue;
            if (source.IsDead && source.TryGetComponent<PlayerCombat>(out var player) && player.IsSpectatingBoss) continue;
            // The network field already gives the owner a permanent HP gauge.
            if (!local && source.transform == viewer.transform) continue;
            if (!actors.TryGetValue(source, out var entry))
            {
                entry = new Actor(source);
                actors.Add(source, entry);
            }
            entry.seenFrame = Time.frameCount;
            Vector3 head = entry.anchor.TransformPoint(entry.offset) + Vector3.up * .15f;
            Vector3 screen = camera.WorldToScreenPoint(head);
            bool visible = screen.z >= camera.nearClipPlane && screen.z <= camera.farClipPlane
                && camera.pixelRect.Contains((Vector2)screen);
            if (!visible)
            {
                if (entry.row && entry.row.gameObject.activeSelf) entry.row.gameObject.SetActive(false);
                continue;
            }
            if (!entry.row) entry.CreateRow(root, source);
            if (!entry.row.gameObject.activeSelf) entry.row.gameObject.SetActive(true);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var position);
            entry.row.anchoredPosition = position - root.rect.center + entry.labelOffset;
            entry.gauge.Render(DisplayedHealth.For(source));
        }
        removed.Clear();
        foreach (var pair in actors)
            if (pair.Value.seenFrame != Time.frameCount) removed.Add(pair.Key);
        foreach (var key in removed)
        {
            if (actors[key].row) Object.Destroy(actors[key].row.gameObject);
            actors.Remove(key);
        }
    }
}
