using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using static UiPrefab;

public sealed partial class ExpeditionUI
{
    private TMPro.TextMeshProUGUI partyLabel;
    private PlayerCombat observedOwner, selectedPlayer;
    private BossPartyState observedParty;
    private ulong? displayedInvitation;
    private string displayedCheck;

    private static PlayerCombat LocalCombat =>
        NetworkManager.Singleton?.LocalClient?.PlayerObject?.GetComponent<PlayerCombat>();

    private static string KnightName(ulong id) => $"기사 {id}";

    private void BuildPartyControls()
    {
        var openParty = At<RectTransform>(hud, "OpenParty");
        Listen(openParty.GetComponent<Button>(), () => SetPage(Page.Party));
        partyLabel = At<TextMeshProUGUI>(openParty, "Label");
    }

    private void UpdatePartyControls(PlayerCombat owner, NetworkHealth hp)
    {
        if (!owner || !hp) return;
        if (observedOwner != owner)
        {
            observedOwner = owner;
            observedParty = null;
            displayedInvitation = null;
            displayedCheck = null;
            selectedPlayer = null;
        }
        var party = owner.PartyState;
        bool changed = observedParty != party;
        bool checkClosed = observedParty?.preparing == true && !party.preparing;
        observedParty = party;
        if (!party.invited) displayedInvitation = null;
        if (!party.preparing) displayedCheck = null;
        if (changed)
        {
            if (checkClosed && page == Page.Party) SetPage(Page.Field);
            int count = party.members?.Length ?? 0;
            partyLabel.text = party.invited ? "파티 초대 도착" : party.preparing ? "입장 확인"
                : count > 0 ? $"파티  {count}/4" : "파티";
            if (page == Page.Party || page == Page.BossEntry || page == Page.InvitePlayer) DrawPage();
        }

        if (page == Page.InvitePlayer && !CanSelect(owner, selectedPlayer)) SetPage(Page.Field);
        if (hp.IsDead) return;
        if (page != Page.Field && page != Page.Party) return;
        if (party.invited && displayedInvitation != party.inviter)
        {
            displayedInvitation = party.inviter;
            SetPage(Page.Party);
        }
        else if (party.preparing && party.token != displayedCheck)
        {
            displayedCheck = party.token;
            SetPage(Page.Party);
        }
    }

