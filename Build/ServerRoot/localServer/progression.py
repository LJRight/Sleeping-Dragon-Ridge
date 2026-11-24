"""Account snapshots and atomic, idempotent game-server commands."""
from bisect import bisect_right
import hashlib
import json
import re
import secrets

if __package__:
    from . import inventory, monster_rewards, quests
else:
    import inventory
    import monster_rewards
    import quests

ProgressError = quests.RuleError
OLD_THRESHOLDS = (0, 100, 200, 400, 600, 900, 1200, 1600, 2000, 2500)
THRESHOLDS = (0, 20, 60, 110, 160, 230, 300, 370, 460, 550, 660,
              770, 880, 1020, 1160, 1320, 1480, 1640, 1840, 2040)
CAP = THRESHOLDS[-1] * 100
OBJECTIVES = quests.OBJECTIVES
EVENT_ID = re.compile(r"[A-Za-z0-9_.:-]{1,96}\Z")


def initialize(db):
    columns = {row[1] for row in db.execute("PRAGMA table_info(user_progress)")}
    old_growth = {"level", "experience"} <= columns
    db.execute("""CREATE TABLE IF NOT EXISTS user_progress (
        user_id TEXT PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
        xp_cents INTEGER NOT NULL DEFAULT 0 CHECK(xp_cents>=0),
        objective TEXT NOT NULL DEFAULT 'tutorial' CHECK(objective IN ('tutorial','hunt','boss','complete')),
        revision INTEGER NOT NULL DEFAULT 0 CHECK(revision>=0),
        gold INTEGER NOT NULL DEFAULT 0 CHECK(gold>=0),
        boss_discovered INTEGER NOT NULL DEFAULT 0 CHECK(boss_discovered IN (0,1)))""")
    if columns and "objective" not in columns:
        db.execute("ALTER TABLE user_progress ADD COLUMN objective TEXT NOT NULL DEFAULT 'tutorial'")
    if columns and "revision" not in columns:
        db.execute("ALTER TABLE user_progress ADD COLUMN revision INTEGER NOT NULL DEFAULT 0 CHECK(revision>=0)")
    if columns and "gold" not in columns:
        db.execute("ALTER TABLE user_progress ADD COLUMN gold INTEGER NOT NULL DEFAULT 0 CHECK(gold>=0)")
    if columns and "boss_discovered" not in columns:
        db.execute("ALTER TABLE user_progress ADD COLUMN boss_discovered INTEGER NOT NULL DEFAULT 0 "
                   "CHECK(boss_discovered IN (0,1))")
    if old_growth and "xp_cents" not in columns:
        # Convert in place: renaming/dropping the parent would also retarget its equipment and reward foreign keys.
        db.execute("ALTER TABLE user_progress ADD COLUMN xp_cents INTEGER NOT NULL DEFAULT 0 CHECK(xp_cents>=0)")
        for user, level, pct in db.execute("SELECT user_id,level,experience FROM user_progress").fetchall():
            if not (1 <= level <= 10 and 0 <= pct <= 100):
                raise ValueError("Legacy level or XP percentage out of range")
            base = OLD_THRESHOLDS[level-1]
            span = OLD_THRESHOLDS[min(level, 9)]-base
            db.execute("UPDATE user_progress SET xp_cents=? WHERE user_id=?", (base*100+round(span*pct), user))
    # Older schemas can require explicit values for the retired columns when a new account is created.
    insert_fields = "user_id,level,experience" if old_growth else "user_id"
    initial_values = ",1,0" if old_growth else ""
    db.execute(f"INSERT OR IGNORE INTO user_progress({insert_fields}) SELECT id{initial_values} FROM users")
    needs_hunt_origins = inventory.initialize(db)
    if db.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name='user_equipment'").fetchone():
        for user, item, slot in db.execute("SELECT user_id,item_id,equipped_slot FROM user_equipment"):
            inventory.grant(db, user, inventory.LEGACY[item], instance="legacy:"+item, equipped=bool(slot))
        db.execute("DROP TABLE user_equipment")
    db.execute("""CREATE TABLE IF NOT EXISTS user_rewards (
        user_id TEXT NOT NULL REFERENCES user_progress ON DELETE CASCADE, reward_id TEXT NOT NULL,
        PRIMARY KEY(user_id,reward_id))""")
    db.execute("""CREATE TABLE IF NOT EXISTS game_actions (
        user_id TEXT NOT NULL REFERENCES user_progress ON DELETE CASCADE, action_id TEXT NOT NULL,
        fingerprint TEXT NOT NULL, result TEXT, PRIMARY KEY(user_id,action_id))""")
    if "result" not in {column[1] for column in db.execute("PRAGMA table_info(game_actions)")}:
        db.execute("ALTER TABLE game_actions ADD COLUMN result TEXT")
    db.execute("""CREATE TABLE IF NOT EXISTS boss_attempts (
        attempt_id TEXT PRIMARY KEY, members TEXT NOT NULL, eligible TEXT NOT NULL,
        cleared INTEGER NOT NULL DEFAULT 0 CHECK(cleared IN (0,1)))""")
    db.execute("""CREATE TABLE IF NOT EXISTS trade_receipts (
        trade_id TEXT PRIMARY KEY, fingerprint TEXT NOT NULL)""")
    quests.initialize(db)
    if needs_hunt_origins:
        inventory.recover_hunt_origins(db)
    return f"INSERT INTO user_progress({insert_fields}) VALUES (?{initial_values})"


