"""Player-trade integrity tests. Every writer uses its own connection to a temporary DB."""
from concurrent.futures import ThreadPoolExecutor
from copy import deepcopy
import json
import os
from pathlib import Path
import secrets
import sqlite3
import tempfile
import threading
import unittest
from unittest.mock import patch
from urllib.error import HTTPError
from urllib.request import Request, urlopen

if __package__:
    from . import inventory, progression, server, trading
else:
    import inventory
    import progression
    import server
    import trading


class PlayerTrade(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.path = Path(temporary.name) / "trade.sqlite3"
        self.store = server.Store(self.path)
        alice = self.store.guest(secrets.token_urlsafe(32), True)
        self.token = alice["accessToken"]
        self.alice = alice["user"]["id"]
        self.bob = self.store.guest(secrets.token_urlsafe(32), True)["user"]["id"]
        with self.store.connect() as db:
            for user, gold, ore, refined in ((self.alice, 100, 10, 2), (self.bob, 50, 3, 4)):
                db.execute("UPDATE user_progress SET gold=? WHERE user_id=?", (gold, user))
                inventory.stack(db, user, "ore", ore)
                inventory.stack(db, user, "rare_ore", refined)
            self.sword = inventory.grant(db, self.alice, "fury_sword", "rare", hunted=True)
            self.helm = inventory.grant(db, self.bob, "balance_helm", "legendary", 3, hunted=True)

    def offer(self, user, **updates):
        return dict(dict(userId=user, revision=0, itemIds=[], gold=0, ore=0, rareOre=0), **updates)

    def deal(self):
        return dict(tradeId="deal-1", first=self.offer(self.alice, itemIds=[self.sword], gold=40, ore=5),
                    second=self.offer(self.bob, itemIds=[self.helm], rareOre=2))

    def mutate(self, operation, *args):
        with self.store.connect() as db:
            db.execute("BEGIN IMMEDIATE")
            return operation(db, *args)

    def commit(self, deal):
        return self.mutate(trading.commit, deal)

    def db_state(self):
        with self.store.connect() as db:
            db.execute("BEGIN")
            return tuple(tuple(sorted(db.execute(f"SELECT * FROM {table}").fetchall())) for table in (
                "user_progress", "item_instances", "item_stacks", "trade_receipts", "game_actions"))

    def race(self, *operations):
        barrier = threading.Barrier(len(operations))

        def start(operation):
            barrier.wait(timeout=10)
            try:
                operation()
                return "ok"
            except progression.ProgressError as error:
                return error.code

        with ThreadPoolExecutor(max_workers=len(operations)) as pool:
            futures = [pool.submit(start, operation) for operation in operations]
            return [future.result(timeout=15) for future in futures]

    def assert_rejected(self, deal, code):
        before = self.db_state()
        with self.assertRaisesRegex(progression.ProgressError, code):
            self.commit(deal)
        self.assertEqual(self.db_state(), before)

    def test_two_sided_swap_preserves_instances_balances_and_revisions(self):
        result = self.commit(self.deal())
        for state, item, enhancement, gold, ore, refined in (
                (result["first"], self.helm, 3, 60, 5, 4),
                (result["second"], self.sword, 0, 90, 8, 2)):
            self.assertEqual((state["revision"], state["gold"]), (1, gold))
            items = {i["instanceId"]: i for i in state["items"]}
            self.assertEqual(set(items), {item, "stack:ore", "stack:rare_ore"})
            self.assertEqual(items[item]["enhancement"], enhancement)
            self.assertEqual((items["stack:ore"]["quantity"], items["stack:rare_ore"]["quantity"]), (ore, refined))
        with self.store.connect() as db:
            self.assertEqual(db.execute("SELECT count(*) FROM item_instances WHERE hunted=1").fetchone()[0], 2)

    def test_concurrent_duplicate_then_replay_after_restart_returns_current_snapshots(self):
        deal = self.deal()
        with self.store.connect() as db:
            other = inventory.grant(db, self.alice, "vital_sword", hunted=True)
        deal["first"]["itemIds"].append(other)
        self.assertEqual(self.race(*[lambda: self.commit(deal)] * 6), ["ok"] * 6)
        self.mutate(progression.action, self.bob, dict(actionId="equip-after", revision=1, kind="equip", target=other))
        self.store = server.Store(self.path)
        before = self.db_state()
        replay = deepcopy(deal)
        replay["first"]["itemIds"].reverse()
        replay["first"], replay["second"] = replay["second"], replay["first"]
        result = self.commit(replay)
        self.assertEqual((result["first"]["revision"], result["second"]["revision"]), (2, 1))
        self.assertTrue(next(i for i in result["first"]["items"] if i["instanceId"] == other)["equipped"])
        self.assertEqual(self.db_state(), before)
        for key, value in (("gold", 41), ("revision", 1), ("userId", "another-user")):
            changed = deepcopy(deal)
            changed["first"][key] = value
            self.assert_rejected(changed, "TRADE_ID_CONFLICT")

    def test_two_competing_buyers_cannot_spend_same_assets(self):
        buyer = self.store.guest(secrets.token_urlsafe(32), True)["user"]["id"]
        gift = self.offer(self.alice, gold=100, itemIds=[self.sword], ore=10)
        one = dict(tradeId="to-bob", first=gift, second=self.offer(self.bob))
        two = dict(tradeId="to-other", first=self.offer(buyer), second=gift)
        self.assertCountEqual(self.race(lambda: self.commit(one), lambda: self.commit(two)), ["ok", "STALE_REVISION"])
        with self.store.connect() as db:
            self.assertEqual(db.execute("SELECT sum(gold),sum(revision) FROM user_progress").fetchone(), (150, 2))
            self.assertEqual(db.execute("SELECT count(*) FROM trade_receipts").fetchone()[0], 1)
            self.assertEqual(db.execute("SELECT sum(quantity) FROM item_stacks WHERE definition='ore'").fetchone()[0], 13)
            owners = list(db.execute("SELECT user_id FROM item_instances WHERE instance_id=?", (self.sword,)))
            self.assertEqual(len(owners), 1)
            self.assertIn(owners[0][0], (buyer, self.bob))

    def race_with_inventory_action(self, kind, npc=""):
        deal = self.deal()
        event = dict(actionId="other-action", kind=kind, target=self.sword, revision=0, npcId=npc)
        results = self.race(lambda: self.commit(deal), lambda: self.mutate(progression.action, self.alice, event))
        self.assertCountEqual(results, ["ok", "STALE_REVISION"])
        with self.store.connect() as db:
            owners = list(db.execute("SELECT user_id,equipped_slot,enhancement FROM item_instances WHERE instance_id=?",
                                     (self.sword,)))
            if results[0] == "ok":
                self.assertEqual(owners, [(self.bob, None, 0)])
            elif kind == "sell":
                self.assertEqual(owners, [])
            else:
                self.assertEqual(owners, [(self.alice, "sword" if kind == "equip" else None, int(kind == "enhance"))])
            self.assertEqual(db.execute("SELECT count(*) FROM trade_receipts").fetchone()[0], int(results[0] == "ok"))

    def test_race_with_equip(self):
        self.race_with_inventory_action("equip")

    def test_race_with_enhancement(self):
        self.race_with_inventory_action("enhance", "smith")

    def test_race_with_captain_sale(self):
        self.race_with_inventory_action("sell", "warden")

    def test_stale_insufficient_and_overflow_never_modify_either_party(self):
        for key, value, code in (("revision", 1, "STALE_REVISION"), ("gold", 51, "INSUFFICIENT_GOLD"),
                                 ("ore", 4, "INSUFFICIENT_MATERIALS"), ("rareOre", 5, "INSUFFICIENT_MATERIALS")):
            deal = self.deal()
            deal["second"][key] = value
            self.assert_rejected(deal, code)
        with self.store.connect() as db:
            db.execute("UPDATE item_stacks SET quantity=? WHERE user_id=? AND definition='ore'",
                       (trading.MAX_AMOUNT, self.bob))
        self.assert_rejected(self.deal(), "BALANCE_LIMIT")
        with self.store.connect() as db:
            db.execute("UPDATE user_progress SET gold=? WHERE user_id=?", (trading.MAX_AMOUNT, self.bob))
        deal = self.deal()
        deal["first"]["ore"] = 0
        self.assert_rejected(deal, "BALANCE_LIMIT")

    def test_item_eligibility_foreign_items_and_per_account_key_collision(self):
        with self.store.connect() as db:
            worn = inventory.grant(db, self.bob, "fury_armor", equipped=True, hunted=True)
            reward = inventory.grant(db, self.bob, inventory.RIDGE_SWORD)
            starter = inventory.grant(db, self.bob, "balance_sword")
        for invalid in (worn, reward, starter, self.sword, "not-owned", "stack:parcel"):
            deal = self.deal()
            deal["second"]["itemIds"] = [invalid]
            self.assert_rejected(deal, "ITEM_UNAVAILABLE")
        with self.store.connect() as db:
            inventory.grant(db, self.bob, "vital_armor", instance=self.sword, hunted=True)
        self.assert_rejected(self.deal(), "ITEM_UNAVAILABLE")

    def test_malformed_empty_self_and_unknown_user(self):
        for key, value in (("gold", True), ("ore", -1), ("rareOre", trading.MAX_AMOUNT+1),
                           ("itemIds", [self.sword]*2), ("itemIds", list("abcde")),
                           ("itemIds", [None]), ("questItem", 1)):
            deal = self.deal()
            deal["first"][key] = value
            self.assert_rejected(deal, "INVALID_TRADE")
        self.assert_rejected(dict(tradeId="empty", first=self.offer(self.alice), second=self.offer(self.bob)), "INVALID_TRADE")
        deal = self.deal()
        deal["second"]["userId"] = self.alice
        self.assert_rejected(deal, "INVALID_TRADE")
        deal["second"]["userId"] = "missing-account"
        self.assert_rejected(deal, "UNKNOWN_USER")

    def test_failure_writing_final_receipt_rolls_back_both_sides_and_can_retry(self):
        with self.store.connect() as db:
            db.execute("""CREATE TRIGGER fail_receipt BEFORE INSERT ON trade_receipts
                BEGIN SELECT RAISE(ABORT,'simulated storage failure'); END""")
        before = self.db_state()
        with self.assertRaisesRegex(sqlite3.IntegrityError, "simulated storage failure"):
            self.commit(self.deal())
        self.assertEqual(self.db_state(), before)
        with self.store.connect() as db:
            db.execute("DROP TRIGGER fail_receipt")
        self.assertEqual(self.commit(self.deal())["second"]["gold"], 90)

    def test_http_requires_game_server_key_and_commits_once(self):
        with patch.dict(os.environ, GAME_SERVER_KEY=secrets.token_urlsafe(32)):
            with server.make_server("127.0.0.1", 0, self.store) as http:
                worker = threading.Thread(target=http.serve_forever, daemon=True)
                worker.start()
                try:
                    route = f"http://127.0.0.1:{http.server_port}/internal/trade/commit"
                    def post(headers):
                        request = Request(route, data=json.dumps(self.deal()).encode(),
                                          headers={"Content-Type": "application/json", **headers})
                        try:
                            response = urlopen(request, timeout=3)
                        except HTTPError as error:
                            response = error
                        with response:
                            return response.status, json.load(response)
                    denied = (403, {"error": "SERVER_KEY_REQUIRED"})
                    self.assertEqual(post({}), denied)
                    self.assertEqual(post({"Authorization": "Bearer "+self.token}), denied)
                    key = {"X-Game-Server-Key": os.environ["GAME_SERVER_KEY"]}
                    status, body = post(key)
                    self.assertEqual((status, body["first"]["revision"], body["second"]["gold"]), (200, 1, 90))
                    self.assertEqual(post(key), (status, body))
                finally:
                    http.shutdown()
                    worker.join()


if __name__ == "__main__":
    unittest.main()
