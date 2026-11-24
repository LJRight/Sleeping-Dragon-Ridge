"""Data-driven quest definitions and immutable contracts for quests already accepted."""
import json
import os
import re
from functools import lru_cache
from pathlib import Path

if __package__:
    from . import inventory, monster_rewards
else:
    import inventory
    import monster_rewards

NPCS = {"warden", "smith"}
OBJECTIVES = ("tutorial", "hunt", "boss", "complete")
BOSS_QUEST = "ridge_boss"
MONSTERS = monster_rewards.FIELD_IDS | {BOSS_QUEST}
TUTORIALS = {"open_inventory", "move", "attack_hit", "equip", "equip_sword", "visit_warden", "visit_smith",
             "use_skill", "skill_hit", "enhance", "temper_starter_sword", "use_potion"}
ID = re.compile(r"[A-Za-z0-9_-]{1,64}\Z")


class RuleError(Exception):
    def __init__(self, status, code):
        super().__init__(code)
        self.status, self.code = status, code


@lru_cache(maxsize=8)
def load(path):
    rows = json.loads(Path(path).read_text(encoding="utf-8"))
    if not isinstance(rows, list):
        raise ValueError(f"{path}: expected quest array")
    catalogue = {}
    for row in rows:
        quest_id = row.get("id") if isinstance(row, dict) else None

        def require(ok, field):
            if not ok:
                raise ValueError(f"{path}: quest {quest_id!r}, invalid {field}")
        require(isinstance(quest_id, str) and ID.fullmatch(quest_id) and quest_id not in catalogue, "id")
        require(row.get("category") in ("main", "side"), "category")
        require(row.get("type") in ("collect", "kill", "tutorial"), "type")
        require(row.get("completion") == "report" or
                row.get("completion") == "immediate" and row["type"] == "tutorial", "completion")
        require("nextObjective" not in row or row["nextObjective"] in OBJECTIVES, "nextObjective")
        require(all(isinstance(row.get(k), str) and row[k] in NPCS for k in ("npcId", "reportNpcId")), "NPC reference")
        require(type(row.get("minLevel")) is int and 1 <= row["minLevel"] <= 20, "minLevel")
        require(all(isinstance(row.get(k), str) and row[k]
                for k in ("name", "dialogue", "instruction")), "display text")
        require(isinstance(row.get("requires"), list) and all(isinstance(i, str)
                for i in row["requires"]), "requires")
        goals = row.get("goals")
        require(isinstance(goals, list) and goals, "goals")
        ids, targets = set(), set()
        domain = {"collect": inventory.MATERIALS, "kill": MONSTERS, "tutorial": TUTORIALS}[row["type"]]
        for g in goals:
            require(isinstance(g, dict) and isinstance(g.get("id"), str) and ID.fullmatch(g["id"])
                    and g["id"] not in ids, "goal ID")
            aliases = g.get("alsoTargets", [])
            require(isinstance(aliases, list) and (not aliases or row["type"] == "kill"), "alsoTargets")
            matched = [g.get("target"), *aliases]
            require(all(isinstance(t, str) and t in domain for t in matched)
                    and len(set(matched)) == len(matched) and targets.isdisjoint(matched), f"goal {g['id']} target")
            require(type(g.get("required")) is int and 1 <= g["required"] <= 9999, f"goal {g['id']} count")
            require(type(g.get("allowExisting", False)) is bool, "allowExisting")
            require(isinstance(g.get("label", ""), str), f"goal {g['id']} label")
            if "dropChanceRef" in g:
                reference = g.pop("dropChanceRef")
                require(reference == g["target"] and reference in monster_rewards.BALANCE["questDropChance"]
                        and "dropChance" not in g, "dropChanceRef")
                g["dropChance"] = monster_rewards.BALANCE["questDropChance"][reference]
            if "dropFrom" in g or "dropChance" in g:
                sources = g.get("dropFrom")
                require(row["type"] == "collect" and isinstance(sources, list) and sources and
                        all(isinstance(s, str) and s in MONSTERS and s != BOSS_QUEST for s in sources)
                        and len(sources) == len(set(sources)), "dropFrom")
                require(type(g.get("dropChance")) is int and 1 <= g["dropChance"] <= 100, "dropChance")
            ids.add(g["id"])
            targets.update(matched)
        if "rewardRef" in row:
            require(row.pop("rewardRef") == "bossReport" and quest_id == BOSS_QUEST, "rewardRef")
            payout = monster_rewards.BALANCE["bossReport"]
            reward = row.setdefault("reward", {})
            require(isinstance(reward, dict) and not {"xp", "gold", "materials"}.intersection(reward), "rewardRef")
            reward.update(xp=payout["xp"], gold=payout["gold"], materials={
                material: payout[count] for material, count in (("ore", "oreCount"), ("rare_ore", "refinedOreCount"))
                if payout[count]})
        for field in ("onAccept", "reward"):
            grant = row.get(field, {})
            require(isinstance(grant, dict) and not set(grant)-{"xp", "gold", "items", "materials"}, field)
            require(all(type(grant.get(k, 0)) is int and 0 <= grant.get(
                k, 0) <= 100000 for k in ("gold", "xp")), field)
            require(isinstance(grant.get("items", []), list) and all(
                isinstance(k, str) and k in inventory.BASE for k in grant.get("items", [])), field)
            require(isinstance(grant.get("materials", {}), dict) and all(k in inventory.MATERIALS and type(v) is int
                    and 0 < v <= 9999 for k, v in grant.get("materials", {}).items()), field)
        catalogue[quest_id] = row
    visited, visiting = set(), set()

    def visit(q):
        if q in visiting:
            raise ValueError(f"{path}: cyclic prerequisite at {q}")
        if q in visited:
            return
        visiting.add(q)
        for p in catalogue[q]["requires"]:
            if p not in catalogue:
                raise ValueError(f"{path}: quest {q} unknown prerequisite {p}")
            visit(p)
        visiting.remove(q)
        visited.add(q)
    for q in catalogue:
        visit(q)
    return catalogue


