using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

// The owner plans from acknowledged account snapshots. The server moves, fights, and validates NPC requests.
public sealed class QuestAutoProgress : MonoBehaviour
{
    private const string BossQuest = "ridge_boss";
    private const float StepInterval = 1f;
    private static QuestAutoProgress current;
    private ExpeditionUI ui;
    private PlayerCombat owner;
    private bool running;
    private bool waitingForView;
    private string message = "";
    private string destination, requestKey;
    private uint manualRevision, destinationEpoch;
    private float nextStep, nextTravelRequest, nextActionRequest;

    public static bool Active => current && current.running;
    public static string Status => current ? current.message : "";
    public static bool Available => Active || current && current.CanStart;

    private PlayerCombat LocalOwner
    {
        get
        {
            var manager = NetworkManager.Singleton;
            return manager && manager.IsConnectedClient && manager.LocalClient.PlayerObject
                ? manager.LocalClient.PlayerObject.GetComponent<PlayerCombat>() : null;
        }
    }

    private bool CanStart => ui && ui.CanGuideQuest && GameProgressBridge.Ready && ui.QuestProgress?.quests != null
        && LocalOwner is { } player && !player.InBossArena && !player.GetComponent<NetworkHealth>().IsDead;

    private void Awake()
    {
        ui = GetComponent<ExpeditionUI>();
        current = this;
    }

    public static void Toggle()
    {
        if (!current) return;
        if (current.running) current.Stop("");
        else if (current.CanStart) current.Begin();
    }

    private void Begin()
    {
        owner = LocalOwner;
        manualRevision = owner.ManualMovementRevision;
        destination = requestKey = null;
        waitingForView = false;
        nextStep = Time.unscaledTime + StepInterval;
        message = "다음 의뢰 확인 중";
        running = true;
    }

    private void Stop(string reason)
    {
        running = false;
        message = reason;
        if (owner && owner.IsSpawned) owner.StopQuestTravel();
        ui.CloseQuestWindow();
        destination = requestKey = null;
        owner = null;
    }

    internal static void HoldCurrentScreen()
    {
        if (Active)
            current.nextStep = Mathf.Max(current.nextStep, Time.unscaledTime + StepInterval);
    }

