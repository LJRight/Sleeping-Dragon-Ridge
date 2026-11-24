using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static UiPrefab;

public sealed partial class ExpeditionUI
{
    private TradeView shownTrade;
    private string dismissedCommit;
    private bool editingOffer;
    private int draftVersion;
    private readonly HashSet<string> draftItems = new();
    private string draftGold, draftOre, draftRareOre;

    private void UpdateTradeControls(PlayerCombat owner)
    {
        if (!owner) return;
        var view = owner.Trade;
        if (view == null)
        {
            shownTrade = null;
            dismissedCommit = null;
            editingOffer = false;
            if (page == Page.Trade) SetPage(Page.Field);
            if (Time.unscaledTime < owner.TradeNoticeUntil)
                statusLabel.text = owner.TradeNotice;
            else if (statusLabel.text == owner.TradeNotice)
                statusLabel.text = GameProgressBridge.Status;
            return;
        }
        if (view.committed && dismissedCommit == view.token) return;
        if (page != Page.Trade)
        {
            if (page != Page.Field && page != Page.InvitePlayer) return;
            if (QuestAutoProgress.Active) QuestAutoProgress.Toggle();
            shownTrade = view;
            editingOffer = false;
            SetPage(Page.Trade);
        }
        else if (shownTrade != view)
        {
            shownTrade = view;
            if (editingOffer && view.version == draftVersion && !view.committed) return;
            editingOffer = false;
            DrawPage();
        }
    }

    private void BackFromTrade()
    {
        if (editingOffer) { editingOffer = false; DrawPage(); return; }
        var owner = LocalCombat;
        var view = owner ? owner.Trade : null;
        if (view == null) { SetPage(Page.Field); return; }
        if (view.committed)
        {
            dismissedCommit = view.token;
            SetPage(Page.Field);
        }
        else owner.RequestTrade(TradeAction.Cancel, view, 0);
    }

    private void DrawTrade()
    {
        var owner = LocalCombat;
        var view = owner ? owner.Trade : null;
        if (view == null) return;
        var peer = KnightName(view.peer);
        if (view.invitation)
        {
            var invite = Spawn("Party/Invitation", modal);
            At<TMP_Text>(invite, "Title").text = "기사 간 거래 요청";
            At<TMP_Text>(invite, "Description").text = view.outgoing ? $"{peer} 님의 응답을 기다리고 있습니다."
                : $"{peer} 님이 거래를 요청했습니다.";
            Listen(At<Button>(invite, "CloseParty"), BackFromTrade);
            var cancel = At<Button>(invite, "DeclinePartyInvite");
            At<TMP_Text>(cancel.transform, "Label").text = view.outgoing ? "요청 취소" : "거절";
            Listen(cancel, BackFromTrade);
            var accept = At<Button>(invite, "AcceptPartyInvite");
            accept.gameObject.SetActive(!view.outgoing);
            Listen(accept, () => owner.RequestTrade(TradeAction.Accept, view, 0));
            return;
        }
        if (editingOffer && view.version == draftVersion) { DrawTradeEditor(owner, view); return; }
        editingOffer = false;
        var pane = Spawn("Party/TradeReview", modal);
        At<TMP_Text>(pane, "Title").text = $"{peer} 님과 거래";
        At<TMP_Text>(pane, "MineTitle").text = "내가 주는 물품" + (view.mineReady ? "   · 확정" : "");
        At<TMP_Text>(pane, "TheirTitle").text = "상대가 주는 물품" + (view.peerReady ? "   · 확정" : "");
        At<TMP_Text>(pane, "Mine").text = OfferText(view.mine);
        At<TMP_Text>(pane, "Theirs").text = OfferText(view.theirs);
        At<TMP_Text>(pane, "Status").text = view.committed ? "거래를 처리하고 있습니다."
            : view.mineReady ? "내 제안을 확정했습니다. 상대의 확정을 기다립니다."
            : view.peerReady ? "상대가 확정했습니다. 물품을 확인한 뒤 거래를 확정하세요."
            : "양쪽 모두 확정하면 거래가 완료됩니다.";
        var cancelTrade = At<Button>(pane, "Cancel");
        At<TMP_Text>(cancelTrade.transform, "Label").text = view.committed ? "닫기" : "거래 취소";
        Listen(cancelTrade, BackFromTrade);
        var edit = At<Button>(pane, "Edit");
        edit.interactable = !view.mineReady && !view.committed && snapshot != null;
        Listen(edit, () => BeginOffer(view));
        var confirm = At<Button>(pane, "Confirm");
        At<TMP_Text>(confirm.transform, "Label").text = view.mineReady ? "확정 철회" : "거래 확정";
        confirm.interactable = !view.committed && snapshot != null && !(view.mine.Empty && view.theirs.Empty);
        Listen(confirm, () => owner.RequestTrade(view.mineReady ? TradeAction.Withdraw : TradeAction.Confirm, view, snapshot.revision));
    }