def definitions():
    return load(os.environ.get("GAME_QUEST_FILE", str(Path(__file__).with_name("quests.json"))))


def initialize(db):
    definitions()
    db.execute("""CREATE TABLE IF NOT EXISTS quests (
        user_id TEXT NOT NULL REFERENCES user_progress ON DELETE CASCADE, quest_id TEXT NOT NULL,
        definition TEXT NOT NULL, completed INTEGER NOT NULL DEFAULT 0, counts TEXT NOT NULL DEFAULT '{}', starter_sword_id TEXT,
        PRIMARY KEY(user_id,quest_id))""")
    if "starter_sword_id" not in {column[1] for column in db.execute("PRAGMA table_info(quests)")}:
        db.execute("ALTER TABLE quests ADD COLUMN starter_sword_id TEXT")
        # Old watch rewards have no origin field. Bind the earliest matching common sword once.
        db.execute("""UPDATE quests SET starter_sword_id=(
            SELECT i.instance_id FROM item_instances i WHERE i.user_id=quests.user_id
                AND i.definition='balance_sword' AND i.rarity='common' AND i.instance_id NOT LIKE 'legacy:%'
            ORDER BY i.rowid LIMIT 1)
            WHERE quest_id='watch' AND completed=1 AND EXISTS (
                SELECT 1 FROM json_each(quests.definition, '$.reward.items') WHERE value='balance_sword')""")
    db.execute("""CREATE TABLE IF NOT EXISTS tutorial_history (
        user_id TEXT NOT NULL REFERENCES user_progress ON DELETE CASCADE, target TEXT NOT NULL,
        count INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(user_id,target))""")


def starter_sword(db, user):
    return db.execute("""SELECT i.instance_id, i.enhancement FROM quests q JOIN item_instances i
        ON i.user_id=q.user_id AND i.instance_id=q.starter_sword_id
        WHERE q.user_id=? AND q.quest_id='watch' AND i.definition='balance_sword'""", (user,)).fetchone()


