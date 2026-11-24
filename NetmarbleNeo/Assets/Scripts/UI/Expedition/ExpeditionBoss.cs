using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static UiPrefab;

public sealed partial class ExpeditionUI
{
    private Button gateButton, townBossButton, leaveArenaButton;
    private TextMeshProUGUI bossRoute;
    private HealthGauge bossGauge;
    private NetworkHealth displayedBoss;
    private RectTransform bossStatus, deathNotice, spectatorPanel;
    private TextMeshProUGUI spectatorName;
    private Button nextSpectatorButton;
    private FollowCamera spectatorCamera;
    private NetworkHealth labelledSpectatorTarget;
    private bool entryFromTown;

    private bool BossObjective => snapshot != null && (snapshot.objective == "boss" || snapshot.objective == "complete");

    private void BuildBossControls()
    {
        var actions = At<RectTransform>(hud, "Boss Actions");
        townBossButton = At<Button>(actions, "TownBossMenu");
        gateButton = At<Button>(actions, "OpenBossGate");
        leaveArenaButton = At<Button>(actions, "LeaveBossArena");
        Listen(townBossButton, () => OpenBossEntry(true));
        Listen(gateButton, () => OpenBossEntry(false));
        Listen(leaveArenaButton, () => GameProgressBridge.Request("leaveBoss"));
        bossStatus = At<RectTransform>(stage, "Boss Status");
        bossRoute = At<TextMeshProUGUI>(bossStatus, "BossRoute");
        bossRoute.text = "";
        bossGauge = new HealthGauge(At<RectTransform>(bossStatus, "Boss Health Bar"), bossRoute);
        bossGauge.Track.gameObject.SetActive(false);
        townBossButton.gameObject.SetActive(false);
        gateButton.gameObject.SetActive(false);
        leaveArenaButton.gameObject.SetActive(false);

        deathNotice = At<RectTransform>(stage, "Respawn");
        deathNotice.gameObject.SetActive(false);

        spectatorPanel = At<RectTransform>(stage, "Spectator");
        spectatorName = At<TextMeshProUGUI>(spectatorPanel, "SpectatorTargetName");
        nextSpectatorButton = At<Button>(spectatorPanel, "NextSpectatorTarget");
        Listen(nextSpectatorButton, () => { if (spectatorCamera) spectatorCamera.NextSpectatorTarget(); });
        spectatorPanel.gameObject.SetActive(false);
    }

    private void UpdateBossControls(PlayerCombat player, NetworkHealth hp)
    {
        if (!player || !hp) return;
        bool dead = hp.IsDead;
        bool watching = player.IsSpectatingBoss;
        if ((dead || watching) && page != Page.Field) SetPage(Page.Field);
        deathNotice.gameObject.SetActive(dead && !watching);
        spectatorPanel.gameObject.SetActive(watching);
        if (page == Page.Field)
        {
            hud.gameObject.SetActive(!dead && !watching);
            heroPanel.gameObject.SetActive(!watching);
            bossStatus.gameObject.SetActive(!dead || watching);
        }
        if (watching)
        {
            if (!spectatorCamera && Camera.main) spectatorCamera = Camera.main.GetComponent<FollowCamera>();
            var subject = spectatorCamera ? spectatorCamera.WatchedPlayer : null;
            if (!ReferenceEquals(subject, labelledSpectatorTarget))
            {
                labelledSpectatorTarget = subject;
                spectatorName.text = subject ? KnightName(subject.OwnerClientId) + " 관전 중" : "생존 파티원 확인 중";
            }
            nextSpectatorButton.interactable = spectatorCamera && spectatorCamera.SpectatorTargetCount > 1;
            return;
        }
        if (dead) return;

        Vector3 position = player.transform.position;
        bool inside = player.InBossArena;
        bool inField = !inside && (!UsesWorldMaps || player.MapSceneName == SceneWorld.Field);
        bool atGate = inField && BossArena.AtGate(position);
        bool inTown = !inside && (!UsesWorldMaps || player.MapSceneName == SceneWorld.Village) && BossArena.InTown(position);
        bool canUseMenu = inTown && BossObjective && snapshot.bossDiscovered;
        if (page == Page.BossEntry && (entryFromTown ? !canUseMenu : !atGate || !BossObjective))
            SetPage(Page.Field);

        townBossButton.gameObject.SetActive(canUseMenu);
        gateButton.gameObject.SetActive(atGate && BossObjective);
        leaveArenaButton.gameObject.SetActive(inside);
        if (inside || bossGauge.Track.gameObject.activeSelf) return;
        if (BossObjective && !snapshot.bossDiscovered)
        {
            if (atGate) bossRoute.text = "능선 관문";
            else if (inField)
            {
                int distance = Mathf.RoundToInt(Vector3.Distance(position, BossArena.Gate));
                bossRoute.text = $"첫 도전 · 사냥터 끝 능선 관문까지 {distance}m";
            }
            else bossRoute.text = "첫 도전 · 마을 북문을 지나 사냥터 끝 능선 관문으로";
        }
        else bossRoute.text = atGate && snapshot != null && !BossObjective
            ? "‘관문 앞의 엄니’를 보고하고 Lv.20에 도달하면 관문이 열립니다." : "";
    }

