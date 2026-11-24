using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class ExpeditionUI
{
    private RectTransform autoQuestControls, questFieldDock, questInventoryDock;
    private Button autoQuestButton;
    private TMP_Text autoQuestCaption, autoQuestStatus;

    private void BuildAutoQuestControls()
    {
        autoQuestControls = UiPrefab.Spawn("Quests/AutoQuestControls", stage);
        questFieldDock = UiPrefab.At<RectTransform>(autoQuestControls, "FieldDock");
        questInventoryDock = UiPrefab.At<RectTransform>(autoQuestControls, "InventoryDock");
        autoQuestButton = UiPrefab.At<Button>(questFieldDock, "AutoQuestButton");
        autoQuestCaption = UiPrefab.At<TMP_Text>(autoQuestButton.transform, "Label");
        autoQuestStatus = UiPrefab.At<TMP_Text>(autoQuestButton.transform, "Status");
        UiPrefab.Listen(autoQuestButton, QuestAutoProgress.Toggle);
        UpdateAutoQuestControls();
    }

    private void UpdateAutoQuestControls()
    {
        bool active = QuestAutoProgress.Active;
        bool inQuestWindow = active && questOwnsWindow;
        bool visible = (page == Page.Field || inQuestWindow) && (QuestAutoProgress.Available || active);
        autoQuestControls.gameObject.SetActive(visible);
        if (!visible) return;
        Transform dock = inQuestWindow ? questInventoryDock : questFieldDock;
        if (autoQuestButton.transform.parent != dock) autoQuestButton.transform.SetParent(dock, false);
        autoQuestCaption.text = active ? "자동 진행 중지" : "자동 진행";
        string status = QuestAutoProgress.Status;
        autoQuestStatus.text = string.IsNullOrEmpty(status) ? "꺼짐" : status;
    }
}