def current(db, user, level):
    saved = {key: (json.loads(d), bool(done), json.loads(counts)) for key, d, done, counts in db.execute(
        "SELECT quest_id,definition,completed,counts FROM quests WHERE user_id=?", (user,))}
    known = dict(definitions())
    known.update({k: r[0] for k, r in saved.items()})
    stacks = dict(db.execute("SELECT definition,quantity FROM item_stacks WHERE user_id=?", (user,)))
    observed = dict(db.execute("SELECT target,count FROM tutorial_history WHERE user_id=?", (user,)))
    equipped = {slot for slot, in db.execute(
        "SELECT equipped_slot FROM item_instances WHERE user_id=? AND equipped_slot IS NOT NULL", (user,))}
    observed["equip"] = len(equipped)
    observed["equip_sword"] = max(observed.get("equip_sword", 0), int("sword" in equipped))
    starter = starter_sword(db, user)
    if starter and starter[1] >= 1:
        observed["temper_starter_sword"] = max(1, observed.get("temper_starter_sword", 0))
    objective, = db.execute("SELECT objective FROM user_progress WHERE user_id=?", (user,)).fetchone()
    if "first_temper" in known and "first_temper" not in saved:
        past_lesson = ("equip_sword" in saved or objective in ("boss", "complete")
                       or any(row["category"] == "main" and row["type"] == "kill" for row, _, _ in saved.values()))
        old_watch_without_sword = ("watch" in saved and saved["watch"][1] and not starter
                                   and not observed.get("temper_starter_sword"))
        if past_lesson or old_watch_without_sword:
            # Keep the old onboarding contract for players past it or lacking a recoverable watch reward.
            del known["first_temper"]
            equip = known.get("equip_sword")
            if "equip_sword" not in saved and equip and equip["requires"] == ["first_temper"]:
                known["equip_sword"] = dict(equip, requires=["watch"])
    views = {}
    for key, row in known.items():
        saved_row = saved.get(key)
        done, counts = saved_row[1:] if saved_row else (False, {})
        goals = []
        for g in row["goals"]:
            value = counts.get(g["id"], 0)
            if row["type"] == "collect" and saved_row and not done:
                value = stacks.get(g["target"], 0)
            goals.append(dict(id=g["id"], target=g["target"], label=g.get("label", ""), count=min(value, g["required"]),
                              alsoTargets=g.get("alsoTargets", []),
                              required=g["required"], allowExisting=g.get("allowExisting", False),
                              observedCount=min(observed.get(g["target"], 0), g["required"])))
            if "dropFrom" in g:
                goals[-1].update(dropFrom=g["dropFrom"], dropChance=g["dropChance"])
        if done:
            state = "completed"
        elif saved_row:
            state = "ready" if all(g["count"] == g["required"] for g in goals) else "active"
        else:
            unlocked = level >= row["minLevel"] and all(p in saved and saved[p][1] for p in row["requires"])
            # Earlier versions unlocked the gate after A1, including saves without quest histories.
            if key == BOSS_QUEST and objective in ("boss", "complete"):
                unlocked = True
            state = "available" if unlocked else "locked"
        focus = next((g for g in goals if g["count"] < g["required"]), goals[-1])
        reward = row.get("reward", {})
        reward_parts = []
        if reward.get("xp"):
            reward_parts.append(f"경험치 {reward['xp']}")
        if reward.get("gold"):
            reward_parts.append(f"골드 {reward['gold']}")
        for item in reward.get("items", []):
            reward_parts.append(inventory.item_name(item))
        for item, quantity in reward.get("materials", {}).items():
            reward_parts.append(f"{inventory.MATERIALS[item]} ×{quantity}")
        view = {k: row[k] for k in ("id", "name", "category", "type", "npcId",
                                    "reportNpcId", "dialogue", "instruction", "minLevel", "requires", "completion")}
        view.update(state=state, goals=goals, target=focus["target"], required=focus["required"], count=focus["count"],
                    rewardXp=reward.get("xp", 0), rewardGold=reward.get("gold", 0), rewardLabel=" · ".join(reward_parts))
        views[key] = (row, view)
    return views


def collection_drops(db, user, monster):
    """Return the eligible material rolls from this player's accepted quest contracts."""
    eligible = {}
    for definition, in db.execute("""SELECT definition FROM quests WHERE user_id=? AND completed=0
            AND json_extract(definition, '$.type')='collect'""", (user,)):
        for goal in json.loads(definition)["goals"]:
            if monster not in goal.get("dropFrom", ()):
                continue
            material = goal["target"]
            owned = db.execute("SELECT quantity FROM item_stacks WHERE user_id=? AND definition=?",
                               (user, material)).fetchone()
            if not owned or owned[0] < goal["required"]:
                eligible[material] = max(eligible.get(material, 0), goal["dropChance"])
    return eligible