    private void UpdateEnemyHealth(int room, NetworkHealth selected)
    {
        if (!displayedBoss || !displayedBoss.IsActive || displayedBoss.CombatRoom != room || room == 0)
        {
            displayedBoss = null;
            if (room != 0)
                foreach (var actor in NetworkHealth.Spawned)
                    if (actor && actor.IsActive && actor.IsArenaBoss && actor.CombatRoom == room)
                    { displayedBoss = actor; break; }
        }
        var target = selected ? selected : displayedBoss;
        bool visible = target;
        if (bossGauge.Track.gameObject.activeSelf != visible)
        {
            bossGauge.Track.gameObject.SetActive(visible);
            bossGauge.ResetLabel();
            if (!visible) bossRoute.text = "";
        }
        if (visible) bossGauge.Render(DisplayedHealth.For(target), target.MonsterDisplayName);
        else if (room != 0) bossRoute.text = "";
    }

    private void OpenBossEntry(bool fromTown)
    {
        if (page != Page.Field) return;
        entryFromTown = fromTown;
        SetPage(Page.BossEntry);
    }

    private void DrawBossEntry()
    {
        var owner = LocalCombat;
        if (!owner) return;
        var party = owner.PartyState;
        bool grouped = party.members?.Length > 0;
        bool leader = !grouped || party.leader == owner.OwnerClientId;
        bool swordEquipped = snapshot?.Worn("sword")?.definitionId == "ridge_sword";
        var pane = Spawn("Boss/Entry", modal);
        At<TextMeshProUGUI>(pane, "Title").text = entryFromTown ? "마을 · 보스 재도전" : "능선 관문";
        string where = entryFromTown ? "마을" : "관문";
        string text = swordEquipped ? "종결 검을 해제해야 보스방에 입장할 수 있습니다."
            : !leader ? "파티장이 도전을 시작하면 입장 요청을 수락할 수 있습니다."
            : grouped ? $"파티원 모두 {where}에 모이세요. 전원이 수락하면 함께 보스방에 입장합니다."
            : $"{where}에서 혼자 보스방에 입장합니다.";
        if (!entryFromTown && snapshot != null && !snapshot.bossDiscovered && leader && !swordEquipped)
            text += "\n첫 입장 후에는 마을에서도 재도전할 수 있습니다.";
        At<TextMeshProUGUI>(pane, "Description").text = text;
        Listen(At<Button>(pane, "CancelBossEntry"), () => SetPage(Page.Field));
        var confirm = At<Button>(pane, "ConfirmBossEntry");
        At<TextMeshProUGUI>(confirm.transform, "Label").text = grouped ? "입장 요청" : entryFromTown ? "재도전" : "도전";
        Listen(confirm, () =>
        {
            GameProgressBridge.Request("enterBoss", entryFromTown ? "town" : "gate");
            SetPage(Page.Field);
        });
        confirm.interactable = leader && !swordEquipped && !party.preparing;
    }

    private void DrawVictory()
    {
        var pane = Spawn("Boss/Victory", modal);
        var description = At<TextMeshProUGUI>(pane, "Description");
        description.text = $"{BossPartyServer.VictoryReturnSeconds}초 후 {description.text}";
        if (snapshot?.Quest("ridge_boss")?.state == "ready")
            description.text += "\n경비대장에게 의뢰를 보고하고 보상을 받으세요.";
    }
}
