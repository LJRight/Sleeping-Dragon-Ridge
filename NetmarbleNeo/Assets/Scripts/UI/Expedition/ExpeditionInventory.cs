using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static ExpeditionWidgets;

public sealed partial class ExpeditionUI
{
    private static readonly string[] Slots = { "sword", "helm", "armor" };
    private static readonly string[] StatKeys = { "Health", "Attack", "Defense" };
    private string confirmingEnhance, lastEnhanceAction;
    private ForgeSounds forgeSounds;
    private bool enhancingEquippedItem;
    internal bool QuestForgeBusy => questOwnsWindow && page == Page.Forge && forgeSounds && forgeSounds.Busy;

    internal static void ForgeRejected()
    {
        if (current && current.forgeSounds && current.forgeSounds.Busy) current.forgeSounds.Clear();
    }
    private static string SlotName(string slot) => slot switch { "sword" => "검", "helm" => "투구", "armor" => "방어구", _ => "재료" };

    private static TMP_Text CaptionAt(Transform parent, string path, string value)
    {
        var label = UiPrefab.At<TMP_Text>(parent, path);
        label.text = value;
        return label;
    }

    private static Button ClickAt(Transform parent, string path, Action action, bool playClick = true)
    {
        var button = UiPrefab.At<Button>(parent, path);
        UiPrefab.Listen(button, action, playClick);
        return button;
    }

    private void DrawInventory()
    {
        var window = UiPrefab.Spawn("Inventory/InventoryWindow", modal);
        if (page == Page.Forge) CaptionAt(window, "Title", "대장간 · 장비 강화");
        if (page == Page.Vendor) CaptionAt(window, "Title", "경비대장 · 사냥 장비 판매");
        ClickAt(window, "InventoryClose", () => SetPage(Page.Field));
        if (!BindProgressState(window)) return;

        var data = window.Find("Data");
        DrawCharacter(data);
        DrawItems(data);
        if (page == Page.Forge)
        {
            var gear = snapshot.Item(confirmingEnhance);
            if (gear?.nextEnhance?.success > 0) DrawEnhanceConfirm(gear, gear.nextEnhance);
            return;
        }
        if (string.IsNullOrEmpty(selectedItem)) return;
        var item = snapshot.Item(selectedItem);
        if (item != null && (page != Page.Vendor || item.sellGold > 0)) DrawDetail(item);
        else selectedItem = null;
    }

    private bool BindProgressState(Transform window)
    {
        var waiting = window.Find("Waiting");
        waiting.gameObject.SetActive(snapshot == null);
        window.Find("Data").gameObject.SetActive(snapshot != null);
        if (snapshot != null) return true;

        CaptionAt(waiting, "Status", GameProgressBridge.Status ?? "서버에서 진행을 확인하고 있습니다.");
        ClickAt(waiting, "RetryProgress", () => GameProgressBridge.Refresh());
        return false;
    }

    private static void SetItemImage(Transform parent, string path, Sprite sprite)
    {
        var image = UiPrefab.At<Image>(parent, path);
        image.sprite = sprite;
        image.enabled = sprite != null;
    }

    private static void BindTile(Transform tile, ExpeditionItem item)
    {
        var sprite = item == null ? null : KnightUIArt.Find(item.Icon);
        SetItemImage(tile, "Artwork", sprite);
        var fallback = CaptionAt(tile, "Fallback", item?.IsGear == true ? "장비" : "재료");
        fallback.enabled = item != null && !sprite;
        SetItemImage(tile, "Frame", KnightUIArt.Find(item?.GradeFrame ?? "slot_common"));
        tile.Find("Equipped").gameObject.SetActive(item?.equipped == true);
    }