def grant(db, user, reward, add_xp, quest_id=None):
    add_xp(db, user, reward.get("xp", 0))
    db.execute("UPDATE user_progress SET gold=gold+? WHERE user_id=?", (reward.get("gold", 0), user))
    for item in reward.get("items", []):
        instance = inventory.grant(db, user, item)
        if quest_id == "watch" and item == "balance_sword":
            db.execute("UPDATE quests SET starter_sword_id=? WHERE user_id=? AND quest_id='watch' AND starter_sword_id IS NULL",
                       (instance, user))
    for item, n in reward.get("materials", {}).items():
        inventory.stack(db, user, item, n)


def finish(db, user, row, goals, add_xp):
    counts = {g["id"]: g["required"] for g in goals}
    db.execute("UPDATE quests SET completed=1,counts=? WHERE user_id=? AND quest_id=?",
               (json.dumps(counts), user, row["id"]))
    grant(db, user, row.get("reward", {}), add_xp, row["id"])
    next_objective = row.get("nextObjective")
    if next_objective:
        previous, = db.execute("SELECT objective FROM user_progress WHERE user_id=?", (user,)).fetchone()
        if OBJECTIVES.index(previous) < OBJECTIVES.index(next_objective):
            db.execute("UPDATE user_progress SET objective=? WHERE user_id=?", (next_objective, user))


def interact(db, user, kind, key, npc, level, add_xp):
    entry = current(db, user, level).get(key)
    if entry is None:
        raise RuleError(400, "UNKNOWN_QUEST")
    row, view = entry
    if npc != row["npcId" if kind == "accept" else "reportNpcId"]:
        raise RuleError(409, "WRONG_NPC")
    if kind == "accept":
        if view["state"] != "available":
            raise RuleError(409, "QUEST_NOT_AVAILABLE")
        counts = {g["id"]: g["observedCount"] for g in view["goals"]
                  if row["type"] == "tutorial" and g["allowExisting"]}
        db.execute("INSERT INTO quests(user_id,quest_id,definition,counts) VALUES(?,?,?,?)",
                   (user, key, json.dumps(row, ensure_ascii=False), json.dumps(counts)))
        grant(db, user, row.get("onAccept", {}), add_xp)
        if row["completion"] == "immediate" and all(counts.get(g["id"], 0) >= g["required"] for g in row["goals"]):
            finish(db, user, row, row["goals"], add_xp)
    else:
        if view["state"] != "ready":
            raise RuleError(409, "QUEST_NOT_READY")
        if row["type"] == "collect":
            for g in row["goals"]:
                inventory.stack(db, user, g["target"], -g["required"])
        finish(db, user, row, view["goals"], add_xp)


def event(db, user, kind, target, add_xp):
    if kind == "tutorial":
        if target not in TUTORIALS:
            raise RuleError(400, "UNKNOWN_TUTORIAL")
        db.execute("""INSERT INTO tutorial_history VALUES(?,?,1) ON CONFLICT(user_id,target)
            DO UPDATE SET count=count+1""", (user, target))
    rows = list(db.execute("SELECT quest_id,definition,counts FROM quests WHERE user_id=? AND completed=0", (user,)))
    for key, definition, raw in rows:
        row = json.loads(definition)
        if row["type"] != kind:
            continue
        counts = json.loads(raw)
        match = next((g for g in row["goals"] if g["target"] == target or target in g.get("alsoTargets", [])), None)
        if not match:
            continue
        counts[match["id"]] = min(match["required"], counts.get(match["id"], 0)+1)
        db.execute("UPDATE quests SET counts=? WHERE user_id=? AND quest_id=?", (json.dumps(counts), user, key))
        if row["completion"] == "immediate" and all(counts.get(g["id"], 0) >= g["required"] for g in row["goals"]):
            finish(db, user, row, row["goals"], add_xp)