    private void Update()
    {
        if (!running) return;
        if (!owner || !owner.IsSpawned || !owner.IsOwner || !GameProgressBridge.Ready || owner.InBossArena
            || owner.ManualMovementRevision != manualRevision)
        {
            Stop("");
            return;
        }
        if (owner.GetComponent<NetworkHealth>().IsDead)
        {
            message = "마을 부활 대기 중";
            destination = null;
            waitingForView = true;
            return;
        }
        if (WorldSceneCoordinator.IsLoading || !ui.CanGuideQuest)
        {
            waitingForView = true;
            if (!WorldSceneCoordinator.IsLoading && ui.ManualQuestWindow) Stop("");
            return;
        }
        if (waitingForView)
        {
            waitingForView = false;
            HoldCurrentScreen();
        }
        if (ui.QuestForgeBusy || Time.unscaledTime < nextStep) return;
        nextStep = Time.unscaledTime + StepInterval;
        var data = ui.QuestProgress;
        if (data?.quests == null) return;
        var quest = NextQuest(data);
        if (quest == null)
        {
            Stop("진행할 의뢰가 없습니다");
            return;
        }
        if (quest.state == "locked")
        {
            var previousHunt = data.quests.Where(q => q.state == "completed" && q.type == "kill" && q.id != BossQuest)
                .OrderByDescending(q => q.minLevel).FirstOrDefault();
            if (quest.minLevel > data.level) Hunt(previousHunt?.target ?? "A1", TrainingLabel(quest));
            else Stop("먼저 진행 중인 의뢰를 완료하세요");
            return;
        }
        if (quest.state == "available") { Visit(quest, quest.npcId); return; }
        if (quest.id == BossQuest)
        {
            if (quest.state != "active")
            {
                Stop(quest.state == "ready" ? "보스 처치 완료 · 경비대장에게 보고" : "보스 의뢰 완료");
                return;
            }
            if (owner.AreaId == SceneWorld.FieldArea && ui.BossGateButtonReady && BossArena.AtGate(owner.transform.position))
                Stop("보스 도전 준비 완료");
            else Go(owner.AreaId == SceneWorld.FieldArea ? "boss_gate" : "field", "최종 보스 관문으로 이동");
            return;
        }
        if (quest.state == "ready")
        {
            // Finish the level gap on this trip, including the XP the pending report will award.
            var next = data.quests.FirstOrDefault(q => q.state == "locked" && q.requires != null
                && Array.IndexOf(q.requires, quest.id) >= 0);
            if (quest.type == "kill" && owner.AreaId == SceneWorld.FieldArea && next != null
                && next.minLevel > data.level && data.levelXpThresholds != null
                && next.minLevel <= data.levelXpThresholds.Length
                && data.totalXp + quest.rewardXp < data.levelXpThresholds[next.minLevel - 1])
            {
                Hunt(quest.target, TrainingLabel(next));
                return;
            }
            Visit(quest, quest.reportNpcId);
            return;
        }
        if (quest.type == "kill") Hunt(quest.target, $"{quest.name} · {quest.count}/{quest.required}");
        else if (quest.type == "tutorial") DoTutorial(data, quest);
        else
        {
            var goal = quest.goals?.FirstOrDefault(g => g.count < g.required && g.dropFrom is { Length: > 0 });
            if (goal == null) Stop("의뢰 수첩에서 필요한 물품을 확인하세요");
            else Hunt(goal.dropFrom[0], $"{goal.Caption} · {goal.count}/{goal.required}");
        }
    }

    private static ExpeditionQuest NextQuest(ExpeditionSnapshot data)
    {
        var boss = data.Quest(BossQuest);
        // Old accounts may already have the gate from the original A1 report.
        bool legacyGate = (data.objective == "boss" || data.objective == "complete")
            && data.Quest("hunt_boar")?.state != "completed";
        if (boss != null && (legacyGate || boss.Ongoing || boss.state == "completed")) return boss;
        // Complete the village errands before leaving for the first hunt.
        var remaining = data.quests.Where(q => q.id != BossQuest && q.state != "completed");
        var open = remaining.Where(q => q.state != "locked");
        var errand = open.FirstOrDefault(q => q.type != "kill");
        if (errand?.type == "collect" && errand.Ongoing)
        {
            var source = errand.goals?.FirstOrDefault(g => g.dropFrom is { Length: > 0 });
            if (source != null)
            {
                // Accept the matching hunt before setting out. Once the items are ready,
                // finish the hunt and its level training on the same trip.
                var hunt = open.FirstOrDefault(q => q.category == "main" && q.type == "kill"
                    && Array.IndexOf(source.dropFrom, q.target) >= 0);
                if (hunt != null && (hunt.state == "available" || errand.state == "ready")) return hunt;
            }
        }
        return errand ?? open.FirstOrDefault()
            ?? remaining.FirstOrDefault(q => q.requires == null || q.requires.All(id => data.Quest(id)?.state == "completed"))
            ?? boss;
    }

    private static string TrainingLabel(ExpeditionQuest next) => $"다음 의뢰 준비 · Lv.{next.minLevel}까지 사냥";

    private void Hunt(string zone, string label)
    {
        Go(owner.AreaId == SceneWorld.FieldArea ? zone : "field", label);
    }

    private void Go(string key, string label)
    {
        if (ui.CloseQuestWindow()) return;
        message = label;
        if (destination == key && destinationEpoch == owner.TravelEpoch && Time.unscaledTime < nextTravelRequest) return;
        destination = key;
        destinationEpoch = owner.TravelEpoch;
        nextTravelRequest = Time.unscaledTime + 2f;
        owner.RequestQuestTravel(key);
    }

