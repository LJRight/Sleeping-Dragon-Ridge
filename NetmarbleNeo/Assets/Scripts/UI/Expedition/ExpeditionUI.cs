using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed partial class ExpeditionUI : MonoBehaviour
{
    private enum Page { Field, Trade, Inventory, Forge, Dialogue, Journal, BossEntry, Party, InvitePlayer, Victory, Vendor }
    private Page page;
    private RectTransform stage, hud, modal, modalWindow;
    private TextMeshProUGUI heroLabel, healthLabel, talkLabel, statusLabel, expLabel;
    private HealthGauge heroGauge;
    private NetworkHealth ownHealth;
    private PlayerCombat ownCombat;
    private RectTransform heroPanel;
    private RectTransform goalPanel;
    private bool pendingBagEvent;
    private Button talkButton, mapGateButton, villageRecallButton;
    private TextMeshProUGUI mapGateLabel;
    private string nearNpc, conversationNpc, selectedQuest, trackedQuest, selectedItem, lastJson;
    private bool byType = true;
    private ScrollRect bagScroll;
    private float scrollPosition = 1, tick;
    private ExpeditionSnapshot snapshot, receivedSnapshot;
    private readonly List<Transform> villagers = new();
    private readonly Transform[] villageSigns = new Transform[QuestNpcLocations.Ids.Length];
    private static ExpeditionUI current;
    private bool UsesWorldMaps => gameObject.scene.name == WorldSceneCoordinator.SessionName;
    public static bool BlocksGameplay => WorldSceneCoordinator.IsLoading || (current && current.page != Page.Field);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        current = null;
        SceneManager.sceneLoaded -= OnScene;
        SceneManager.sceneLoaded += OnScene;
    }

    private static void OnScene(Scene scene, LoadSceneMode mode)
    {
        if ((scene.name == WorldSceneCoordinator.SessionName || scene.name == "Prototype") && !current)
        {
            var go = new GameObject("Expedition Interface");
            SceneManager.MoveGameObjectToScene(go,scene);
            current = go.AddComponent<ExpeditionUI>();
            if (scene.name == WorldSceneCoordinator.SessionName) go.AddComponent<QuestAutoProgress>();
        }
    }

    private void Update()
    {
        var manager = NetworkManager.Singleton;
        bool connected = manager && manager.IsConnectedClient && manager.LocalClient?.PlayerObject;
        if (!connected)
        {
            if (forgeSounds && forgeSounds.Busy) forgeSounds.Clear();
            if (stage && stage.gameObject.activeSelf)
            {
                stage.gameObject.SetActive(false);
                SetPage(Page.Field);
            }
            return;
        }
        if (WorldSceneCoordinator.IsLoading)
        {
            if (stage && stage.gameObject.activeSelf) stage.gameObject.SetActive(false);
            trackingFieldTap = false;
            return;
        }
        if (!stage) Build();
        if (!stage.gameObject.activeSelf)
        {
            stage.gameObject.SetActive(true);
            tick = 0;
        }
        var player = manager.LocalClient.PlayerObject;
        if (!ownHealth || ownHealth.NetworkObject != player)
        {
            ownHealth = player.GetComponent<NetworkHealth>();
            ownCombat = player.GetComponent<PlayerCombat>();
        }
        if (ownCombat && ownCombat.VictoryRoom > 0 && ownCombat.VictoryRoom == ownCombat.BossRoomId)
        {
            if (page != Page.Victory)
            {
                SetPage(Page.Victory);
                deathNotice.gameObject.SetActive(false);
                spectatorPanel.gameObject.SetActive(false);
                UpdateAutoQuestControls();
            }
            return;
        }
        if (page == Page.Victory) SetPage(Page.Field);
        ReadFieldTap(player);
        if (page != Page.Field && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            GameplayUiAudio.PlayClick();
            Back();
        }
        if (ownHealth)
        {
            // The replicated maximum HP can change before the forge receipt arrives.
            bool holdHealth = enhancingEquippedItem && forgeSounds && forgeSounds.Busy
                && ownCombat && ownCombat.MapSceneName == SceneWorld.Village;
            if (!holdHealth) heroGauge.Render(DisplayedHealth.For(ownHealth));
            UpdateEnemyHealth(ownHealth.CombatRoom, ownCombat ? ownCombat.HudTarget : null);
        }
        UpdateAutoQuestControls();
        if (Time.unscaledTime < tick) return;
        tick = Time.unscaledTime + .12f;
        UpdateField(player);
    }

    private void Build()
    {
        var canvas = UiPrefab.Spawn("HUD/ExpeditionHUD", transform);
        forgeSounds = canvas.GetComponent<ForgeSounds>();
        stage = UiPrefab.At<RectTransform>(canvas, "Safe Area/Safe Stage");
        hud = UiPrefab.At<RectTransform>(stage, "Field");
        heroPanel = UiPrefab.At<RectTransform>(stage, "Knight Status");
        heroLabel = UiPrefab.At<TextMeshProUGUI>(heroPanel, "Name");
        healthLabel = UiPrefab.At<TextMeshProUGUI>(heroPanel, "Health");
        expLabel = UiPrefab.At<TextMeshProUGUI>(heroPanel, "Experience");
        heroGauge = new HealthGauge(UiPrefab.At<RectTransform>(heroPanel, "Player Health Bar"), healthLabel);
        goalPanel = UiPrefab.At<RectTransform>(hud, "TrackedObjective");
        UiPrefab.Listen(goalPanel.GetComponent<Button>(), () => SetPage(Page.Journal));
        UiPrefab.Listen(UiPrefab.At<Button>(hud, "OpenInventory"), OpenInventory);
        talkButton = UiPrefab.At<Button>(hud, "OpenDialogue");
        UiPrefab.Listen(talkButton, OpenDialogue);
        talkLabel = UiPrefab.At<TextMeshProUGUI>(talkButton.transform, "Label");
        mapGateButton = UiPrefab.At<Button>(hud, "MoveBetweenMaps");
        UiPrefab.Listen(mapGateButton, UseMapGate);
        mapGateLabel = UiPrefab.At<TextMeshProUGUI>(mapGateButton.transform, "Label");
        mapGateButton.gameObject.SetActive(false);
        villageRecallButton = UiPrefab.At<Button>(hud, "RecallToVillage");
        UiPrefab.Listen(villageRecallButton, RecallToVillage);
        villageRecallButton.gameObject.SetActive(false);
        statusLabel = UiPrefab.At<TextMeshProUGUI>(stage, "Status");
        BuildBossControls();
        BuildPartyControls();
        modalWindow = UiPrefab.At<RectTransform>(stage, "Window");
        modal = UiPrefab.At<RectTransform>(modalWindow, "Page Content");
        modalWindow.SetAsLastSibling();
        modalWindow.gameObject.SetActive(false);
        BuildAutoQuestControls();
        GameProgressBridge.Changed += Changed;
        ApplyControlArtwork();
        if (!UsesWorldMaps) SpawnVillagers();
        Changed();
    }

    private static void ApplyControlArtwork()
    {
        var combatHud = FindFirstObjectByType<PrototypeCombatHud>();
        if (combatHud) combatHud.HideDebugHealthText();
        var stick = FindFirstObjectByType<Joystick>();
        if (!stick) return;
        foreach(var img in stick.GetComponentsInChildren<Image>(true))
        {
            var name = img.gameObject.name.ToLowerInvariant();
            if (!name.Contains("background") && !name.Contains("handle")) continue;
            img.sprite = KnightUIArt.Find(name.Contains("handle")?"joystick_thumb":"joystick_base");
            img.type = Image.Type.Simple; img.preserveAspect = true; img.color=Color.white;
        }
    }

    private void SpawnVillagers()
    {
        foreach (var id in QuestNpcLocations.Ids)
        {
            QuestNpcLocations.TryPosition(id,out var p);
            var actor = new GameObject("NPC_"+id);
            actor.transform.SetParent(transform,false);
            actor.transform.position = new Vector3(p.x,.97f,p.z);
            var sr = actor.AddComponent<SpriteRenderer>();
            sr.sprite = KnightUIArt.Find("knight_base");
            sr.color = id == "smith" ? new Color(1f,.80f,.60f) : Color.white;
            if (sr.sprite) actor.transform.localScale = Vector3.one * (2.1f/sr.sprite.bounds.size.y);
            SpawnNameplate("HUD/PrototypeNameplate", actor.transform, id);
            villagers.Add(actor.transform);
        }
    }

    private void UpdateField(NetworkObject player)
    {
        var combat = player.GetComponent<PlayerCombat>();
        bool inVillage = combat && (UsesWorldMaps ? combat.MapSceneName == SceneWorld.Village : !combat.InBossArena);
        var cam=Camera.main;
        if(cam) foreach (var v in villagers) v.rotation = cam.transform.rotation;
        nearNpc = null;
        if (inVillage)
        {
            float shortest = QuestNpcLocations.Radius * QuestNpcLocations.Radius;
            for (int i = 0; i < QuestNpcLocations.Ids.Length; i++)
            {
                string id = QuestNpcLocations.Ids[i];
                if (!QuestNpcLocations.TryPosition(id, out var p)) continue;
                float d = (p-player.transform.position).sqrMagnitude;
                if (d <= shortest) { nearNpc=id; shortest=d; }
                if (cam && UsesWorldMaps) UpdateVillageSign(i, cam);
            }
        }
        if(page==Page.Dialogue && !InReach(conversationNpc)) SetPage(Page.Field);
        if(page==Page.Forge && !CanUseForge) SetPage(Page.Field);
        if(page==Page.Vendor && !CanSell) SetPage(Page.Field);
        talkButton.gameObject.SetActive(nearNpc != null);
        if (nearNpc!=null) talkLabel.text=QuestNpcLocations.Name(nearNpc)+" · 대화";
        int level = snapshot?.level ?? 1;
        heroLabel.text=$"Lv. {level}  원정 기사";
        bool atMapGate = UsesWorldMaps && combat && GameProgressBridge.Ready && combat.CanUseMapGate;
        mapGateButton.gameObject.SetActive(atMapGate);
        if (atMapGate) mapGateLabel.text = inVillage ? "사냥터로 이동" : "마을로 이동";
        villageRecallButton.gameObject.SetActive(UsesWorldMaps && combat && GameProgressBridge.Ready
            && combat.CanRecallToVillage && !combat.PartyState.preparing);
        UpdateBossControls(combat, ownHealth);
        UpdatePartyControls(combat, ownHealth);
        UpdateTradeControls(combat);
        expLabel.text=GameProgressBridge.Ready ? $"경험치   {snapshot?.experience ?? 0:0.#}%" : "진행 불러오는 중";
    }

    private void UpdateVillageSign(int index, Camera cam)
    {
        var sign = villageSigns[index];
        if (!sign)
        {
            string id = QuestNpcLocations.Ids[index];
            var actor = QuestNpcLocations.FindVillageActor(id);
            if (!actor) return;
            sign = villageSigns[index] = SpawnNameplate("HUD/VillageNameplate", actor, id);
        }
        sign.rotation = cam.transform.rotation;
    }

    private static Transform SpawnNameplate(string asset, Transform actor, string id)
    {
        var sign = UiPrefab.Spawn(asset, actor);
        var label = sign.GetComponent<TextMeshPro>();
        label.text = QuestNpcLocations.Name(id);
        label.outlineColor = Color.black;
        label.outlineWidth = .18f;
        return sign;
    }

    private void UseMapGate()
    {
        var owner = LocalCombat;
        if (BlocksGameplay || !owner || !owner.CanUseMapGate) return;
        owner.RequestMapGate();
    }

    private void RecallToVillage()
    {
        var owner = LocalCombat;
        if (BlocksGameplay || !owner || !owner.CanRecallToVillage || owner.PartyState.preparing) return;
        if (QuestAutoProgress.Active) QuestAutoProgress.Toggle();
        owner.RequestVillageRecall();
    }

    private bool InReach(string id)
    {
        var owner=NetworkManager.Singleton?.LocalClient?.PlayerObject;
        return owner && owner.TryGetComponent(out PlayerCombat combat) && !combat.InBossArena
            && (!UsesWorldMaps || combat.MapSceneName == SceneWorld.Village)
            && QuestNpcLocations.TryPosition(id,out var p)
            && (owner.transform.position-p).sqrMagnitude<=QuestNpcLocations.Radius*QuestNpcLocations.Radius;
    }

    private void Changed()
    {
        if (!stage) return;
        var raw=GameProgressBridge.SnapshotJson;
        if(string.IsNullOrEmpty(raw) && lastJson!=null)
        {
            if (forgeSounds && forgeSounds.Busy) forgeSounds.Clear();
            receivedSnapshot=null;lastJson=null;selectedItem=null;selectedQuest=null;trackedQuest=null;
            lastEnhanceAction = null;
            scrollPosition=1;
        }
        if (!string.IsNullOrEmpty(raw) && raw!=lastJson)
        {
            try
            {
                var fresh=JsonUtility.FromJson<ExpeditionSnapshot>(raw);
                if (fresh!=null)
                {
                    receivedSnapshot=fresh; lastJson=raw;
                }
            }
            catch(ArgumentException e){ Debug.LogWarning("진행 화면 JSON 오류: "+e.Message); }
        }
        var result = receivedSnapshot?.enhanceResult;
        if (forgeSounds && forgeSounds.Busy)
        {
            statusLabel.text = string.IsNullOrEmpty(GameProgressBridge.Status)
                ? "장비를 강화하고 있습니다…" : GameProgressBridge.Status;
            if (result?.instanceId == forgeSounds.PendingItem) ShowEnhanceResult(result);
            return;
        }
        if (!ShowEnhanceResult(result)) PresentProgress(GameProgressBridge.Status);
    }

    private void PresentProgress(string status)
    {
        if (questOwnsWindow && receivedSnapshot?.revision != snapshot?.revision) QuestAutoProgress.HoldCurrentScreen();
        snapshot = receivedSnapshot;
        SelectTracked();
        statusLabel.text = status;
        statusLabel.transform.SetAsLastSibling();
        DrawGoalPanel(snapshot?.Quest(trackedQuest));
        if(pendingBagEvent && page==Page.Inventory && GameProgressBridge.Ready)
        {
            pendingBagEvent=false;
            GameProgressBridge.Request("tutorial","open_inventory");
        }
        if(page!=Page.Field) DrawPage();
    }

    private void SelectTracked()
    {
        var quests=snapshot?.quests;
        if (quests==null) return;
        if(snapshot.Quest(trackedQuest)?.Ongoing==true) return;
        var fallback=quests.FirstOrDefault(q=>q.Ongoing && q.category=="main")
            ?? quests.FirstOrDefault(q=>q.Ongoing) ?? quests.FirstOrDefault(q=>q.state=="available");
        trackedQuest=fallback?.id;
    }

    private void SetPage(Page next)
    {
        questOwnsWindow = false;
        confirmingEnhance = null;
        page=next; pendingBagEvent=false; selectedItem=null; selectedQuest=null; bagScroll=null; scrollPosition=1;
        trackingFieldTap = false;
        if (!stage) return;
        modalWindow.gameObject.SetActive(next!=Page.Field); hud.gameObject.SetActive(next==Page.Field);
        heroPanel.gameObject.SetActive(next==Page.Field);
        bossStatus.gameObject.SetActive(next==Page.Field);
        DrawPage();
    }

    private void DrawPage()
    {
        if(bagScroll) scrollPosition=bagScroll.verticalNormalizedPosition;
        bagScroll=null;
        for(int i=modal.childCount-1;i>=0;i--)
        {
            var o=modal.GetChild(i).gameObject;
            o.SetActive(false); Destroy(o);
        }
        if (page==Page.BossEntry) DrawBossEntry();
        else if (page==Page.Trade) DrawTrade();
        else if (page==Page.Party) DrawParty();
        else if (page==Page.InvitePlayer) DrawInvitePlayer();
        else if (page==Page.Victory) DrawVictory();
        else if (page==Page.Inventory || page==Page.Forge || page==Page.Vendor) DrawInventory();
        else if(page==Page.Dialogue || page==Page.Journal) DrawQuests();
    }

    public void OpenInventory()
    {
        if(page!=Page.Field) return;
        SetPage(Page.Inventory);
        if(!GameProgressBridge.Ready) { pendingBagEvent=true;GameProgressBridge.Refresh(); }
        else GameProgressBridge.Request("tutorial","open_inventory");
    }

    private void OpenDialogue()
    {
        if (page!=Page.Field || !InReach(nearNpc)) return;
        conversationNpc=nearNpc;
        SetPage(Page.Dialogue);
        if(!GameProgressBridge.Ready) GameProgressBridge.Refresh();
    }

    private void Back()
    {
        if (page == Page.Trade) { BackFromTrade(); return; }
        if (confirmingEnhance != null) { CancelForgeConfirmation(); return; }
        if(selectedItem!=null){ selectedItem=null;DrawPage(); }
        else if(page!=Page.Field)SetPage(Page.Field);
    }

    private void OnDestroy()
    {
        GameProgressBridge.Changed -= Changed;
        if(current==this)current=null;
    }
}