    private void DrawCharacter(Transform data)
    {
        CaptionAt(data, "Level", $"Lv. {snapshot.level}");
        var layers = data.Find("Knight Portrait/Appearance");
        foreach (string slot in Slots)
        {
            var worn = snapshot.Worn(slot);
            SetItemImage(layers, slot, worn == null ? null : KnightUIArt.Find(worn.Icon));
            var equipment = data.Find("Equipment/" + slot);
            var tile = equipment.Find("Icon");
            BindTile(tile, worn);
            var button = tile.GetComponent<Button>();
            // An empty equipment slot has no action and retains the standard slot artwork.
            button.enabled = worn != null && page != Page.Vendor && (page != Page.Forge || worn.nextEnhance?.success > 0);
            if (worn != null) UiPrefab.Listen(button, () => SelectItem(worn.instanceId));
            CaptionAt(equipment, "Name", worn?.name ?? "미장착").color = worn != null ? Pale : Muted;
        }

        for (int i = 0; i < StatKeys.Length; i++)
            CaptionAt(data, "Stats/" + StatKeys[i] + "/Value", ExpeditionStats.Value(snapshot.stats, i).ToString("N0"));
    }

    private void DrawItems(Transform data)
    {
        var items = snapshot.items ?? Array.Empty<ExpeditionItem>();
        bool forge = page == Page.Forge;
        bool vendor = page == Page.Vendor;
        if (forge) items = items.Where(i => i.IsGear).ToArray();
        if (vendor) items = items.Where(i => i.sellGold > 0).ToArray();
        string lessonSword = forge && snapshot.Quest("first_temper")?.state == "active" ? snapshot.starterSwordInstanceId : null;
        CaptionAt(data, "ItemCount", vendor ? $"판매할 장비 선택  ({items.Length})"
            : forge ? $"강화할 장비 선택  ({items.Length})" : $"보유 아이템  {items.Length}");
        CaptionAt(data, "SortItems/Label", byType ? "정렬 · 타입" : "정렬 · 이름");
        ClickAt(data, "SortItems", () =>
        {
            byType = !byType;
            scrollPosition = 1;
            bagScroll = null;
            DrawPage();
        });
        var sorted = byType
            ? items.OrderBy(i => i.IsGear ? Array.IndexOf(Slots, i.slot) : 9).ThenBy(i => i.name, StringComparer.Ordinal).ThenBy(i => i.instanceId, StringComparer.Ordinal)
            : items.OrderBy(i => i.name, StringComparer.Ordinal).ThenBy(i => i.instanceId, StringComparer.Ordinal);

        bagScroll = UiPrefab.At<ScrollRect>(data, "Bag Scroll");
        var cards = bagScroll.content.GetComponent<UiRepeatingList>().Populate(items.Length);
        bagScroll.content.Find("Empty").gameObject.SetActive(items.Length == 0);
        if (vendor && items.Length == 0)
            CaptionAt(bagScroll.content, "Empty", "판매할 미장착 사냥 장비가 없습니다.");
        int index = 0;
        foreach (var item in sorted)
        {
            var card = cards[index++];
            card.name = "Item_" + item.instanceId;
            var select = card.GetComponent<Button>();
            UiPrefab.Listen(select, () => SelectItem(item.instanceId));
            bool capped = forge && !(item.nextEnhance?.success > 0);
            select.interactable = !capped;
            BindTile(card.Find("Icon"), item);
            CaptionAt(card, "Name", item.name);
            string summary = item.IsGear ? $"{item.Rank}  +{item.enhancement}" : $"수량  {item.quantity}";
            if (vendor) summary += $" · {item.sellGold} G";
            if (item.instanceId == lessonSword) summary += " · 의뢰";
            CaptionAt(card, "Summary", capped ? summary + " · 최대" : summary).color = item.IsGear ? Gold : Muted;
        }
        Canvas.ForceUpdateCanvases();
        bagScroll.verticalNormalizedPosition = scrollPosition;
        CaptionAt(data, "Gold", snapshot.gold.ToString("N0") + " G");
    }

    private void SelectItem(string id)
    {
        if (page == Page.Forge) confirmingEnhance = id;
        else selectedItem = id;
        DrawPage();
    }