    private void HaltAtInteraction()
    {
        if (destination == null) return;
        owner.StopQuestTravel();
        destination = null;
    }

    private bool ReachNpc(string npc, string label)
    {
        if (owner.AreaId != 0) { Go("village", label); return false; }
        if (!QuestNpcLocations.TryPosition(npc, out var position)
            || Vector3.Distance(owner.transform.position, position) > QuestNpcLocations.Radius - .2f)
        {
            Go(npc, label);
            return false;
        }
        if (destination != null)
        {
            HaltAtInteraction();
            return false;
        }
        message = label;
        return true;
    }

    private void Visit(ExpeditionQuest quest, string npc)
    {
        bool accept = quest.state == "available";
        string label = QuestNpcLocations.Name(npc) + (accept ? " · 의뢰 수락" : quest.type == "collect" ? " · 물품 전달" : " · 완료 보고");
        if (!ReachNpc(npc, label)) return;
        if (ui.ShowQuestDialogue(quest, npc) && CanSend(quest.id + quest.state)) ui.SubmitVisibleQuest(quest);
    }

    private void DoTutorial(ExpeditionSnapshot data, ExpeditionQuest quest)
    {
        if (quest.target == "temper_starter_sword")
        {
            TemperStarterSword(data, quest);
            return;
        }
        HaltAtInteraction();
        message = quest.instruction;
        if (!ui.ShowQuestInventory()) return;
        if (quest.target == "open_inventory")
        {
            // Opening the actual inventory emits the normal tutorial event. Retry on a missed acknowledgment.
            if (requestKey != quest.id) CanSend(quest.id);
            else if (CanSend(quest.id)) GameProgressBridge.Request("tutorial", "open_inventory");
        }
        else if (quest.target == "equip_sword" || quest.target == "equip")
        {
            var sword = data.Worn("sword") ?? data.Item(data.starterSwordInstanceId)
                ?? data.items?.FirstOrDefault(i => i.definitionId == "balance_sword")
                ?? data.items?.FirstOrDefault(i => i.slot == "sword" && i.IsGear);
            if (sword == null) { Stop("장착할 검을 확인하세요"); return; }
            if (CanSend(quest.id)) GameProgressBridge.Request("equip", sword.instanceId);
        }
        else Stop("의뢰 수첩에서 수행 조건을 확인하세요");
    }

    private void TemperStarterSword(ExpeditionSnapshot data, ExpeditionQuest quest)
    {
        if (!ReachNpc("smith", "첫 담금질 · 대장장이에게서 검 +1 강화")) return;
        if (requestKey == quest.id && Time.unscaledTime < nextActionRequest) return;
        var sword = data.Item(data.starterSwordInstanceId);
        var offer = sword?.nextEnhance;
        if (sword == null || sword.enhancement != 0 || offer == null)
        {
            Stop("대장장이에게서 첫 검을 확인하세요");
            return;
        }
        if (data.gold < offer.gold || (data.Item("stack:ore")?.quantity ?? 0) < offer.ore)
        {
            Stop("첫 담금질에 필요한 강화석과 골드를 확인하세요");
            return;
        }
        if (!ui.ShowQuestForge(sword.instanceId))
        {
            // Leave the free-to-cancel confirmation visible long enough to read the cost.
            nextStep = Time.unscaledTime + 1.5f;
            return;
        }
        if (CanSend(quest.id)) ui.ConfirmForge(sword.instanceId);
    }

    private bool CanSend(string key)
    {
        if (requestKey == key && Time.unscaledTime < nextActionRequest) return false;
        requestKey = key;
        nextActionRequest = Time.unscaledTime + 5f;
        return true;
    }

    private void OnDestroy()
    {
        if (current == this) current = null;
    }
}
