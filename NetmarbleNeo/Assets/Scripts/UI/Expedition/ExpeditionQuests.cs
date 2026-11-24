using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static ExpeditionWidgets;

public sealed partial class ExpeditionUI
{
    private void DrawGoalPanel(ExpeditionQuest quest)
    {
        CaptionAt(goalPanel, "Title", quest?.name ?? "의뢰 수첩");
        string directive = quest == null ? (snapshot == null ? "의뢰 목록 확인" : "현재 진행할 의뢰가 없습니다")
            : quest.state == "available" ? QuestNpcLocations.Name(quest.npcId) + "에게 의뢰 받기"
            : quest.state == "ready" ? QuestNpcLocations.Name(quest.reportNpcId) + (quest.type == "collect" ? "에게 전달" : "에게 보고")
            : quest.instruction;
        CaptionAt(goalPanel, "Directive", directive);
        var goals = quest?.Ongoing == true ? quest.goals : null;
        var scroll = UiPrefab.At<ScrollRect>(goalPanel, "Tracked Goals");
        scroll.gameObject.SetActive(goals?.Length > 0);
        BindGoalList(scroll.content, goals);
    }

    private static void BindGoalList(RectTransform content, ExpeditionGoal[] goals)
    {
        int count = goals?.Length ?? 0;
        var entries = content.GetComponent<UiRepeatingList>().Populate(count);
        for (int i = 0; i < count; i++)
        {
            var label = entries[i].GetComponent<TMP_Text>();
            label.text = goals[i].Progress;
            label.color = goals[i].count >= goals[i].required ? Ice : Muted;
        }
    }

    private void DrawQuests()
    {
        bool journal = page == Page.Journal;
        var pane = UiPrefab.Spawn(journal ? "Quests/QuestJournal" : "Quests/NpcDialogue", modal);
        if (!journal)
        {
            CaptionAt(pane, "Title", QuestNpcLocations.Name(conversationNpc));
            bool warden = conversationNpc == "warden";
            var service = ClickAt(pane, "ForgeButton", () =>
            {
                if (warden && CanSell) SetPage(Page.Vendor);
                else if (!warden) OpenForge();
            });
            if (warden) CaptionAt(service.transform, "Label", "장비 판매");
            service.gameObject.SetActive(GameProgressBridge.Ready && (warden ? CanSell : conversationNpc == "smith" && CanUseForge));
        }
        ClickAt(pane, "DialogueClose", () => SetPage(Page.Field));
        if (!BindProgressState(pane)) return;
        var data = pane.Find("Data");
        var quests = (snapshot.quests ?? Array.Empty<ExpeditionQuest>())
            .Where(q => q.state == "available" || q.Ongoing)
            .Where(q => journal || q.npcId == conversationNpc || (q.Ongoing && q.reportNpcId == conversationNpc))
            .OrderBy(q => q.state switch { "ready" => 0, "available" => 1, _ => 2 })
            .ThenBy(q => q.category == "main" ? 0 : 1).ToArray();
        data.Find("Empty").gameObject.SetActive(quests.Length == 0);
        var browser = data.Find("Quest Browser");
        browser.gameObject.SetActive(quests.Length > 0);
        if (quests.Length == 0)
        {
            selectedQuest = null;
            return;
        }

        var selected = quests.FirstOrDefault(q => q.id == selectedQuest) ?? quests[0];
        selectedQuest = selected.id;
        var list = UiPrefab.At<ScrollRect>(browser, "Quest List");
        var entries = list.content.GetComponent<UiRepeatingList>().Populate(quests.Length);
        for (int i = 0; i < quests.Length; i++)
        {
            var quest = quests[i];
            var row = entries[i];
            row.name = "Quest_" + quest.id;
            row.Find("Highlight").gameObject.SetActive(quest.id == selectedQuest);
            UiPrefab.Listen(row.GetComponent<Button>(), () => { selectedQuest = quest.id; DrawPage(); });
            CaptionAt(row, "Name", quest.name);
            CaptionAt(row, "State", $"{(quest.category == "main" ? "메인" : "서브")} 의뢰   ·   {quest.StateText}").color = quest.Ongoing ? Ice : Gold;
        }

        var detail = browser.Find("Detail");
        CaptionAt(detail, "Name", selected.name);
        CaptionAt(detail, "State", $"{selected.StateText}   ·   Lv.{Math.Max(1, selected.minLevel)}   ·   {QuestNpcLocations.Name(selected.npcId)} 의뢰");
        CaptionAt(detail, "Dialogue", selected.dialogue);
        CaptionAt(detail, "Instruction", selected.instruction);
        var progress = UiPrefab.At<ScrollRect>(detail, "Quest Objectives");
        string route = selected.state == "available" ? QuestNpcLocations.Name(selected.npcId) + "에게 수락"
            : selected.completion == "immediate" ? "목표 달성 시 즉시 완료"
            : QuestNpcLocations.Name(selected.reportNpcId) + (selected.type == "collect" ? "에게 전달" : "에게 보고");
        CaptionAt(progress.content, "Route", route);
        BindGoalList(progress.content, selected.goals);
        string reward = string.IsNullOrEmpty(selected.rewardLabel)
            ? $"경험치 {selected.rewardXp:N0}   ·   {selected.rewardGold:N0} 골드" : selected.rewardLabel;
        CaptionAt(detail, "Reward", "보상   " + reward).gameObject.SetActive(
            !string.IsNullOrEmpty(selected.rewardLabel) || selected.rewardXp > 0 || selected.rewardGold > 0);
        var action = UiPrefab.At<Button>(detail, "QuestAction");
        if (journal)
        {
            action.gameObject.SetActive(selected.Ongoing);
            if (!selected.Ongoing) return;
            action.name = "Track_" + selected.id;
            CaptionAt(action.transform, "Label", trackedQuest == selected.id ? "추적 중" : "목표 추적");
            UiPrefab.Listen(action, () => { trackedQuest = selected.id; SetPage(Page.Field); Changed(); });
        }
        else
        {
            bool accept = selected.state == "available" && selected.npcId == conversationNpc;
            bool report = selected.state == "ready" && selected.reportNpcId == conversationNpc;
            action.gameObject.SetActive(accept || report);
            if (!accept && !report) return;
            CaptionAt(action.transform, "Label", accept ? "의뢰 수락" : selected.type == "collect" ? "물품 전달" : "완료 보고");
            UiPrefab.Listen(action, () => SubmitQuest(selected, accept));
        }
    }