    private void DrawParty()
    {
        var owner = LocalCombat;
        if (!owner) return;
        var party = owner.PartyState;
        var members = party.members ?? Array.Empty<ulong>();
        string asset = party.invited ? "Party/Invitation" : members.Length == 0 ? "Party/Solo" : "Party/Roster";
        var pane = Spawn(asset, modal);
        At<TextMeshProUGUI>(pane, "Title").text = party.invited ? "파티 초대" : party.preparing ? "보스방 입장 확인"
            : members.Length > 0 ? $"내 파티   {members.Length}/4" : "파티";
        Listen(At<Button>(pane, "CloseParty"), () => SetPage(Page.Field));
        if (party.invited)
        {
            At<TextMeshProUGUI>(pane, "Description").text = $"{KnightName(party.inviter)} 님이 파티에 초대했습니다.";
            string inviter = party.inviter.ToString();
            BindPartyAction(pane, "DeclinePartyInvite", "partyDecline", inviter);
            BindPartyAction(pane, "AcceptPartyInvite", "partyAccept", inviter);
            return;
        }
        if (members.Length == 0)
        {
            if (party.preparing)
            {
                At<TextMeshProUGUI>(pane, "Description").text = "혼자 도전할 보스방을 준비하고 있습니다.";
                BindPartyAction(pane, "CancelPartyEntry", "partyCancel", party.token);
            }
            else At<Button>(pane, "CancelPartyEntry").gameObject.SetActive(false);
            return;
        }
        var ready = party.ready ?? Array.Empty<ulong>();
        var roster = At<RectTransform>(pane, "Roster");
        for (int i = 0; i < roster.childCount; i++)
        {
            var slot = roster.GetChild(i);
            slot.gameObject.SetActive(i < members.Length);
            if (i >= members.Length) continue;
            var row = At<RectTransform>(slot, "MemberRow");
            ulong id = members[i];
            bool leader = id == party.leader;
            var role = At<TextMeshProUGUI>(row, "Role");
            role.text = leader ? "파티장" : "파티원";
            role.color = leader ? ExpeditionWidgets.Gold : ExpeditionWidgets.Muted;
            At<TextMeshProUGUI>(row, "Name").text = KnightName(id) + (id == owner.OwnerClientId ? "  (나)" : "");
            var readyLabel = At<TextMeshProUGUI>(row, "Ready");
            readyLabel.gameObject.SetActive(party.preparing);
            if (party.preparing)
            {
                bool accepted = Array.IndexOf(ready, id) >= 0;
                readyLabel.text = accepted ? "입장 수락" : "수락 대기";
                readyLabel.color = accepted ? ExpeditionWidgets.Ice : ExpeditionWidgets.Muted;
            }
        }
        var instruction = At<TextMeshProUGUI>(pane, "EntryInstruction");
        instruction.gameObject.SetActive(party.preparing);
        var cancel = At<Button>(pane, "CancelPartyEntry");
        cancel.gameObject.SetActive(party.preparing);
        bool canAccept = party.preparing && Array.IndexOf(ready, owner.OwnerClientId) < 0;
        At<Button>(pane, "AcceptPartyEntry").gameObject.SetActive(canAccept);
        At<Button>(pane, "LeaveParty").gameObject.SetActive(!party.preparing && !owner.InBossArena);
        if (party.preparing)
        {
            string location = party.source == "town" ? "마을" : "관문";
            instruction.text = $"파티원 모두 {location}에서 수락하면 입장합니다.";
            bool leader = owner.OwnerClientId == party.leader;
            At<TextMeshProUGUI>(cancel.transform, "Label").text = leader ? "요청 취소" : "입장 거절";
            BindPartyAction(pane, "CancelPartyEntry", "partyCancel", party.token);
            if (canAccept) BindPartyAction(pane, "AcceptPartyEntry", "partyReady", party.token);
        }
        else if (!owner.InBossArena)
        {
            BindPartyAction(pane, "LeaveParty", "partyLeave");
        }
    }

    private static bool CanSelect(PlayerCombat owner, PlayerCombat other) =>
        owner && other && other != owner && other.IsSpawned && other.NetworkObject.IsPlayerObject
        && !other.InBossArena && !owner.InBossArena && other.MapSceneName == owner.MapSceneName;

    private void DrawInvitePlayer()
    {
        var owner = LocalCombat;
        if (!CanSelect(owner, selectedPlayer)) return;
        var party = owner.PartyState;
        var members = party.members ?? Array.Empty<ulong>();
        ulong targetId = selectedPlayer.OwnerClientId;
        string reason = Array.IndexOf(members, targetId) >= 0 ? "같은 파티에 속한 기사입니다."
            : party.invited || party.preparing ? "진행 중인 요청에 먼저 응답하세요."
            : members.Length >= 4 ? "파티 정원 4명이 모두 모였습니다."
            : members.Length > 0 && party.leader != owner.OwnerClientId ? "파티장이 다른 기사를 초대할 수 있습니다."
            : null;
        var pane = Spawn("Party/SelectedPlayer", modal);
        At<TextMeshProUGUI>(pane, "Title").text = KnightName(targetId);
        if (reason != null) At<TextMeshProUGUI>(pane, "Description").text = reason;
        Listen(At<Button>(pane, "CloseSelectedPlayer"), () => SetPage(Page.Field));
        var invite = At<Button>(pane, "InviteSelectedPlayer");
        invite.gameObject.SetActive(reason == null);
        var trade = At<Button>(pane, "RequestTrade");
        trade.gameObject.SetActive(owner.AreaId == 0);
        trade.interactable = GameProgressBridge.Ready && owner.Trade == null;
        Listen(trade, () => { owner.InviteToTrade(targetId); SetPage(Page.Field); });
        if (reason == null) Listen(invite, () => SendPartyAction("partyInvite", targetId.ToString()));
    }

    private void BindPartyAction(Transform pane, string id, string action, string target = "")
    {
        Listen(At<Button>(pane, id), () => SendPartyAction(action, target));
    }

    private void SendPartyAction(string action, string target)
    {
        GameProgressBridge.Request(action, target);
        SetPage(Page.Field);
    }
}
