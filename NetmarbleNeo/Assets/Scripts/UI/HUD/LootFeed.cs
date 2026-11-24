using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
internal sealed class LootReceipt
{
    public string actionId;
    public LootEntry[] entries;
}

[Serializable]
internal sealed class LootEntry
{
    public string type, definitionId, name, rarity;
    public int quantity;
}

// A receipt can arrive before the HUD is built or while a window covers it.
internal static class LootNotifications
{
    private static readonly HashSet<string> received = new(StringComparer.Ordinal);
    private static readonly Queue<LootEntry> pending = new();
    internal static uint Session { get; private set; }

    internal static void Receive(LootReceipt receipt)
    {
        if (string.IsNullOrEmpty(receipt?.actionId) || receipt.entries == null || !received.Add(receipt.actionId))
            return;
        int previousCount = pending.Count;
        foreach (var entry in receipt.entries)
            if (entry != null && entry.quantity > 0 && !string.IsNullOrEmpty(entry.definitionId))
                pending.Enqueue(entry);
        if (pending.Count > previousCount) GameplayUiAudio.PlayDropOrSell();
    }

    internal static bool Take(out LootEntry entry) => pending.TryDequeue(out entry);

    internal static void Clear()
    {
        received.Clear();
        pending.Clear();
        Session++;
    }
}

public sealed class LootFeed : MonoBehaviour
{
    [SerializeField, Min(.1f)] private float displaySeconds = 2.5f;
    [SerializeField] private RectTransform[] slots;

    private sealed class Row
    {
        internal GameObject view;
        internal Image icon;
        internal TMP_Text name, quantity;
        internal float remaining;
    }

    private Row[] rows;
    private uint session;

    private void Awake()
    {
        rows = new Row[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            rows[i] = new Row
            {
                view = slot.gameObject,
                icon = UiPrefab.At<Image>(slot, "Icon"),
                name = UiPrefab.At<TMP_Text>(slot, "Name"),
                quantity = UiPrefab.At<TMP_Text>(slot, "Quantity")
            };
            slot.gameObject.SetActive(false);
        }
        session = LootNotifications.Session;
    }

    private void Update()
    {
        foreach (var row in rows)
        {
            if (session != LootNotifications.Session) row.remaining = 0f;
            row.remaining -= Time.unscaledDeltaTime;
            if (row.remaining > 0f) continue;
            if (!LootNotifications.Take(out var entry))
            {
                if (row.view.activeSelf) row.view.SetActive(false);
                row.remaining = 0f;
                continue;
            }

            row.icon.sprite = KnightUIArt.Find(entry.definitionId == "ridge_sword" ? "fury_sword" : entry.definitionId);
            row.name.text = entry.name;
            row.quantity.text = $"×{entry.quantity:N0}";
            row.name.color = entry.type == "gold" || entry.rarity == "legendary" ? ExpeditionWidgets.Gold
                : entry.rarity == "rare" ? ExpeditionWidgets.Ice : ExpeditionWidgets.Pale;
            row.remaining = displaySeconds;
            row.view.SetActive(true);
        }
        session = LootNotifications.Session;
    }
}