    private void SubmitQuest(ExpeditionQuest quest, bool accept)
    {
        if (!InReach(conversationNpc)) { SetPage(Page.Field); return; }
        if (accept) trackedQuest = quest.id;
        GameProgressBridge.Request(accept ? "accept" : "report", quest.id, conversationNpc);
    }

    private bool questOwnsWindow;
    internal ExpeditionSnapshot QuestProgress => snapshot;
    internal bool CanGuideQuest => stage && stage.gameObject.activeInHierarchy && (page == Page.Field || questOwnsWindow);
    internal bool ManualQuestWindow => page != Page.Field && !questOwnsWindow;
    internal bool BossGateButtonReady => gateButton && gateButton.isActiveAndEnabled && gateButton.IsInteractable();

    internal bool CloseQuestWindow()
    {
        if (!questOwnsWindow) return false;
        SetPage(Page.Field);
        return true;
    }

    // Return true once the player has seen the matching window for at least one planning tick.
    internal bool ShowQuestDialogue(ExpeditionQuest quest, string npc)
    {
        if (!InReach(npc)) return false;
        if (!questOwnsWindow || page != Page.Dialogue || conversationNpc != npc)
        {
            conversationNpc = npc;
            SetPage(Page.Dialogue);
            questOwnsWindow = true;
            return false;
        }
        if (selectedQuest == quest.id) return true;
        selectedQuest = quest.id;
        DrawPage();
        return false;
    }

    internal void SubmitVisibleQuest(ExpeditionQuest quest) => SubmitQuest(quest, quest.state == "available");

    internal bool ShowQuestInventory()
    {
        if (questOwnsWindow && page == Page.Inventory) return true;
        if (CloseQuestWindow()) return false;
        OpenInventory();
        questOwnsWindow = page == Page.Inventory;
        return false;
    }
}
