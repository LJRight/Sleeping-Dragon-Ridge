using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class PrototypeCombatHud : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.Button attackButton;
    [SerializeField] private TMP_Text healthText;
    private SkillButton[] skillButtons = System.Array.Empty<SkillButton>();
    private UnityEngine.UI.Button autoCombatButton;
    private TMP_Text autoCombatLabel;
    private UnityEngine.UI.Button potionButton;
    private TMP_Text potionLabel;
    private int displayedPotionSeconds = -1;
    private PlayerCombat localCombat;
    private ActorHealthBars actorBars;
    private RectTransform localTargetPanel;
    private HealthGauge localTargetGauge;
    private StringBuilder text = new(), displayedText = new();

    internal Transform SafeControls => attackButton.transform.parent;
    internal void HideDebugHealthText() => healthText.enabled = false;

    private void Awake()
    {
        attackButton.onClick.AddListener(Attack);
        attackButton.gameObject.SetActive(false);
        healthText.gameObject.SetActive(false);
    }

    private void Update()
    {
        var manager = NetworkManager.Singleton;
        bool local = LocalCombatTest.IsLocal(this);
        bool client = local || (manager != null && manager.IsClient && manager.IsConnectedClient);
        healthText.gameObject.SetActive(client);
        if (!client)
        {
            attackButton.gameObject.SetActive(false);
            foreach (var skill in skillButtons) skill.Button.gameObject.SetActive(false);
            if (autoCombatButton) autoCombatButton.gameObject.SetActive(false);
            if (potionButton) potionButton.gameObject.SetActive(false);
            localCombat = null;
            actorBars?.Hide();
            return;
        }
        if (skillButtons.Length == 0)
            skillButtons = new[]
            {
                BuildSkillButton("SpinningSlashButton", PlayerSkill.SpinningSlash),
                BuildSkillButton("UppercutButton", PlayerSkill.Uppercut),
                BuildSkillButton("JumpAttackButton", PlayerSkill.JumpAttack)
            };
        if (local && localCombat == null) localCombat = FindFirstObjectByType<PlayerCombat>();
        if (!local && localCombat == null && manager.LocalClient.PlayerObject != null)
            localCombat = manager.LocalClient.PlayerObject.GetComponent<PlayerCombat>();
        if (local) UpdateLocalTarget();
        bool showAttacks = !localCombat || !localCombat.IsSpectatingBoss;
        attackButton.gameObject.SetActive(showAttacks);
        foreach (var skill in skillButtons)
        {
            skill.Button.gameObject.SetActive(showAttacks);
            if (showAttacks) skill.Refresh(localCombat);
        }
        UpdateAutoCombatButton();
        UpdateHealthPotionButton();
        if (!showAttacks) return;
        // Keep the button usable during every combo stage; server code handles queued inputs.
        attackButton.interactable = localCombat != null && localCombat.CanRequestAttack;
        if (!healthText.isActiveAndEnabled) return;
        text.Clear();
        var actors = NetworkHealth.Spawned;
        for (int i = 0; i < actors.Count; i++)
        {
            var health = actors[i];
            if (!health || !health.IsActive || !localCombat || health.CombatRoom != localCombat.BossRoomId) continue;
            var view = DisplayedHealth.For(health);
            if (health.EntityKind == NetworkHealth.Kind.Monster) text.Append("Monster ").Append(health.NetworkObjectId);
            else if (health.IsLocalTest || health.IsOwner) text.Append("You");
            else text.Append("Player ").Append(health.OwnerClientId);
            text.Append(": ").Append(view.Rounded).Append(" / ").Append(view.RoundedMaximum);
            if (health.TryGetComponent<ServerMonsterMovement>(out var monster))
                text.Append("  [").Append(monster.CurrentState).Append(']');
            if (health.IsDead) text.Append("  DEAD");
            text.AppendLine();
        }
        if (!text.Equals(displayedText))
        {
            healthText.SetText(text);
            (text, displayedText) = (displayedText, text);
        }
    }

    private void LateUpdate()
    {
        if (!localCombat) return;
        actorBars ??= new ActorHealthBars(SafeControls);
        actorBars.Draw(localCombat, Camera.main, LocalCombatTest.IsLocal(this));
    }

    private void UpdateLocalTarget()
    {
        var target = localCombat ? localCombat.HudTarget : null;
        if (!target)
        {
            if (localTargetPanel && localTargetPanel.gameObject.activeSelf) localTargetPanel.gameObject.SetActive(false);
            return;
        }
        if (!localTargetPanel)
        {
            localTargetPanel = UiPrefab.Spawn("Boss/Status", SafeControls);
            localTargetGauge = new HealthGauge(UiPrefab.At<RectTransform>(localTargetPanel, "Boss Health Bar"),
                UiPrefab.At<TMP_Text>(localTargetPanel, "BossRoute"));
        }
        if (!localTargetPanel.gameObject.activeSelf) localTargetPanel.gameObject.SetActive(true);
        localTargetGauge.Render(DisplayedHealth.For(target), target.MonsterDisplayName);
    }

    private SkillButton BuildSkillButton(string prefab, PlayerSkill skill)
    {
        var rect = UiPrefab.Spawn("Combat/" + prefab, SafeControls);
        var button = rect.GetComponent<UnityEngine.UI.Button>();
        UiPrefab.Listen(button, () => { if (localCombat) localCombat.RequestSkill(skill); }, playClick: false);
        var seconds = UiPrefab.At<TMP_Text>(rect, "Cooldown Seconds");
        seconds.outlineWidth = .20f;
        UiPrefab.At<TMP_Text>(rect, "Caption").outlineWidth = .20f;
        return new SkillButton(skill, button, UiPrefab.At<UnityEngine.UI.Image>(rect, "Cooldown Sweep"), seconds);
    }

    private sealed class SkillButton
    {
        public readonly UnityEngine.UI.Button Button;
        private readonly PlayerSkill skill;
        private readonly UnityEngine.UI.Image ring;
        private readonly TMP_Text label;
        private int displayedSeconds = -1;

        public SkillButton(PlayerSkill skill, UnityEngine.UI.Button button, UnityEngine.UI.Image ring, TMP_Text label)
        {
            this.skill = skill;
            Button = button;
            this.ring = ring;
            this.label = label;
        }

        public void Refresh(PlayerCombat combat)
        {
            Button.interactable = combat && combat.CanRequestSkill(skill);
            float remaining = combat ? combat.SkillCooldownRemaining(skill) : 0f;
            ring.fillAmount = remaining > 0f ? Mathf.Clamp01(remaining / combat.SkillCooldownDuration(skill)) : 0f;
            int seconds = Mathf.CeilToInt(remaining);
            if (seconds == displayedSeconds) return;
            displayedSeconds = seconds;
            label.enabled = seconds > 0;
            if (seconds > 0) label.SetText("{0}", seconds);
        }
    }

    private void UpdateAutoCombatButton()
    {
        bool visible = localCombat && localCombat.CanToggleAutoCombat && !ExpeditionUI.BlocksGameplay;
        if (visible && !autoCombatButton)
        {
            var rect = UiPrefab.Spawn("Combat/AutoCombatButton", SafeControls);
            autoCombatButton = rect.GetComponent<UnityEngine.UI.Button>();
            autoCombatLabel = UiPrefab.At<TMP_Text>(rect, "Label");
            autoCombatButton.onClick.AddListener(ToggleAutoCombat);
        }
        if (!autoCombatButton) return;
        autoCombatButton.gameObject.SetActive(visible);
        if (visible) autoCombatLabel.text = localCombat.AutoCombatEnabled ? "자동 전투 켜짐" : "자동 전투 꺼짐";
    }

    private void ToggleAutoCombat()
    {
        if (!localCombat || ExpeditionUI.BlocksGameplay) return;
        GameplayUiAudio.PlayClick();
        localCombat.ToggleAutoCombat();
    }

    private void UpdateHealthPotionButton()
    {
        bool visible = localCombat && !localCombat.IsSpectatingBoss && !ExpeditionUI.BlocksGameplay;
        if (visible && !potionButton)
        {
            var rect = UiPrefab.Spawn("Combat/HealthPotionButton", SafeControls);
            potionButton = rect.GetComponent<UnityEngine.UI.Button>();
            potionLabel = UiPrefab.At<TMP_Text>(rect, "Cooldown Seconds");
            potionLabel.outlineWidth = .20f;
            potionButton.onClick.AddListener(UseHealthPotion);
        }
        if (!potionButton) return;
        potionButton.gameObject.SetActive(visible);
        if (!visible) return;
        potionButton.interactable = localCombat.CanUseHealthPotion;
        int seconds = Mathf.CeilToInt(localCombat.HealthPotionCooldownRemaining);
        if (seconds == displayedPotionSeconds) return;
        displayedPotionSeconds = seconds;
        potionLabel.enabled = seconds > 0;
        if (seconds > 0) potionLabel.SetText("{0}", seconds);
    }

    private void UseHealthPotion()
    {
        if (!localCombat || !localCombat.CanUseHealthPotion) return;
        localCombat.RequestHealthPotion();
    }

    private void Attack() { if (localCombat != null) localCombat.RequestAttack(); }
    private void OnDestroy()
    {
        if (attackButton != null) attackButton.onClick.RemoveListener(Attack);
        if (autoCombatButton) autoCombatButton.onClick.RemoveListener(ToggleAutoCombat);
        if (potionButton) potionButton.onClick.RemoveListener(UseHealthPotion);
    }
}