def growth(cents):
    level = bisect_right(THRESHOLDS, cents//100)
    pct = 100.0 if level == len(THRESHOLDS) else (
        cents-THRESHOLDS[level-1]*100)/(THRESHOLDS[level]-THRESHOLDS[level-1])
    return {"level": level, "experience": round(pct, 2), "totalXp": cents/100}


def add_xp(db, user, gain):
    if gain:
        # Preserve already-earned points when migrating accounts above the revised cap.
        db.execute("UPDATE user_progress SET xp_cents=max(xp_cents,min(?,xp_cents+?)) WHERE user_id=?",
                   (CAP, gain*100, user))


def progress_row(db, user):
    row = db.execute("SELECT xp_cents,objective,revision,gold,boss_discovered FROM user_progress WHERE user_id=?",
                     (user,)).fetchone()
    if row is None:
        raise ProgressError(404, "UNKNOWN_USER")
    return row


def snapshot(db, user):
    cents, objective, revision, gold, boss_discovered = progress_row(db, user)
    progress = growth(cents)
    items, stats = inventory.equipment_snapshot(db, user, progress["level"])
    starter = quests.starter_sword(db, user)
    legacy = [(i["instanceId"][7:], i["equipped"]) for i in items if i["instanceId"].startswith("legacy:")]
    return dict(progress, levelXpThresholds=list(THRESHOLDS), objective=objective, revision=revision, gold=gold, bossDiscovered=bool(boss_discovered),
                items=items, stats=stats, starterSwordInstanceId=starter[0] if starter else "",
                quests=[view for _, view in quests.current(db, user, progress["level"]).values()],
                inventory=[k for k, _ in legacy], equipped={k.split("_")[0]: k for k, equipped in legacy if equipped})


def snapshot_after_action(db, user):
    state = snapshot(db, user)
    # The last hunt may be reported before the final level, or after earning it.
    if state["objective"] not in ("boss", "complete") and any(
            q["id"] == quests.BOSS_QUEST and q["state"] != "locked" for q in state["quests"]):
        db.execute("UPDATE user_progress SET objective='boss' WHERE user_id=?", (user,))
        state["objective"] = "boss"
    return state


def kill(db, user, target, event_id):
    reward = monster_rewards.BALANCE["field"].get(target)
    if reward is None:
        raise ProgressError(400, "UNKNOWN_MONSTER")
    if not db.execute("INSERT OR IGNORE INTO user_rewards VALUES (?,?)", (user, event_id)).rowcount:
        raise ProgressError(409, "REWARD_ALREADY_APPLIED")
    gold = reward["gold"]
    add_xp(db, user, reward["xp"])
    db.execute("UPDATE user_progress SET gold=gold+? WHERE user_id=?", (gold, user))
    entries = [dict(type="gold", definitionId="gold", name="골드", quantity=gold, rarity="")] if gold else []
    roll = secrets.randbelow(100)
    for rarity, key in zip(inventory.RARITIES, monster_rewards.GEAR_CHANCES):
        chance = reward[key]
        if roll < chance:
            definition = secrets.choice(inventory.FIELD_GEAR)
            inventory.grant(db, user, definition, rarity, hunted=True)
            entries.append(dict(type="equipment", definitionId=definition, name=inventory.item_name(definition),
                                quantity=1, rarity=rarity))
            break
        roll -= chance
    materials = [("ore", reward["oreCount"], reward["oreChance"]),
                 ("rare_ore", reward["refinedOreCount"], reward["refinedOreChance"])]
    materials.extend((item, 1, chance) for item, chance in quests.collection_drops(db, user, target).items())
    for material, amount, chance in materials:
        if secrets.randbelow(100) < chance and amount:
            inventory.stack(db, user, material, amount)
            entries.append(dict(type="material", definitionId=material, name=inventory.MATERIALS[material],
                                quantity=amount, rarity="common"))
    quests.event(db, user, "kill", target, add_xp)
    return dict(actionId=event_id, entries=entries)


def action(db, user, data):
    # Caller holds BEGIN IMMEDIATE. The operation, effects and acknowledgement commit together.
    event_id, revision = data.get("actionId"), data.get("revision")
    kind, target, npc = data.get("kind"), data.get("target"), data.get("npcId") or ""
    if (set(data)-{"actionId", "revision", "kind", "target", "npcId"} or
            not isinstance(event_id, str) or not EVENT_ID.fullmatch(event_id) or
            type(revision) is not int or revision < 0 or not isinstance(kind, str) or
            not isinstance(target, str) or not target or len(target) > 96 or not isinstance(npc, str) or len(npc) > 64):
        raise ProgressError(400, "INVALID_ACTION")
    cents, objective, current, _, boss_discovered = progress_row(db, user)
    signature = hashlib.sha256(json.dumps([kind, target, npc]).encode()).hexdigest()
    result_key = "lootResult" if kind == "kill" else "enhanceResult"
    prior = db.execute("SELECT fingerprint,result FROM game_actions WHERE user_id=? AND action_id=?",
                       (user, event_id)).fetchone()
    if prior:
        if prior[0] != signature:
            raise ProgressError(409, "ACTION_ID_REUSED")
        response = snapshot(db, user)
        if prior[1]:
            response[result_key] = json.loads(prior[1])
        return response
    if revision != current:
        raise ProgressError(409, "STALE_REVISION")
    changed = True
    result = None
    if kind == "discoverBoss":
        if target != "ridge" or npc:
            raise ProgressError(400, "INVALID_BOSS_GATE")
        if objective not in ("boss", "complete"):
            raise ProgressError(409, "BOSS_QUEST_LOCKED")
        changed = not boss_discovered
        if changed:
            db.execute("UPDATE user_progress SET boss_discovered=1 WHERE user_id=?", (user,))
    elif kind == "kill":
        result = kill(db, user, target, event_id)
    elif kind in ("accept", "report"):
        quests.interact(db, user, kind, target, npc, growth(cents)["level"], add_xp)
    elif kind in ("equip", "unequip"):
        error = inventory.change_slot(db, user, target, kind == "equip")
        if error:
            raise ProgressError(409, error)
        if kind == "equip":
            quests.event(db, user, "tutorial", "equip", add_xp)
            if db.execute("SELECT 1 FROM item_instances WHERE user_id=? AND instance_id=? AND equipped_slot='sword'",
                          (user, target)).fetchone():
                quests.event(db, user, "tutorial", "equip_sword", add_xp)
    elif kind == "sell":
        if npc != "warden":
            raise ProgressError(409, "WRONG_NPC")
        error = inventory.sell(db, user, target)
        if error:
            raise ProgressError(409, error)
    elif kind in ("enhance", "temper"):
        if npc != "smith":
            raise ProgressError(409, "WRONG_NPC")
        starter = quests.starter_sword(db, user)
        first_upgrade = bool(starter and starter == (target, 0))
        if kind == "temper":
            temper = quests.current(db, user, growth(cents)["level"]).get("first_temper")
            if not temper or temper[1]["state"] != "active" or temper[1]["target"] != "temper_starter_sword":
                raise ProgressError(409, "TEMPER_QUEST_NOT_ACTIVE")
            if not first_upgrade:
                raise ProgressError(409, "STARTER_SWORD_REQUIRED")
        result, error = inventory.enhance(db, user, target)
        if error:
            raise ProgressError(409, error)
        result["actionId"] = event_id
        if result["outcome"] == "success":
            quests.event(db, user, "tutorial", "enhance", add_xp)
            if first_upgrade and result["after"] == 1:
                quests.event(db, user, "tutorial", "temper_starter_sword", add_xp)
    elif kind == "tutorial":
        if target == "equip_sword":
            raise ProgressError(400, "EQUIP_ACTION_REQUIRED")
        if target in ("enhance", "temper_starter_sword"):
            raise ProgressError(400, "ENHANCE_ACTION_REQUIRED")
        quests.event(db, user, kind, target, add_xp)
    else:
        raise ProgressError(400, "UNKNOWN_ACTION")
    db.execute("INSERT INTO game_actions(user_id,action_id,fingerprint,result) VALUES(?,?,?,?)",
               (user, event_id, signature, json.dumps(result) if result else None))
    if changed:
        db.execute("UPDATE user_progress SET revision=revision+1 WHERE user_id=?", (user,))
    response = snapshot_after_action(db, user)
    if result:
        response[result_key] = result
    return response


def boss_attempt(db, operation, data):
    """The caller holds a write transaction. Only the trusted combat server starts and clears attempts."""
    attempt_id = data.get("attemptId")
    fields = {"attemptId", "members"} if operation == "start" else {"attemptId"}
    if set(data) != fields or not isinstance(attempt_id, str) or not EVENT_ID.fullmatch(attempt_id):
        raise ProgressError(400, "INVALID_ATTEMPT")
    record = db.execute("SELECT members,eligible,cleared FROM boss_attempts WHERE attempt_id=?", (attempt_id,)).fetchone()
    if operation == "start":
        members = data["members"]
        if (not isinstance(members, list) or not 1 <= len(members) <= 4 or
                not all(isinstance(m, str) and re.fullmatch(r"[A-Za-z0-9_-]{3,32}", m) for m in members)
                or len(set(members)) != len(members)):
            raise ProgressError(400, "INVALID_MEMBERS")
        members = sorted(members)
        if record and members != json.loads(record[0]):
            raise ProgressError(409, "ATTEMPT_ID_REUSED")
        if not record:
            eligible = []
            for user in members:
                cents, objective, _, _, _ = progress_row(db, user)
                if objective not in ("boss", "complete"):
                    raise ProgressError(409, "BOSS_QUEST_LOCKED")
                if db.execute("SELECT 1 FROM item_instances WHERE user_id=? AND definition=? AND equipped_slot='sword'",
                              (user, inventory.RIDGE_SWORD)).fetchone():
                    raise ProgressError(409, "RIDGE_SWORD_EQUIPPED")
                quest = quests.current(db, user, growth(cents)["level"]).get(quests.BOSS_QUEST)
                if quest and quest[1]["state"] == "active":
                    eligible.append(user)
            db.execute("INSERT INTO boss_attempts(attempt_id,members,eligible) VALUES(?,?,?)",
                       (attempt_id, json.dumps(members), json.dumps(eligible)))
            return dict(attemptId=attempt_id, eligible=eligible, cleared=False)
    if not record:
        raise ProgressError(404, "UNKNOWN_ATTEMPT")
    eligible = json.loads(record[1])
    cleared = bool(record[2])
    if operation == "clear" and not cleared:
        for user in eligible:
            cents, *_ = progress_row(db, user)
            quest = quests.current(db, user, growth(cents)["level"]).get(quests.BOSS_QUEST)
            if quest and quest[1]["state"] == "active":
                quests.event(db, user, "kill", quests.BOSS_QUEST, add_xp)
                db.execute("UPDATE user_progress SET revision=revision+1 WHERE user_id=?", (user,))
        db.execute("UPDATE boss_attempts SET cleared=1 WHERE attempt_id=?", (attempt_id,))
        cleared = True
    return dict(attemptId=attempt_id, eligible=eligible, cleared=cleared)


def checkpoint(db, user, data):
    """Compatibility adapter for old servers; legacy writers only own legacy instances."""
    if set(data)-{"revision", "xpGain", "rewardId", "objective", "inventory", "equipped"}:
        raise ProgressError(400, "INVALID_CHECKPOINT")
    revision, gain, reward = data.get("revision"), data.get("xpGain", 0), data.get("rewardId")
    objective, items, equipped = data.get("objective"), data.get("inventory"), data.get("equipped")
    if not (type(revision) is int and revision >= 0 and type(gain) is int and 0 <= gain <= 2500 and
            (reward is None or isinstance(reward, str) and EVENT_ID.fullmatch(reward)) and (not gain or reward) and
            objective in OBJECTIVES and isinstance(items, list) and all(
                isinstance(i, str) and i in inventory.LEGACY for i in items)
            and len(set(items)) == len(items) and isinstance(equipped, dict)
            and all(isinstance(i, str) and i in items and i.split("_")[0] == s for s, i in equipped.items())):
        raise ProgressError(400, "INVALID_CHECKPOINT")
    if revision != progress_row(db, user)[2]:
        raise ProgressError(409, "STALE_REVISION")
    if reward and not db.execute("INSERT OR IGNORE INTO user_rewards VALUES(?,?)", (user, reward)).rowcount:
        raise ProgressError(409, "REWARD_ALREADY_APPLIED")
    # Only the old inventory subset can be replaced by an old writer. Preserved modern slots take precedence.
    modern_slots = {s for s, in db.execute("""SELECT equipped_slot FROM item_instances
        WHERE user_id=? AND equipped_slot IS NOT NULL AND instance_id NOT LIKE 'legacy:%'""", (user,))}
    legacy_stats = {i: (r, n) for i, r, n in db.execute("""SELECT instance_id,rarity,enhancement FROM item_instances
        WHERE user_id=? AND instance_id LIKE 'legacy:%'""", (user,))}
    db.execute("DELETE FROM item_instances WHERE user_id=? AND instance_id LIKE 'legacy:%'", (user,))
    for item in items:
        definition = inventory.LEGACY[item]
        slot = definition.split("_")[1]
        wear = equipped.get(item.split("_")[0]) == item
        if wear and slot in modern_slots:
            raise ProgressError(409, "EQUIPMENT_STATE_CHANGED")
        instance = "legacy:"+item
        rarity, enhancement = legacy_stats.get(instance, ("common", 0))
        inventory.grant(db, user, definition, rarity, enhancement, instance=instance, equipped=wear)
    add_xp(db, user, gain)
    if db.execute("SELECT 1 FROM quests WHERE user_id=? AND quest_id=? AND completed=1",
                  (user, quests.BOSS_QUEST)).fetchone():
        objective = "complete"
    db.execute("UPDATE user_progress SET objective=?,revision=revision+1 WHERE user_id=?", (objective, user))
    return snapshot_after_action(db, user)
