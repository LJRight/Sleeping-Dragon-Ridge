"""Regression tests for the guest, quest, progress and inventory contracts."""
import concurrent.futures
from contextlib import closing
import json
import math
import os
from pathlib import Path
import secrets
import sqlite3
import subprocess
import sys
import tempfile
import threading
import unittest
from unittest.mock import patch
from urllib.error import HTTPError
from urllib.request import Request, urlopen

if __package__:
    from . import inventory, progression, quests, server, prepare_test_account
else:
    import inventory
    import progression
    import quests
    import server
    import prepare_test_account


class Gameplay(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name)/"test.sqlite3"
        self.store = server.Store(self.path)
        self.seed = secrets.token_urlsafe(32)
        self.session = self.store.guest(self.seed, True)
        self.user = self.session["user"]["id"]

    def read(self, user=None):
        with self.store.connect() as db:
            db.execute("BEGIN")
            return progression.snapshot(db, user or self.user)

    def test_editor_server_advertises_ephemeral_port_and_exits_on_parent_eof(self):
        session = secrets.token_hex(16)
        command = [sys.executable, "-B", "-u", server.__file__, "--db", str(self.path),
                   "serve", "--host", "127.0.0.1", "--port", "0", "--editor-parent"]
        env = dict(os.environ, NEO_EDITOR_SESSION=session, GAME_SERVER_KEY=secrets.token_urlsafe(32))
        with subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, env=env) as child:
            with concurrent.futures.ThreadPoolExecutor(max_workers=1) as reader:
                try:
                    port = int(reader.submit(child.stdout.readline).result(timeout=10))
                    self.assertTrue(0 < port <= 65535)
                    with urlopen(f"http://127.0.0.1:{port}/health", timeout=3) as response:
                        self.assertEqual(json.load(response), {"status": "ok", "editorSession": session})
                    child.stdin.close()
                    self.assertEqual(child.wait(timeout=5), 0)
                finally:
                    # Unblock the reader even when startup stalls or an assertion fails.
                    if child.poll() is None:
                        child.kill()
                        child.wait(timeout=5)

    def act(self, kind, target, npc="", event=None, revision=None, user=None):
        user = user or self.user
        data = dict(kind=kind, target=target, npcId=npc, actionId=event or secrets.token_hex(10),
                    revision=self.read(user)["revision"] if revision is None else revision)
        with self.store.connect() as db:
            db.execute("BEGIN IMMEDIATE")
            return progression.action(db, user, data)

    def status(self, key, snap=None):
        return next(q for q in (snap or self.read())["quests"] if q["id"] == key)

    def attempt(self, operation, attempt_id, members=None):
        data = dict(attemptId=attempt_id)
        if members is not None:
            data["members"] = members
        with self.store.connect() as db:
            db.execute("BEGIN IMMEDIATE")
            return progression.boss_attempt(db, operation, data)

    def wear_first_sword(self, user):
        accepted = self.act("accept", "first_temper", "smith", user=user)
        sword = accepted["starterSwordInstanceId"]
        self.act("temper", sword, "smith", user=user)
        self.act("accept", "equip_sword", "warden", user=user)
        return self.act("equip", sword, user=user)

    def hunt_to_level(self, user, state, monster, level):
        threshold = state["levelXpThresholds"][level-1]
        xp = progression.monster_rewards.BALANCE["field"][monster]["xp"]
        if state["totalXp"] < threshold:
            self.assertGreater(xp, 0, f"{monster} must award XP to reach level {level}")
            for _ in range(math.ceil((threshold - state["totalXp"]) / xp)):
                state = self.act("kill", monster, user=user)
        self.assertGreaterEqual(state["level"], level)
        return state

    def unlock_gate(self, user):
        self.act("accept", "watch", "warden", user=user)
        self.act("tutorial", "open_inventory", user=user)
        self.wear_first_sword(user)
        for key, monster, required, next_level in [
                ("hunt", "A1", 1, 3), ("hunt_a2", "A2", 2, 5), ("hunt_a3", "A3", 3, 10),
                ("hunt_a4", "A4", 4, 15), ("hunt_boar", "B1", 3, 20)]:
            self.act("accept", key, "warden", user=user)
            for _ in range(required):
                self.act("kill", monster, user=user)
            report = self.act("report", key, "warden", user=user)
            self.hunt_to_level(user, report, monster, next_level)
        return self.read(user)

    def test_party_clear_roster_once_offline_reports_and_repeated_victories(self):
        members = [self.user] + [self.store.guest(secrets.token_urlsafe(32), True)["user"]["id"] for _ in range(3)]
        for user in members:
            self.unlock_gate(user)
        for user in members[:2]:
            self.act("accept", "ridge_boss", "warden", user=user)
        with self.assertRaisesRegex(progression.ProgressError, "INVALID_MEMBERS"):
            self.attempt("start", "invalid", members + [members[0]])
        with self.assertRaisesRegex(progression.ProgressError, "UNKNOWN_ATTEMPT"):
            self.attempt("clear", "invalid")
        start = self.attempt("start", "raid-1", members)
        self.assertEqual(start, dict(attemptId="raid-1", eligible=sorted(members[:2]), cleared=False))
        self.assertEqual(self.attempt("start", "raid-1", members[::-1]), start)
        with self.assertRaisesRegex(progression.ProgressError, "ATTEMPT_ID_REUSED"):
            self.attempt("start", "raid-1", members[:2])
        self.act("accept", "ridge_boss", "warden", user=members[2])
        snapshots = {u: self.read(u) for u in members}
        # Simulate no connected clients and a restarted login service before the trusted server delivers victory.
        self.store = server.Store(self.path)
        with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
            clears = list(pool.map(lambda _: self.attempt("clear", "raid-1"), range(3)))
        self.assertTrue(all(result == dict(start, cleared=True) for result in clears))
        for user in members[:2]:
            first = snapshots[user]
            ready = self.read(user)
            self.assertEqual((self.status("ridge_boss", ready)["state"], ready["revision"], ready["gold"]),
                             ("ready", first["revision"] + 1, first["gold"]))
        for user in members[2:]:
            self.assertEqual(self.read(user), snapshots[user])
        self.assertEqual(self.status("ridge_boss", self.read(members[1]))["state"], "ready")
        for user in members[:2]:
            first = snapshots[user]
            before_items = {i["definitionId"]: i["quantity"] for i in first["items"] if i["type"] == "material"}
            claim = self.act("report", "ridge_boss", "warden", user=user, event="claim")
            sword, = [i for i in claim["items"] if i["definitionId"] == "ridge_sword"]
            payout = progression.monster_rewards.BALANCE["bossReport"]
            self.assertEqual((claim["objective"], claim["gold"], sword["rarity"], sword["enhancement"]),
                             ("complete", first["gold"] + payout["gold"], "legendary", 0))
            for material, n in (("ore", payout["oreCount"]), ("rare_ore", payout["refinedOreCount"])):
                quantity = sum(i["quantity"] for i in claim["items"] if i["definitionId"] == material)
                self.assertEqual(quantity, before_items.get(material, 0) + n)
            self.assertEqual(self.act("report", "ridge_boss", "warden", user=user, event="claim", revision=0), claim)
            with self.assertRaisesRegex(progression.ProgressError, "QUEST_NOT_READY"):
                self.act("report", "ridge_boss", "warden", user=user)
        self.assertEqual(self.attempt("start", "raid-2", members)["eligible"], [members[2]])
        self.attempt("clear", "raid-2")
        first_sword = next(i["instanceId"] for i in self.read()["items"] if i["definitionId"] == "ridge_sword")
        self.act("equip", first_sword)
        with self.assertRaisesRegex(progression.ProgressError, "RIDGE_SWORD_EQUIPPED"):
            self.attempt("start", "raid-3", members)
        self.act("unequip", first_sword)
        self.assertEqual(self.attempt("start", "raid-3", [self.user])["eligible"], [])
        self.assertEqual(self.status("ridge_boss")["state"], "completed")
        with self.assertRaisesRegex(progression.ProgressError, "UNKNOWN_MONSTER"):
            self.act("kill", "ridge_boss")

    def test_pre_boss_main_chain_and_level_gates(self):
        fresh = self.read()
        self.assertEqual(len(fresh["quests"]), 12)
        self.assertEqual(sum(q["category"] == "main" for q in fresh["quests"]), 9)
        self.assertEqual(fresh["levelXpThresholds"], list(progression.THRESHOLDS))
        self.act("accept", "watch", "warden")
        self.act("tutorial", "open_inventory")
        self.wear_first_sword(self.user)
        steps = [
            ("hunt", ["A1"], "hunt_a2", 3),
            ("hunt_a2", ["A2"]*2, "hunt_a3", 5),
            ("hunt_a3", ["A3"]*3, "hunt_a4", 10),
            ("hunt_a4", ["A4"]*4, "hunt_boar", 15),
            ("hunt_boar", ["B1G", "B1", "B1G"], "ridge_boss", 20),
        ]
        for key, kills, successor, level in steps:
            with self.subTest(quest=key):
                before = self.read()
                self.act("accept", key, "warden")
                for target in kills:
                    self.act("kill", target)
                self.assertEqual(self.status(key)["state"], "ready")
                result = self.act("report", key, "warden")
                xp = sum(progression.monster_rewards.BALANCE["field"][target]["xp"] for target in kills)
                expected = before["totalXp"] + xp + self.status(key, result)["rewardXp"]
                self.assertEqual(result["totalXp"], min(expected, progression.THRESHOLDS[-1]))
                next_quest = self.status(successor, result)
                self.assertEqual((next_quest["requires"], next_quest["minLevel"]), ([key], level))
                if result["level"] < level:
                    self.assertEqual((result["objective"], next_quest["state"]), ("hunt", "locked"))
                    with self.assertRaisesRegex(progression.ProgressError, "QUEST_NOT_AVAILABLE"):
                        self.act("accept", successor, "warden")
                    if successor == "ridge_boss":
                        with self.assertRaisesRegex(progression.ProgressError, "BOSS_QUEST_LOCKED"):
                            self.attempt("start", "premature", [self.user])
                result = self.hunt_to_level(self.user, result, kills[0], level)
                self.assertEqual(self.status(successor, result)["state"], "available")
        self.assertEqual((result["level"], result["objective"]), (20, "boss"))
        self.assertEqual(self.status("supplies")["state"], "available")

    def test_first_sword_checks_equipped_slot_and_remembers_success(self):
        self.act("accept", "watch", "warden")
        watched = self.act("tutorial", "open_inventory")
        starter = next(i["instanceId"] for i in watched["items"] if i["definitionId"] == "balance_sword")
        with self.store.connect() as db:
            helm = inventory.grant(db, self.user, "vital_helm")
        self.act("equip", helm)
        self.assertEqual(self.status("equip_sword")["goals"][0]["observedCount"], 0)
        with self.assertRaisesRegex(progression.ProgressError, "EQUIP_ACTION_REQUIRED"):
            self.act("tutorial", "equip_sword")
        self.act("accept", "first_temper", "smith")
        self.act("temper", starter, "smith")
        self.act("equip", starter)
        self.act("unequip", starter)
        result = self.act("accept", "equip_sword", "warden", event="already-equipped-before")
        self.assertEqual(self.status("equip_sword", result)["state"], "completed")
        self.assertEqual(self.act("accept", "equip_sword", "warden", revision=0, event="already-equipped-before"), result)
        self.assertEqual(next(i["quantity"] for i in result["items"] if i["definitionId"] == "ore"), 1)

    def test_first_temper_only_pays_once_for_the_reward_sword_at_the_smith(self):
        with self.store.connect() as db:
            spare = inventory.grant(db, self.user, "balance_sword")
        self.act("accept", "watch", "warden")
        watched = self.act("tutorial", "open_inventory")
        sword = watched["starterSwordInstanceId"]
        self.assertNotEqual(spare, sword)
        self.assertEqual(self.status("equip_sword", watched)["state"], "locked")
        with self.assertRaisesRegex(progression.ProgressError, "TEMPER_QUEST_NOT_ACTIVE"):
            self.act("temper", sword, "smith")
        accepted = self.act("accept", "first_temper", "smith", event="smith-accept")
        self.assertEqual(self.act("accept", "first_temper", "smith", event="smith-accept", revision=0), accepted)
        self.assertEqual((accepted["gold"], self.status("first_temper", accepted)["state"]), (10, "active"))
        self.assertEqual(next(i["quantity"] for i in accepted["items"] if i["definitionId"] == "ore"), 1)
        for action, target, npc, error in [
                ("accept", "first_temper", "smith", "QUEST_NOT_AVAILABLE"),
                ("temper", spare, "smith", "STARTER_SWORD_REQUIRED"),
                ("temper", sword, "warden", "WRONG_NPC"), ("enhance", sword, "", "WRONG_NPC"),
                ("tutorial", "temper_starter_sword", "", "ENHANCE_ACTION_REQUIRED"),
                ("tutorial", "enhance", "", "ENHANCE_ACTION_REQUIRED")]:
            with self.subTest(action=action, npc=npc), self.assertRaisesRegex(progression.ProgressError, error):
                self.act(action, target, npc)
        self.assertEqual(self.read(), accepted)
        with self.store.connect() as db:
            db.execute("UPDATE user_progress SET gold=9 WHERE user_id=?", (self.user,))
        insolvent = self.read()
        with self.assertRaisesRegex(progression.ProgressError, "INSUFFICIENT_GOLD"):
            self.act("temper", sword, "smith")
        self.assertEqual(self.read(), insolvent)
        with self.store.connect() as db:
            db.execute("UPDATE user_progress SET gold=10 WHERE user_id=?", (self.user,))

        def submit(_):
            return self.act("temper", sword, "smith", event="smith-upgrade", revision=accepted["revision"])
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            first, replay = pool.map(submit, range(2))
        self.assertEqual(first, replay)
        self.assertEqual(first["enhanceResult"], dict(instanceId=sword, before=0, after=1, outcome="success", actionId="smith-upgrade"))
        self.assertEqual((first["gold"], first["revision"], self.status("first_temper", first)["state"]),
                         (0, accepted["revision"] + 1, "completed"))
        self.assertFalse(any(i["definitionId"] == "ore" for i in first["items"]))
        self.assertEqual(self.status("equip_sword", first)["state"], "available")
        with self.assertRaisesRegex(progression.ProgressError, "TEMPER_QUEST_NOT_ACTIVE"):
            self.act("temper", sword, "smith")
        self.store = server.Store(self.path)
        self.assertEqual(self.act("temper", sword, "smith", event="smith-upgrade", revision=0), first)

    def test_first_temper_credits_manual_upgrade_before_or_after_acceptance(self):
        for beforehand in (False, True):
            user = self.store.guest(secrets.token_urlsafe(32), True)["user"]["id"]
            self.act("accept", "watch", "warden", user=user)
            sword = self.act("tutorial", "open_inventory", user=user)["starterSwordInstanceId"]
            with self.store.connect() as db:
                spare = inventory.grant(db, user, "balance_sword")
                inventory.stack(db, user, "ore", 2)
                db.execute("UPDATE user_progress SET gold=20 WHERE user_id=?", (user,))
            if not beforehand:
                self.act("accept", "first_temper", "smith", user=user)
            wrong = self.act("enhance", spare, "smith", user=user)
            goal = self.status("first_temper", wrong)["goals"][0]
            self.assertEqual((goal["observedCount"], goal["count"]), (0, 0))
            upgraded = self.act("enhance", sword, "smith", user=user)
            if beforehand:
                self.assertEqual(self.status("first_temper", upgraded)["state"], "available")
                # The recorded success survives loss of the item before the player visits the smith.
                with self.store.connect() as db:
                    db.execute("DELETE FROM item_instances WHERE user_id=? AND instance_id=?", (user, sword))
                upgraded = self.act("accept", "first_temper", "smith", user=user)
            self.assertEqual((upgraded["gold"], self.status("first_temper", upgraded)["state"]), (0, "completed"))
            self.assertEqual(next(i["quantity"] for i in upgraded["items"] if i["definitionId"] == "ore"), 1)
            with self.assertRaisesRegex(progression.ProgressError, "TEMPER_QUEST_NOT_ACTIVE"):
                self.act("temper", sword, "smith", user=user)

    def test_first_temper_migrates_old_swords_and_preserves_existing_contracts(self):
        users = [self.user] + [self.store.guest(secrets.token_urlsafe(32), True)["user"]["id"] for _ in range(2)]
        old = [dict(q) for q in quests.definitions().values() if q["id"] != "first_temper"]
        next(q for q in old if q["id"] == "equip_sword")["requires"] = ["watch"]
        path = Path(self.temp.name) / "before-smith.json"
        path.write_text(json.dumps(old))
        with patch.dict(os.environ, GAME_QUEST_FILE=str(path)):
            for user in users:
                self.act("accept", "watch", "warden", user=user)
                self.act("tutorial", "open_inventory", user=user)
            self.act("accept", "equip_sword", "warden", user=users[2])
        with self.store.connect() as db:
            # Reproduce the old table without a reward-instance link or a dedicated tutorial history.
            db.execute("ALTER TABLE quests DROP COLUMN starter_sword_id")
            original, = db.execute("SELECT instance_id FROM item_instances WHERE user_id=?", (users[0],)).fetchone()
            db.execute("UPDATE item_instances SET enhancement=1 WHERE user_id=?", (users[0],))
            inventory.grant(db, users[0], "balance_sword")
            for user in users[1:]:
                db.execute("DELETE FROM item_instances WHERE user_id=?", (user,))
        self.store = server.Store(self.path)
        self.assertEqual(self.read()["starterSwordInstanceId"], original)
        recognized = self.act("accept", "first_temper", "smith")
        self.assertEqual((recognized["gold"], self.status("first_temper", recognized)["state"]), (10, "completed"))
        for user, expected in zip(users[1:], ("available", "active")):
            restored = self.read(user)
            self.assertFalse(any(q["id"] == "first_temper" for q in restored["quests"]))
            self.assertEqual((self.status("equip_sword", restored)["state"], self.status("equip_sword", restored)["requires"]),
                             (expected, ["watch"]))

    def test_legacy_hunt_contract_and_boss_entitlement(self):
        catalogue = json.loads(Path(quests.__file__).with_name("quests.json").read_text())
        legacy = {q["id"]: q for q in catalogue if q["id"] in ("watch", "supplies", "hunt", "ridge_boss")}
        legacy["hunt"].update(requires=["watch"], nextObjective="boss")
        legacy["ridge_boss"].update(requires=["hunt"], minLevel=1)
        path = Path(self.temp.name)/"old.json"
        path.write_text(json.dumps(list(legacy.values())))
        accepted = self.store.guest(secrets.token_urlsafe(32), True)["user"]["id"]
        with patch.dict(os.environ, GAME_QUEST_FILE=str(path)):
            for user in (self.user, accepted):
                self.act("accept", "watch", "warden", user=user)
                self.act("tutorial", "open_inventory", user=user)
                self.act("accept", "hunt", "warden", user=user)
            self.act("kill", "A1", user=accepted)
            self.act("report", "hunt", "warden", user=accepted)
            self.act("accept", "ridge_boss", "warden", user=accepted)
        self.act("kill", "A1")
        grandfathered = self.act("report", "hunt", "warden")
        self.assertEqual((grandfathered["level"], grandfathered["objective"]), (2, "boss"))
        self.assertEqual(self.status("ridge_boss", grandfathered)["state"], "available")
        self.act("accept", "ridge_boss", "warden")
        old_boss = self.status("ridge_boss", self.read(accepted))
        self.assertEqual((old_boss["state"], old_boss["minLevel"], old_boss["requires"]), ("active", 1, ["hunt"]))
        self.assertEqual(self.attempt("start", "legacy", [self.user, accepted])["eligible"], sorted([self.user, accepted]))
        self.attempt("clear", "legacy")
        self.assertEqual(self.status("ridge_boss")["state"], "ready")
        self.assertEqual(self.status("ridge_boss", self.read(accepted))["state"], "ready")

    def test_enhancement_costs_roll_boundaries_cap_and_retry(self):
        self.assertNotIn(inventory.RIDGE_SWORD, inventory.FIELD_GEAR)
        for definition, step, roll, outcome, after in [
                ("balance_sword", 0, 99, "success", 1), ("balance_sword", 1, 90, "keep", 1),
                ("balance_sword", 3, 90, "downgrade", 2), ("balance_armor", 5, 99, "destroy", -1),
                ("ridge_sword", 5, 99, "downgrade", 4), ("ridge_sword", 6, 99, "downgrade", 5),
                ("ridge_sword", 6, 6, "success", 7)]:
            with self.subTest(definition=definition, step=step, roll=roll):
                with self.store.connect() as db:
                    item = inventory.grant(db, self.user, definition, "legendary", enhancement=step)
                    db.execute("UPDATE user_progress SET gold=2000 WHERE user_id=?", (self.user,))
                    db.execute("DELETE FROM item_stacks WHERE user_id=?", (self.user,))
                    inventory.stack(db, self.user, "ore", 20)
                    inventory.stack(db, self.user, "rare_ore", 10)
                worn = self.act("equip", item)
                gear = next(i for i in worn["items"] if i["instanceId"] == item)
                rule = gear["nextEnhance"]
                self.assertEqual(sum(rule[k] for k in ("success", "keep", "downgrade", "destroy")), 100)
                aid = "upgrade-" + item
                with patch.object(inventory.secrets, "randbelow", return_value=roll) as random:
                    result = self.act("enhance", item, "smith", event=aid)
                    self.assertEqual(self.act("enhance", item, "smith", event=aid, revision=0), result)
                    random.assert_called_once_with(100)
                self.assertEqual(result["enhanceResult"],
                                 dict(actionId=aid, instanceId=item, outcome=outcome, before=step, after=after))
                self.assertEqual(result["gold"], 2000 - rule["gold"])
                materials = {i["definitionId"]: i["quantity"] for i in result["items"] if i["type"] == "material"}
                self.assertEqual(materials, dict(ore=20-rule["ore"], rare_ore=10-rule["rareOre"]))
                found = next((i for i in result["items"] if i["instanceId"] == item), None)
                if outcome == "destroy":
                    self.assertIsNone(found)
                else:
                    self.assertEqual(found["enhancement"], after)
                    self.assertTrue(found["equipped"])
                if after == 7:
                    self.assertIsNone(found["nextEnhance"])
                    with self.assertRaisesRegex(progression.ProgressError, "ENHANCEMENT_AT_CAP"):
                        self.act("enhance", item, "smith")

    def test_equipped_quest_sword_concurrent_upgrade_and_rejoin(self):
        self.unlock_gate(self.user)
        self.act("accept", "ridge_boss", "warden")
        self.attempt("start", "solo-upgrade", [self.user])
        self.attempt("clear", "solo-upgrade")
        claim = self.act("report", "ridge_boss", "warden")
        sword = next(i["instanceId"] for i in claim["items"] if i["definitionId"] == inventory.RIDGE_SWORD)
        before = self.act("equip", sword)

        def submit(_):
            return self.act("enhance", sword, "smith", event="confirm-once", revision=before["revision"])

        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            first, retry = pool.map(submit, range(2))
        self.assertEqual(first, retry)
        self.store = server.Store(self.path)
        user = self.store.guest(self.session["guestToken"], False)["user"]["id"]
        restored = self.read(user)
        self.assertEqual(restored, {k: v for k, v in first.items() if k != "enhanceResult"})
        self.assertEqual(restored["revision"], before["revision"] + 1)
        self.assertEqual(restored["gold"], before["gold"] - 10)
        ore = next(i["quantity"] for i in before["items"] if i["definitionId"] == "ore")
        self.assertEqual(next(i["quantity"] for i in restored["items"] if i["definitionId"] == "ore"), ore - 1)
        gear = next(i for i in restored["items"] if i["instanceId"] == sword)
        self.assertTrue(gear["equipped"])
        self.assertEqual(gear["enhancement"], 1)
        self.assertEqual(restored["stats"], dict(before["stats"], attack=before["stats"]["attack"] + 1))

    def test_boss_sword_wildcard_mixed_and_matching_armor(self):
        with self.store.connect() as db:
            sword = inventory.grant(db, self.user, "ridge_sword")
            helm = inventory.grant(db, self.user, "fury_helm")
            armor = inventory.grant(db, self.user, "vital_armor")
            matching = inventory.grant(db, self.user, "fury_armor")
        data = self.act("equip", sword)
        self.assertEqual(data["stats"], dict(hp=100, attack=26, defense=0))
        self.act("equip", helm)
        data = self.act("equip", armor)
        self.assertEqual(data["stats"], dict(hp=132, attack=31, defense=6))
        gear = next(i for i in data["items"] if i["instanceId"] == sword)
        self.assertEqual({s["setId"]: s["setCount"] for s in gear["setBonuses"]}, dict(fury=2, vital=2, balance=1))
        data = self.act("equip", matching)
        self.assertEqual(data["stats"], dict(hp=106, attack=37, defense=1))
        unequipped = self.act("unequip", sword)
        self.assertEqual(unequipped["stats"], dict(hp=106, attack=17, defense=1))
        with self.assertRaisesRegex(progression.ProgressError, "INSUFFICIENT_MATERIALS"):
            self.act("enhance", sword, "smith")
        with self.store.connect() as db:
            inventory.stack(db, self.user, "ore", 1)
        with self.assertRaisesRegex(progression.ProgressError, "INSUFFICIENT_GOLD"):
            self.act("enhance", sword, "smith")
        self.assertEqual(next(i["quantity"] for i in self.read()["items"] if i["definitionId"] == "ore"), 1)

    def test_guest_atomic_retries_reauth_expiry_revocation(self):
        seed = secrets.token_urlsafe(32)
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
            replies = list(pool.map(lambda _: self.store.guest(seed, True), range(4)))
        self.assertEqual(len({r["user"]["id"] for r in replies}), 1)
        self.assertEqual(len({r["guestToken"] for r in replies}), 1)
        self.assertNotEqual(replies[0]["guestToken"], seed)
        with self.store.connect() as db:
            self.assertEqual(db.execute("SELECT count(*) FROM user_progress").fetchone()[0], 2)
            db.execute("UPDATE sessions SET expires_at=0")
            stored = db.execute(
                "SELECT creation_hash,refresh_hash FROM guest_keys WHERE user_id=?", (self.user,)).fetchone()
            self.assertNotIn(self.seed, stored)
            self.assertNotIn(self.session["guestToken"], stored)
        self.assertIsNone(self.store.user_for_token(self.session["accessToken"]))
        resumed = self.store.guest(self.session["guestToken"], False)
        self.assertEqual(resumed["user"]["id"], self.user)
        self.assertTrue(self.store.revoke_guest(self.user))
        self.assertIsNone(self.store.user_for_token(resumed["accessToken"]))
        for proof, create in [(self.session["guestToken"], False), (self.seed, True), (seed, False)]:
            with self.assertRaises(server.APIError):
                self.store.guest(proof, create)
        with self.store.connect() as db:
            db.execute("UPDATE guest_keys SET expires_at=0 WHERE user_id=?", (replies[0]["user"]["id"],))
        with self.assertRaises(server.APIError):
            self.store.guest(replies[0]["guestToken"], False)

    def test_real_rewards_all_quest_types_and_rejoin(self):
        b = self.store.guest(secrets.token_urlsafe(32), True)["user"]["id"]
        unchanged = self.read(b)
        self.assertEqual(self.status("hunt")["state"], "locked")
        with self.assertRaisesRegex(progression.ProgressError, "WRONG_NPC"):
            self.act("accept", "watch", "smith")
        self.act("accept", "watch", "warden")
        self.act("accept", "supplies", "warden", event="accept-01")
        self.act("accept", "supplies", "warden", event="accept-01", revision=0)
        self.assertEqual(self.status("supplies")["state"], "ready")
        self.act("tutorial", "attack_hit")
        self.assertEqual(self.status("watch")["state"], "active")
        self.act("tutorial", "open_inventory")
        self.assertEqual(self.status("watch")["state"], "completed")
        self.wear_first_sword(self.user)
        self.assertEqual(self.status("hunt")["state"], "available")
        self.act("accept", "hunt", "warden")
        self.act("kill", "A2")
        self.assertEqual(self.status("hunt")["count"], 0)
        pre = self.read()
        post = self.act("kill", "A1", event="monster-17", revision=pre["revision"])
        self.assertEqual(post["totalXp"]-pre["totalXp"], 20)
        self.assertEqual(post["gold"]-pre["gold"], 12)
        self.assertEqual(post["revision"], pre["revision"]+1)
        self.assertEqual(self.act("kill", "A1", event="monster-17", revision=pre["revision"]), post)
        with self.assertRaisesRegex(progression.ProgressError, "ACTION_ID_REUSED"):
            self.act("kill", "A2", event="monster-17")
        self.assertEqual(self.status("hunt", post)["state"], "ready")
        self.store = server.Store(self.path)
        self.assertEqual({k: v for k, v in post.items() if k != "lootResult"}, self.read())
        self.assertEqual(self.store.guest(self.session["guestToken"], False)["user"]["id"], self.user)
        with self.assertRaisesRegex(progression.ProgressError, "WRONG_NPC"):
            self.act("report", "supplies", "warden")
        snapshot = self.act("report", "supplies", "smith", event="delivery")
        self.assertFalse(any(i["definitionId"] == "parcel" for i in snapshot["items"]))
        same = self.act("report", "supplies", "smith", event="delivery", revision=0)
        self.assertEqual(snapshot, same)
        with self.assertRaisesRegex(progression.ProgressError, "QUEST_NOT_READY"):
            self.act("report", "supplies", "smith")
        self.act("report", "hunt", "warden")
        self.assertEqual(self.read()["gold"], 72)  # 10 - first temper 10 + A2 20 + A1 12 + delivery 15 + hunt 25
        self.assertEqual(self.read(b), unchanged)

    def test_monster_reward_file_controls_new_kills_and_new_quest_contracts(self):
        rewards = progression.monster_rewards
        balance = json.loads(Path(rewards.__file__).with_name("monster_rewards.json").read_text())
        path = Path(self.temp.name) / "monster_rewards.json"
        balance["field"]["A1"].update(xp=3, gold=0, oreChance=100, oreCount=4,
                                      commonGearChance=100, rareGearChance=1)
        path.write_text(json.dumps(balance))
        with self.assertRaisesRegex(ValueError, "equipment probabilities"):
            rewards.load(path)
        balance["field"]["A1"]["rareGearChance"] = 0
        balance["field"]["B1G"].update(xp=0, gold=0, commonGearChance=0, rareGearChance=0,
                                       legendaryGearChance=0, oreChance=100, oreCount=0, refinedOreChance=0)
        balance["questDropChance"]["ember_heartstone"] = 8
        balance["bossReport"].update(gold=23, oreCount=0)
        path.write_text(json.dumps(balance))

        self.unlock_gate(self.user)
        self.act("accept", "ridge_boss", "warden")
        self.act("accept", "ember_hearts", "smith")
        fresh = self.store.guest(secrets.token_urlsafe(32), True)["user"]["id"]
        self.unlock_gate(fresh)
        new_quests = Path(self.temp.name) / "quests.json"
        new_quests.write_bytes(Path(quests.__file__).with_name("quests.json").read_bytes())
        with patch.object(rewards, "BALANCE", rewards.load(path)), patch.dict(os.environ, GAME_QUEST_FILE=str(new_quests)):
            before = self.read()
            after = self.act("kill", "A1")
            self.assertEqual(after["totalXp"], before["totalXp"])  # the account is already at the cap
            self.assertEqual(after["gold"], before["gold"])
            self.assertEqual([(r["type"], r["quantity"]) for r in after["lootResult"]["entries"]],
                             [("equipment", 1), ("material", 4)])
            novice = self.store.guest(secrets.token_urlsafe(32), True)["user"]["id"]
            self.assertEqual(self.act("kill", "A1", user=novice)["totalXp"], 3)
            inventory_before = self.read(novice)["items"]
            with patch.object(progression.secrets, "randbelow", return_value=0):
                empty = self.act("kill", "B1G", user=novice)
            self.assertEqual(empty["lootResult"]["entries"], [])
            self.assertEqual(empty["items"], inventory_before)
            self.act("accept", "ridge_boss", "warden", user=fresh)
            self.act("accept", "ember_hearts", "smith", user=fresh)
            self.attempt("start", "edited-rewards", [self.user, fresh])
            self.attempt("clear", "edited-rewards")
            original = json.loads(Path(rewards.__file__).with_name("monster_rewards.json").read_text())
            for user, gold, ore, chance in [
                    (self.user, original["bossReport"]["gold"], original["bossReport"]["oreCount"],
                     original["questDropChance"]["ember_heartstone"]), (fresh, 23, 0, 8)]:
                snapshot = self.read(user)
                self.assertEqual(self.status("ember_hearts", snapshot)["goals"][0]["dropChance"], chance)
                payout = self.act("report", "ridge_boss", "warden", user=user)
                self.assertEqual(payout["gold"] - snapshot["gold"], gold)
                def ore_count(state):
                    return sum(item["quantity"] for item in state["items"] if item["definitionId"] == "ore")
                self.assertEqual(ore_count(payout) - ore_count(snapshot), ore)
                self.assertTrue(any(i["definitionId"] == "ridge_sword" for i in payout["items"]))

    def test_warden_prices_and_sale_replay(self):
        # Use the normal kill action and force one roll for each rarity, independent of balance edits.
        for rarity, price in (("common", 30), ("rare", 100), ("legendary", 300)):
            with self.subTest(rarity=rarity):
                odds = dict.fromkeys(progression.monster_rewards.GEAR_CHANCES, 0)
                odds[rarity + "GearChance"] = 100
                with patch.dict(progression.monster_rewards.BALANCE["field"]["A1"], odds):
                    before = self.act("kill", "A1")
                item, = [i for i in before["items"] if i["type"] == "equipment"]
                self.assertEqual((item["rarity"], item["sellGold"]), (rarity, price))
                with self.store.connect() as db:
                    db.execute("UPDATE item_instances SET enhancement=3 WHERE user_id=?", (self.user,))
                event = "sell-" + rarity
                def sell(_):
                    return self.act("sell", item["instanceId"], "warden", event=event, revision=before["revision"])
                with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
                    results = list(pool.map(sell, range(3)))
                self.assertEqual(results, [results[0]] * 3)
                self.assertEqual(results[0]["revision"], before["revision"] + 1)
                self.assertEqual(results[0]["gold"], before["gold"] + price)
                self.assertFalse(any(i["instanceId"] == item["instanceId"] for i in results[0]["items"]))
                with self.assertRaisesRegex(progression.ProgressError, "ITEM_NOT_SELLABLE"):
                    self.act("sell", item["instanceId"], "warden")
                with self.assertRaisesRegex(progression.ProgressError, "ACTION_ID_REUSED"):
                    self.act("sell", "another-item", "warden", event=event)

    def test_warden_rejects_wrong_npc_foreign_bound_equipped_and_overflow(self):
        other = self.store.guest(secrets.token_urlsafe(32), True)["user"]["id"]
        with self.store.connect() as db:
            bound = inventory.grant(db, self.user, "balance_sword")
            worn = inventory.grant(db, self.user, "vital_armor", hunted=True, equipped=True)
            foreign = inventory.grant(db, other, "fury_armor", hunted=True)
            owned = inventory.grant(db, self.user, "fury_helm", hunted=True)
            db.execute("UPDATE user_progress SET gold=? WHERE user_id=?", (2**31 - 1, self.user))
        before = self.read()
        for instance, npc, error in [(bound, "warden", "ITEM_NOT_SELLABLE"), (worn, "warden", "ITEM_NOT_SELLABLE"),
                                     (foreign, "warden", "ITEM_NOT_SELLABLE"), (owned, "smith", "WRONG_NPC"),
                                     (owned, "warden", "GOLD_LIMIT")]:
            with self.subTest(item=instance, npc=npc), self.assertRaisesRegex(progression.ProgressError, error):
                self.act("sell", instance, npc)
        self.assertEqual(self.read(), before)
        self.assertEqual(sum(i["sellGold"] for i in before["items"]), 30)

    def test_warden_sale_rolls_back_and_competes_with_equip(self):
        with self.store.connect() as db:
            item = inventory.grant(db, self.user, "fury_sword", hunted=True)
            db.execute("""CREATE TRIGGER fail_sale BEFORE DELETE ON item_instances
                          BEGIN SELECT raise(ABORT,'test interrupted sale'); END""")
        before = self.read()
        with self.assertRaisesRegex(sqlite3.IntegrityError, "test interrupted sale"):
            self.act("sell", item, "warden", event="sale")
        self.assertEqual(self.read(), before)
        with self.store.connect() as db:
            self.assertIsNone(db.execute("SELECT 1 FROM game_actions WHERE action_id='sale'").fetchone())
            db.execute("DROP TRIGGER fail_sale")
        start = threading.Barrier(2)
        def attempt(kind):
            start.wait(timeout=5)
            try:
                self.act(kind, item, "warden" if kind == "sell" else "", event=kind, revision=before["revision"])
                return kind
            except progression.ProgressError as error:
                self.assertEqual(error.code, "STALE_REVISION")
                return None
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(attempt, ("sell", "equip")))
        self.assertEqual(results.count(None), 1)
        after = self.read()
        self.assertEqual(after["revision"], before["revision"] + 1)
        self.assertEqual(after["gold"], 30 if "sell" in results else 0)
        self.assertEqual([i["equipped"] for i in after["items"]], [] if "sell" in results else [True])

    def test_hunt_origin_migration_protects_ambiguous_legacy_instances(self):
        self.act("accept", "watch", "warden")
        self.act("tutorial", "open_inventory")
        protected = self.read()["starterSwordInstanceId"]
        with patch.object(progression.secrets, "choice", return_value="balance_sword"), \
                patch.dict(progression.monster_rewards.BALANCE["field"]["A1"],
                           dict(commonGearChance=100, rareGearChance=0, legendaryGearChance=0)):
            self.act("kill", "A1")
        with patch.object(progression.secrets, "choice", return_value="fury_helm"), \
                patch.dict(progression.monster_rewards.BALANCE["field"]["A1"],
                           dict(commonGearChance=0, rareGearChance=100, legendaryGearChance=0)):
            hunt = self.act("kill", "A1")
        recovered, = [i["instanceId"] for i in hunt["items"] if i.get("rarity") == "rare"]
        with self.store.connect() as db:
            inventory.grant(db, self.user, "fury_helm", "rare", instance="fixture-example")
            inventory.grant(db, self.user, "vital_armor")  # No matching loot receipt.
            db.execute("ALTER TABLE item_instances DROP COLUMN hunted")
        for _ in range(2):
            self.store = server.Store(self.path)
            items = self.read()["items"]
            self.assertEqual([(i["instanceId"], i["sellGold"]) for i in items if i.get("sellGold")], [(recovered, 100)])
            self.assertTrue(any(i["instanceId"] == protected and i["sellGold"] == 0 for i in items))

    def test_field_loot_receipt_matches_grants_and_recovers_only_on_kill_retry(self):
        before = self.read()
        # This receipt scenario exercises a legendary and both materials independently of live drop rates.
        payouts = progression.monster_rewards.BALANCE["field"]
        guaranteed = dict(payouts["B1G"], commonGearChance=0, rareGearChance=0, legendaryGearChance=100,
                          oreChance=100, oreCount=6, refinedOreChance=100, refinedOreCount=2)
        with patch.dict(payouts, B1G=guaranteed), patch.object(progression.secrets, "choice", return_value="fury_helm"):
            rewarded = self.act("kill", "B1G", event="gold-boar")
        receipt = rewarded["lootResult"]
        self.assertEqual(receipt, dict(actionId="gold-boar", entries=[
            dict(type="gold", definitionId="gold", name="골드", quantity=guaranteed["gold"], rarity=""),
            dict(type="equipment", definitionId="fury_helm", name="맹공의 투구", quantity=1, rarity="legendary"),
            dict(type="material", definitionId="ore", name="강화석", quantity=6, rarity="common"),
            dict(type="material", definitionId="rare_ore", name="정제 강화석", quantity=2, rarity="common")]))
        self.assertEqual(rewarded["gold"] - before["gold"], receipt["entries"][0]["quantity"])
        self.assertCountEqual([(i["definitionId"], i["quantity"], i["rarity"]) for i in rewarded["items"]],
                              [(e["definitionId"], e["quantity"], e["rarity"]) for e in receipt["entries"][1:]])
        self.assertNotIn("lootResult", self.act("accept", "watch", "warden"))
        self.store = server.Store(self.path)
        latest = self.read()
        self.assertNotIn("lootResult", latest)
        with patch.object(progression.secrets, "randbelow", side_effect=AssertionError("rerolled")):
            self.assertEqual(self.act("kill", "B1G", event="gold-boar", revision=0), dict(latest, lootResult=receipt))
        # Historical kill acknowledgements had a NULL result; no fabricated receipt on replay.
        with self.store.connect() as db:
            db.execute("UPDATE game_actions SET result=NULL WHERE user_id=? AND action_id=?", (self.user, "gold-boar"))
        self.assertEqual(self.act("kill", "B1G", event="gold-boar", revision=0), latest)

    def test_side_quest_drops_roll_cap_persist_and_consume_on_report(self):
        other = self.store.guest(secrets.token_urlsafe(32), True)["user"]["id"]
        self.unlock_gate(self.user)
        for quest, material, npc, sources, reward, ore_reward in [
                ("ember_hearts", "ember_heartstone", "smith", ["A2"], 25, 1),
                ("broken_palisade", "sturdy_tusk", "warden", ["B1", "B1G"], 60, 2)]:
            with self.subTest(quest=quest):
                def quantity(state, definition):
                    return sum(i["quantity"] for i in state["items"] if i["definitionId"] == definition)

                with patch.object(progression.secrets, "randbelow", return_value=0):
                    self.assertEqual(quantity(self.act("kill", sources[0]), material), 0)
                accepted = self.act("accept", quest, npc)
                goal, = self.status(quest, accepted)["goals"]
                chance = progression.monster_rewards.BALANCE["questDropChance"][material]
                self.assertEqual((goal["dropFrom"], goal["dropChance"], goal["required"]), (sources, chance, 2))
                with patch.object(progression.secrets, "randbelow", return_value=0):
                    self.assertEqual(quantity(self.act("kill", "A1"), material), 0)
                    self.assertEqual(quantity(self.act("kill", sources[0], user=other), material), 0)
                with patch.object(progression.secrets, "randbelow", side_effect=[99, 99, 99, chance]):
                    self.assertEqual(quantity(self.act("kill", sources[0]), material), 0)
                with patch.object(progression.secrets, "randbelow", side_effect=[99, 99, 99, chance-1]):
                    first = self.act("kill", sources[0], event=quest+"-drop-1")
                self.assertEqual(first["lootResult"]["entries"][-1], dict(type="material", definitionId=material,
                                 name=inventory.MATERIALS[material], quantity=1, rarity="common"))
                self.store = server.Store(self.path)
                self.assertEqual(self.status(quest)["count"], 1)
                self.assertEqual(self.act("kill", sources[0], event=quest+"-drop-1", revision=0), first)
                with self.assertRaisesRegex(progression.ProgressError, "QUEST_NOT_READY"):
                    self.act("report", quest, npc)

                def finish_collection(_):
                    return self.act("kill", sources[-1], event=quest+"-drop-2", revision=first["revision"])

                with patch.object(progression.secrets, "randbelow", side_effect=[99, 99, 99, chance-1]), \
                        concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
                    second, duplicate = pool.map(finish_collection, range(2))
                self.assertEqual(second, duplicate)
                self.assertEqual((quantity(second, material), self.status(quest, second)["state"]), (2, "ready"))
                with patch.object(progression.secrets, "randbelow", return_value=0):
                    capped = self.act("kill", sources[-1])
                self.assertEqual(quantity(capped, material), 2)
                self.assertNotIn(material, [e["definitionId"] for e in capped["lootResult"]["entries"]])
                with self.assertRaisesRegex(progression.ProgressError, "WRONG_NPC"):
                    self.act("report", quest, "warden" if npc == "smith" else "smith")
                report = self.act("report", quest, npc, event=quest+"-report")
                self.assertEqual((self.status(quest, report)["state"], quantity(report, material)), ("completed", 0))
                self.assertEqual(report["gold"] - capped["gold"], reward)
                self.assertEqual(quantity(report, "ore") - quantity(capped, "ore"), ore_reward)
                self.assertEqual(self.act("report", quest, npc, event=quest+"-report", revision=0), report)
                with patch.object(progression.secrets, "randbelow", return_value=0):
                    self.assertEqual(quantity(self.act("kill", sources[-1]), material), 0)

    def test_boss_discovery_is_account_scoped_and_saved_once(self):
        b = self.store.guest(secrets.token_urlsafe(32), True)["user"]["id"]
        other = self.read(b)
        self.assertFalse(other["bossDiscovered"])
        with self.assertRaisesRegex(progression.ProgressError, "BOSS_QUEST_LOCKED"):
            self.act("discoverBoss", "ridge")
        ready = self.unlock_gate(self.user)
        self.assertEqual((ready["objective"], ready["bossDiscovered"]), ("boss", False))
        with self.assertRaisesRegex(progression.ProgressError, "INVALID_BOSS_GATE"):
            self.act("discoverBoss", "town")
        with self.assertRaisesRegex(progression.ProgressError, "STALE_REVISION"):
            self.act("discoverBoss", "ridge", event="gate", revision=ready["revision"]-1)

        def confirm_gate(_):
            return self.act("discoverBoss", "ridge", event="gate", revision=ready["revision"])

        expected = dict(ready, bossDiscovered=True, revision=ready["revision"]+1)
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            self.assertEqual(list(pool.map(confirm_gate, range(2))), [expected, expected])
        self.assertEqual(self.act("discoverBoss", "ridge", event="second-entry"), expected)
        self.store = server.Store(self.path)
        self.assertEqual(self.read(), expected)
        self.assertEqual(self.read(b), other)

    def test_instance_stats_isolation_retry_and_old_writer(self):
        prepare_test_account.prepare(self.store, self.user)
        prepare_test_account.prepare(self.store, self.user)
        s = self.read()
        self.assertEqual(s["gold"], 100)
        self.assertEqual(s["stats"], dict(hp=140, attack=19, defense=6))
        swords = [i for i in s["items"] if i["definitionId"] == "balance_sword"]
        self.assertEqual(len(swords), 2)
        self.assertEqual(sum(i["equipped"] for i in swords), 1)
        partial = self.act("unequip", "fixture-balance-sword", event="unwear")
        self.assertEqual(partial["stats"], dict(hp=128, attack=13, defense=5))
        after = self.act("equip", "fixture-balance-spare", event="replace")
        self.assertEqual(after["stats"], dict(hp=140, attack=23, defense=6))
        self.assertEqual(after, self.act("unequip", "fixture-balance-sword", revision=0, event="unwear"))
        self.assertEqual(after["gold"], 100)
        b = self.store.guest(secrets.token_urlsafe(32), True)["user"]["id"]
        with self.assertRaisesRegex(progression.ProgressError, "ITEM_NOT_OWNED"):
            self.act("equip", "fixture-balance-spare", user=b)
        with self.store.connect() as db:
            db.execute("BEGIN IMMEDIATE")
            result = progression.checkpoint(db, self.user, dict(revision=after["revision"], objective="hunt", inventory=[],
                                                                equipped={}, rewardId="old-slime", xpGain=20))
        self.assertEqual(result["items"], after["items"])
        self.assertEqual(result["gold"], after["gold"])
        with self.assertRaisesRegex(progression.ProgressError, "STALE_REVISION"):
            self.act("unequip", "fixture-balance-spare", revision=0)
        self.assertEqual(self.read()["revision"], result["revision"])

    def test_concurrent_delivery_spends_once(self):
        self.act("accept", "supplies", "warden")
        rev = self.read()["revision"]

        def report(_):
            return self.act("report", "supplies", "smith", event="same-delivery", revision=rev)
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(report, range(2)))
        self.assertEqual(results[0], results[1])
        self.assertEqual(results[0]["gold"], 15)
        self.assertEqual(results[0]["revision"], rev+1)
        self.assertEqual(results[0]["items"], [])

    def test_edited_content_partial_progress_and_goal_ids(self):
        src = Path(quests.__file__).with_name("quests.json")
        edited = json.loads(src.read_text())
        changed = dict(next(q for q in edited if q["id"] == "hunt"), id="field_test", requires=[],
                       npcId="smith", reportNpcId="warden", name="테스트 의뢰")
        edited.append(changed)
        changed["goals"] = [{"id": "first", "target": "A1", "required": 2},
                            {"id": "second", "target": "A2", "required": 1}]
        path = Path(self.temp.name)/"quests-v1.json"
        path.write_text(json.dumps(edited))
        with patch.dict(os.environ, GAME_QUEST_FILE=str(path)):
            self.act("accept", "field_test", "smith")
            self.act("kill", "A1")
            self.assertEqual(self.status("field_test")["count"], 1)
        changed.update(reportNpcId="smith")
        changed["goals"][0].update(target="B1", required=3)
        second = Path(self.temp.name)/"quests-v2.json"
        second.write_text(json.dumps(edited))
        with patch.dict(os.environ, GAME_QUEST_FILE=str(second)):
            self.store = server.Store(self.path)
            fresh = self.store.guest(secrets.token_urlsafe(32), True)["user"]["id"]
            fresh_quest = self.status("field_test", self.read(fresh))
            self.assertEqual((fresh_quest["target"], fresh_quest["required"], fresh_quest["reportNpcId"]),
                             ("B1", 3, "smith"))
            old = self.status("field_test")
            self.assertEqual((old["required"], old["count"], old["reportNpcId"]), (2, 1, "warden"))
            self.act("kill", "A1")
            self.assertEqual(self.status("field_test")["target"], "A2")
            self.act("kill", "A2")
            self.act("report", "field_test", "warden")
            self.assertEqual(self.status("field_test")["state"], "completed")
            self.act("accept", "field_test", "smith", user=fresh)
            self.act("kill", "A1", user=fresh)
            self.assertEqual(self.status("field_test", self.read(fresh))["count"], 0)
            for _ in range(3):
                self.act("kill", "B1", user=fresh)
            self.act("kill", "A2", user=fresh)
            with self.assertRaisesRegex(progression.ProgressError,"WRONG_NPC"):
                self.act("report", "field_test", "warden", user=fresh)
            result = self.act("report", "field_test", "smith", user=fresh)
            self.assertEqual(self.status("field_test", result)["state"], "completed")

    def test_prior_tutorial_actions_current_equipment_and_skill_hit(self):
        catalogue = json.loads(Path(quests.__file__).with_name("quests.json").read_text())
        def add(key, target, allow):
            goal = dict(id="first", target=target, required=1, allowExisting=allow)
            catalogue.append(dict(catalogue[0], id=key, goals=[goal], reward={}))
        add("visit_memory", "visit_smith", True)
        add("new_visit", "visit_smith", False)
        add("existing_gear", "equip", True)
        add("fresh_gear", "equip", False)
        add("accurate", "skill_hit", False)
        path = Path(self.temp.name) / "tutorials.json"
        path.write_text(json.dumps(catalogue))
        with patch.dict(os.environ, GAME_QUEST_FILE=str(path)):
            prepare_test_account.prepare(self.store, self.user)
            self.act("tutorial", "visit_smith")
            available = self.status("visit_memory")["goals"][0]
            self.assertEqual((available["allowExisting"], available["observedCount"]), (True, 1))
            for key in ("visit_memory", "new_visit", "existing_gear", "fresh_gear", "accurate"):
                self.act("accept", key, "warden")
            for key in ("visit_memory", "existing_gear"):
                self.assertEqual(self.status(key)["state"], "completed")
            for key in ("new_visit", "fresh_gear", "accurate"):
                self.assertEqual(self.status(key)["state"], "active")
            self.act("tutorial", "use_skill")
            self.assertEqual(self.status("accurate")["count"], 0)
            self.act("tutorial", "skill_hit")
            self.assertEqual(self.status("accurate")["state"], "completed")
            self.act("equip", "fixture-balance-spare")
            self.assertEqual(self.status("fresh_gear")["state"], "completed")
            self.act("tutorial", "visit_smith")
            self.assertEqual(self.status("new_visit")["state"], "completed")

    def test_content_validation_and_shared_collect_goods(self):
        catalogue = json.loads(Path(quests.__file__).with_name("quests.json").read_text())
        catalogue.append(dict(catalogue[1], id="supplies2", onAccept={}))
        path = Path(self.temp.name)/"delivery.json"
        path.write_text(json.dumps(catalogue))
        with patch.dict(os.environ, GAME_QUEST_FILE=str(path)):
            self.act("accept", "supplies", "warden")
            self.act("accept", "supplies2", "warden")
            self.assertEqual(self.status("supplies2")["state"], "ready")
            self.act("report", "supplies", "smith")
            self.assertEqual(self.status("supplies2")["state"], "active")
            with self.assertRaisesRegex(progression.ProgressError, "QUEST_NOT_READY"):
                self.act("report", "supplies2", "smith")
        for number, (field, value, reason) in enumerate([
            ("id", "watch", "id"), ("requires", ["missing"], "unknown prerequisite"),
            ("npcId", "ghost", "NPC"), ("requires", ["supplies2"], "cyclic"),
            ("goals", [dict(id="g", target="dragon", required=2)], "target"),
        ]):
            invalid = Path(self.temp.name)/f"bad-{number}.json"
            bad = [*catalogue[:-1], dict(catalogue[-1], **{field: value})]
            invalid.write_text(json.dumps(bad))
            with self.assertRaisesRegex(ValueError, reason):
                quests.load(str(invalid))

    def test_upgrade_existing_password_user_and_old_progress(self):
        old_path = Path(self.temp.name)/"old.sqlite3"
        with closing(sqlite3.connect(old_path)) as db, db:
            db.executescript("""
                CREATE TABLE users(id TEXT PRIMARY KEY,salt BLOB NOT NULL,password_hash BLOB NOT NULL,iterations INTEGER NOT NULL);
                CREATE TABLE user_progress(user_id TEXT PRIMARY KEY REFERENCES users,xp_cents INTEGER NOT NULL,objective TEXT NOT NULL,revision INTEGER NOT NULL);
                CREATE TABLE user_equipment(user_id TEXT NOT NULL REFERENCES user_progress,item_id TEXT,equipped_slot TEXT);
                CREATE TABLE user_rewards(user_id TEXT NOT NULL,reward_id TEXT NOT NULL,PRIMARY KEY(user_id,reward_id));
            """)
            salt = secrets.token_bytes(16)
            db.execute("INSERT INTO users VALUES('original',?,?,?)",
                       (salt, server.password_hash("test-password", salt), server.ITERATIONS))
            db.execute("INSERT INTO user_progress VALUES('original',250000,'boss',8)")
            db.execute("INSERT INTO user_equipment VALUES('original','weapon_1','weapon')")
            db.execute("INSERT INTO user_rewards VALUES('original','original-reward')")
        s = server.Store(old_path)
        login = s.login("original", "test-password")
        self.assertEqual(s.user_for_token(login["accessToken"]), {"id": "original"})
        with s.connect() as db:
            snap = progression.snapshot(db, "original")
            self.assertEqual((snap["totalXp"], snap["level"], snap["gold"],
                             snap["revision"], snap["objective"]), (2500, 20, 0, 8, "boss"))
            self.assertIs(snap["bossDiscovered"], False)
            self.assertEqual(snap["equipped"], dict(weapon="weapon_1"))
            self.assertTrue(snap["items"][0]["equipped"])
            self.assertEqual(db.execute("SELECT count(*) FROM user_rewards").fetchone()[0], 1)
            progression.add_xp(db, "original", 20)
            self.assertEqual(progression.snapshot(db, "original")["totalXp"], 2500)
        with server.Store(old_path).connect() as db:
            self.assertEqual(len(progression.snapshot(db, "original")["items"]), 1)
            for stage in ("boss", "complete"):
                db.execute("UPDATE user_progress SET objective=? WHERE user_id='original'", (stage,))
                snap = progression.snapshot(db, "original")
                self.assertEqual(self.status("ridge_boss", snap)["state"], "available")
            self.assertEqual(db.execute("SELECT count(*) FROM quests WHERE user_id='original'").fetchone()[0], 0)

    def test_legacy_level_progress_keeps_completion_gear_and_reward_history(self):
        path = Path(self.temp.name)/"level-schema.sqlite3"
        with closing(sqlite3.connect(path)) as db, db:
            db.executescript("""
                PRAGMA foreign_keys=ON;
                CREATE TABLE users(id TEXT PRIMARY KEY,salt BLOB,password_hash BLOB,iterations INTEGER);
                CREATE TABLE user_progress(user_id TEXT PRIMARY KEY REFERENCES users,level INTEGER NOT NULL,
                    experience REAL NOT NULL,objective TEXT NOT NULL DEFAULT 'tutorial',revision INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE user_equipment(user_id TEXT REFERENCES user_progress ON DELETE CASCADE,item_id TEXT,equipped_slot TEXT);
                CREATE TABLE user_rewards(user_id TEXT REFERENCES user_progress ON DELETE CASCADE,reward_id TEXT,PRIMARY KEY(user_id,reward_id));
                INSERT INTO users VALUES ('old-player', X'01', X'02', 1);
                INSERT INTO user_progress VALUES ('old-player', 3, 50, 'complete', 9);
                INSERT INTO user_equipment VALUES ('old-player','weapon_1','weapon');
                INSERT INTO user_rewards VALUES ('old-player','earned-before-migration');
            """)
        for _ in range(2):
            store = server.Store(path)
            with store.connect() as db:
                snap = progression.snapshot(db, "old-player")
                self.assertEqual((snap["totalXp"], snap["objective"], snap["revision"]), (300, "complete", 9))
                self.assertEqual(self.status("ridge_boss", snap)["state"], "available")
                self.assertEqual(snap["equipped"], {"weapon": "weapon_1"})
                self.assertEqual(db.execute("SELECT reward_id FROM user_rewards").fetchall(), [("earned-before-migration",)])
                self.assertEqual(db.execute("PRAGMA foreign_key_check").fetchall(), [])
            guest_id = store.guest(secrets.token_urlsafe(32), True)["user"]["id"]
            with store.connect() as db:
                self.assertEqual(progression.snapshot(db, guest_id)["totalXp"], 0)

    def test_http_guest_authorization_and_real_gameplay_commands(self):
        with patch.dict(os.environ, GAME_SERVER_KEY=secrets.token_urlsafe(32)):
            with server.make_server("127.0.0.1", 0, self.store) as srv:
                thread = threading.Thread(target=srv.serve_forever, daemon=True)
                thread.start()
                base = f"http://127.0.0.1:{srv.server_port}"

                def call(route, body=None, headers=None):
                    req = Request(base+route, data=json.dumps(body).encode() if body is not None else None,
                                  headers={"Content-Type": "application/json", **(headers or {})})
                    try:
                        response = urlopen(req, timeout=3)
                    except HTTPError as e:
                        response = e
                    with response:
                        return response.status, json.load(response)
                try:
                    seed = secrets.token_urlsafe(32)
                    status, auth = call("/auth/guest/create", {"guestToken": seed})
                    self.assertEqual(status, 200)
                    uid = auth["user"]["id"]
                    headers = {"Authorization": "Bearer "+auth["accessToken"]}
                    self.assertEqual(call("/auth/me", headers=headers), (200, {"user": {"id": uid}}))
                    own = call("/progress/me", headers=headers)[1]
                    route = "/internal/progress/"+uid
                    self.assertEqual(call(route, headers=headers)[0], 403)
                    key = {"X-Game-Server-Key": os.environ["GAME_SERVER_KEY"]}
                    self.assertEqual(call(route, headers=key)[1], own)
                    body = dict(actionId="kill-1", kind="kill", target="A1", revision=own["revision"])
                    code, done = call(route+"/action", body, key)
                    self.assertEqual((code, done["level"], done["gold"]), (200, 2, 12))
                    self.assertEqual(call(route+"/action", body, key)[1], done)
                    self.assertEqual(call(route+"/action", dict(body, actionId="kill-2"), key),
                                     (409, {"error": "STALE_REVISION"}))
                    self.assertEqual(
                        call("/auth/guest/resume", dict(guestToken=auth["guestToken"]))[1]["user"]["id"], uid)
                    self.assertEqual(call("/progress/me", headers=headers)[1],
                                     {k: v for k, v in done.items() if k != "lootResult"})
                    raid = dict(attemptId="http-raid", members=[uid])
                    self.assertEqual(call("/internal/boss/start", raid, headers)[0], 403)
                    self.assertEqual(call("/internal/boss/start", raid, key), (409, {"error":"BOSS_QUEST_LOCKED"}))
                    self.unlock_gate(uid)
                    self.act("accept", "ridge_boss", "warden", user=uid)
                    start = dict(attemptId="http-raid", eligible=[uid], cleared=False)
                    self.assertEqual(call("/internal/boss/start", raid, key), (200, start))
                    clear = dict(attemptId="http-raid")
                    self.assertEqual(call("/internal/boss/clear", clear, headers)[0], 403)
                    self.assertEqual(call("/internal/boss/clear", clear, key), (200, dict(start, cleared=True)))
                    self.assertEqual(self.status("ridge_boss", call("/progress/me", headers=headers)[1])["state"], "ready")
                finally:
                    srv.shutdown()
                    thread.join()


if __name__ == "__main__":
    unittest.main()