    private void DrawDetail(ExpeditionItem item)
    {
        var overlay = UiPrefab.Spawn("Inventory/ItemDetail", modal);
        var pane = overlay.Find("Item Detail");
        CaptionAt(pane, "Title", page == Page.Vendor ? "경비대장 · 판매 확인" : item.IsGear ? "장비 상세" : "아이템 상세");
        ClickAt(pane, "DetailClose", Back);
        BindTile(pane.Find("Selected Icon"), item);
        CaptionAt(pane, "Name", item.name);
        CaptionAt(pane, "Summary", item.IsGear ? $"{item.Rank}   ·   {SlotName(item.slot)}   ·   +{item.enhancement}" : $"보유 수량  {item.quantity}");
        pane.Find("Material").gameObject.SetActive(!item.IsGear);
        var gear = pane.Find("Gear");
        gear.gameObject.SetActive(item.IsGear);
        if (!item.IsGear)
        {
            CaptionAt(pane, "Material/Quantity", item.quantity.ToString("N0"));
            return;
        }

        for (int i = 0; i < StatKeys.Length; i++)
        {
            int baseValue = ExpeditionStats.Value(item.@base, i);
            int rank = ExpeditionStats.Value(item.rarityBonus, i);
            int plus = ExpeditionStats.Value(item.enhanceBonus, i);
            var row = gear.Find("Stats/" + StatKeys[i]);
            CaptionAt(row, "Value", $"{baseValue}   (+{rank + plus})");
            CaptionAt(row, "Bonus", $"+{rank}   /   +{plus}");
        }
        gear.Find("Ridge Set").gameObject.SetActive(item.IsRidgeSword);
        gear.Find("Armor Set").gameObject.SetActive(!item.IsRidgeSword);
        if (item.IsRidgeSword)
        {
            var sets = item.setBonuses ?? Array.Empty<ExpeditionSetBonus>();
            CaptionAt(gear, "Ridge Set/Count", string.Join("   ", sets.Select(s => $"{s.setName} {s.setCount}/3")));
        }
        else
        {
            var set = gear.Find("Armor Set");
            CaptionAt(set, "Name", $"{item.setName} 세트   {item.setCount}/3");
            BindEffectRow(set.Find("Two Pieces"), item, 2, item.setTwo);
            BindEffectRow(set.Find("Three Pieces"), item, 3, item.setThree);
        }

        if (page == Page.Vendor)
        {
            CaptionAt(gear, "DetailAction/Label", $"{item.sellGold} G에 판매");
            ClickAt(gear, "DetailAction", () =>
            {
                if (!CanSell || selectedItem != item.instanceId || !(snapshot?.Item(selectedItem)?.sellGold > 0)) return;
                GameProgressBridge.Request("sell", selectedItem, "warden");
                selectedItem = null;
                DrawPage();
            }, playClick: false).interactable = CanSell && item.sellGold > 0;
            return;
        }

        CaptionAt(gear, "DetailAction/Label", item.equipped ? "장착 해제" : "장착");
        var equip = ClickAt(gear, "DetailAction", () =>
        {
            var selected = snapshot?.Item(selectedItem);
            if (selected?.IsGear == true)
                GameProgressBridge.Request(selected.equipped ? "unequip" : "equip", selected.instanceId);
        }, playClick: false);
        var player = Unity.Netcode.NetworkManager.Singleton?.LocalClient?.PlayerObject;
        equip.interactable = item.equipped || !item.IsRidgeSword || !player || !player.GetComponent<PlayerCombat>().InBossArena;
    }

    private static void BindEffectRow(Transform row, ExpeditionItem item, int pieces, ExpeditionStats effect)
    {
        bool active = item.setCount >= pieces;
        CaptionAt(row, "State", $"{pieces}부위 · {(active ? "활성" : "비활성")}").color = active ? Ice : Muted;
        CaptionAt(row, "Effect", effect?.Format() ?? "추가 효과 없음").color = active ? Pale : Muted;
    }

    private bool CanUseForge => UsesWorldMaps && ownHealth && !ownHealth.IsDead && InReach("smith");
    private bool CanSell => UsesWorldMaps && ownHealth && !ownHealth.IsDead && InReach("warden");

    private void OpenForge()
    {
        if (CanUseForge) SetPage(Page.Forge);
    }

    internal bool ShowQuestForge(string swordId)
    {
        if (!CanUseForge) return false;
        if (page != Page.Forge)
        {
            OpenForge();
            questOwnsWindow = true;
            return false;
        }
        if (confirmingEnhance == swordId) return true;
        SelectItem(swordId);
        return false;
    }