    private static string OfferText(TradeOffer offer)
    {
        if (offer.Empty) return "등록한 물품이 없습니다.";
        var lines = new List<string>();
        if (offer.gold > 0) lines.Add($"골드  {offer.gold:N0}");
        if (offer.ore > 0) lines.Add($"강화석  ×{offer.ore:N0}");
        if (offer.rareOre > 0) lines.Add($"정제 강화석  ×{offer.rareOre:N0}");
        lines.AddRange(offer.gear.Select(g => g.Label));
        return string.Join("\n", lines);
    }

    private void BeginOffer(TradeView view)
    {
        editingOffer = true;
        draftVersion = view.version;
        draftItems.Clear();
        foreach (var g in view.mine.gear) draftItems.Add(g.id);
        draftGold = view.mine.gold.ToString();
        draftOre = view.mine.ore.ToString();
        draftRareOre = view.mine.rareOre.ToString();
        DrawPage();
    }

    private void DrawTradeEditor(PlayerCombat owner, TradeView view)
    {
        if (snapshot == null) return;
        var pane = Spawn("Party/TradeOffer", modal);
        var items = snapshot.items ?? Array.Empty<ExpeditionItem>();
        var equipment = items.Where(i => i.IsGear && !i.equipped && i.sellGold > 0).ToArray();
        var count = At<TMP_Text>(pane, "GearTitle");
        void ShowCount() => count.text = $"사냥 장비 · {draftItems.Count}/4개";
        ShowCount();
        var error = At<TMP_Text>(pane, "Error");
        var list = At<UiRepeatingList>(pane, "Equipment/Viewport/Content");
        var rows = list.Populate(equipment.Length);
        At<TMP_Text>(pane, "Empty").gameObject.SetActive(equipment.Length == 0);
        for (int i = 0; i < equipment.Length; i++)
        {
            var item = equipment[i];
            var row = rows[i];
            At<TMP_Text>(row, "Name").text = $"{item.Rank} {item.name} +{item.enhancement}";
            void Mark()
            {
                bool selected = draftItems.Contains(item.instanceId);
                row.Find("Highlight").gameObject.SetActive(selected);
                At<TMP_Text>(row, "State").text = selected ? "제안에 포함" : "선택하여 추가";
            }
            Mark();
            Listen(row.GetComponent<Button>(), () => {
                if (!draftItems.Remove(item.instanceId))
                {
                    if (draftItems.Count == 4) { error.text = "장비는 한 번에 4개까지 선택할 수 있습니다."; return; }
                    draftItems.Add(item.instanceId);
                }
                error.text = "";
                Mark();
                ShowCount();
            });
        }
        int oreMax = snapshot.Item("stack:ore")?.quantity ?? 0;
        int rareMax = snapshot.Item("stack:rare_ore")?.quantity ?? 0;
        At<TMP_Text>(pane, "GoldTitle").text = $"골드 · 보유 {snapshot.gold:N0}";
        At<TMP_Text>(pane, "OreTitle").text = $"강화석 · 보유 {oreMax:N0}";
        At<TMP_Text>(pane, "RareTitle").text = $"정제 강화석 · 보유 {rareMax:N0}";
        BindAmount("Gold", draftGold, s => draftGold = s);
        BindAmount("Ore", draftOre, s => draftOre = s);
        BindAmount("RareOre", draftRareOre, s => draftRareOre = s);
        Listen(At<Button>(pane, "Back"), BackFromTrade);
        Listen(At<Button>(pane, "Save"), () => {
            if (!Amount(draftGold, snapshot.gold, out int gold) || !Amount(draftOre, oreMax, out int ore)
                || !Amount(draftRareOre, rareMax, out int rare))
            {
                error.text = "보유 수량 이내의 정수를 입력하세요.";
                return;
            }
            owner.RequestTrade(TradeAction.Edit, view, snapshot.revision, draftItems.ToArray(), gold, ore, rare);
        });

        void BindAmount(string name, string value, Action<string> changed)
        {
            var input = At<TMP_InputField>(pane, name);
            input.SetTextWithoutNotify(value);
            input.onValueChanged.AddListener(s => changed(s));
        }
    }

    private static bool Amount(string text, int maximum, out int amount)
    {
        amount = 0;
        return string.IsNullOrEmpty(text) || int.TryParse(text, out amount) && amount >= 0 && amount <= maximum;
    }
}