    internal void ConfirmForge(string itemId)
    {
        if (page != Page.Forge || !CanUseForge || confirmingEnhance != itemId || !GameProgressBridge.Ready) return;
        if (forgeSounds && !forgeSounds.Begin(itemId)) return;
        enhancingEquippedItem = snapshot?.Item(itemId)?.equipped == true;
        confirmingEnhance = null;
        statusLabel.text = "장비를 강화하고 있습니다…";
        GameProgressBridge.Request(questOwnsWindow ? "temper" : "enhance", itemId, "smith");
        DrawPage();
    }

    private void CancelForgeConfirmation()
    {
        if (questOwnsWindow && QuestAutoProgress.Active)
        {
            QuestAutoProgress.Toggle();
            return;
        }
        confirmingEnhance = null;
        DrawPage();
    }

    private void DrawEnhanceConfirm(ExpeditionItem item, ExpeditionEnhanceOffer offer)
    {
        var shield = UiPrefab.Spawn("Inventory/EnhanceConfirmation", modal);
        var panel = shield.Find("Enhance Confirmation");
        CaptionAt(panel, "Title", $"{item.name}  +{item.enhancement} → +{item.enhancement + 1}");
        string statName = offer.stat switch
        {
            "hp" => "최대 체력", "attack" => "공격력", "defense" => "방어력", _ => null
        };
        CaptionAt(panel, "StatPreview", statName == null ? "" :
            $"강화 성공 시   {statName} {offer.statBefore} → {offer.statAfter} (+{offer.statAfter - offer.statBefore})");
        int ore = snapshot.Item("stack:ore")?.quantity ?? 0;
        int rare = snapshot.Item("stack:rare_ore")?.quantity ?? 0;
        string costs = $"골드 {snapshot.gold}/{offer.gold}     강화석 {ore}/{offer.ore}";
        if (offer.rareOre > 0) costs += $"     정제 강화석 {rare}/{offer.rareOre}";
        CaptionAt(panel, "Costs", costs);
        CaptionAt(panel, "Chances", $"성공 {offer.success}%   유지 {offer.keep}%   하락 {offer.downgrade}%   파괴 {offer.destroy}%");
        string risk = item.IsRidgeSword ? "잠든 용의 검은 파괴될 확률도 1단계 하락에 포함됩니다."
            : offer.destroy > 0 ? "파괴되면 이 장비를 잃습니다." : "";
        CaptionAt(panel, "Risk", "결과와 관계없이 비용을 소모합니다.\n" + risk);
        ClickAt(panel, "CancelEnhance", CancelForgeConfirmation);
        bool affordable = snapshot.gold >= offer.gold && ore >= offer.ore && rare >= offer.rareOre;
        CaptionAt(panel, "ConfirmEnhance/Label", affordable ? "강화 시도" : "재료 또는 골드 부족");
        ClickAt(panel, "ConfirmEnhance", () => ConfirmForge(item.instanceId), playClick: false).interactable =
            affordable && CanUseForge && !(forgeSounds && forgeSounds.Busy);
    }

    private bool ShowEnhanceResult(ExpeditionEnhanceResult result)
    {
        if (string.IsNullOrEmpty(result?.actionId) || result.actionId == lastEnhanceAction) return false;
        lastEnhanceAction = result.actionId;
        string message = result.outcome switch
        {
            "success" => $"강화 성공   +{result.before} → +{result.after}",
            "downgrade" => $"강화 단계 하락   +{result.before} → +{result.after}",
            "destroy" => "강화 실패 · 장비가 파괴됐습니다.",
            _ => $"강화 단계 유지   +{result.after}"
        };
        void Present()
        {
            if (questOwnsWindow && page == Page.Forge) QuestAutoProgress.HoldCurrentScreen();
            // Include any newer inventory refresh received while the hammer sound was playing.
            PresentProgress(message);
        }
        if (forgeSounds) forgeSounds.Finish(result, Present);
        else Present();
        return true;
    }
}
