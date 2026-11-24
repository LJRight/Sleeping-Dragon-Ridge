"""Equipment definitions, per-account item instances and derived character stats."""
from collections import Counter
import secrets
from uuid import uuid4

SLOTS = {"sword": "검", "helm": "투구", "armor": "방어구"}
SET_NAMES = {"fury": "맹공", "vital": "생명", "balance": "균형"}
RARITIES = ("common", "rare", "legendary")
HUNT_PRICES = {"common": 30, "rare": 100, "legendary": 300}
HUNTED_SPARE = "hunted=1 AND equipped_slot IS NULL"
RIDGE_SWORD = "ridge_sword"
BASE = {
    "fury_sword": (0, 6, 0), "fury_helm": (6, 2, 0), "fury_armor": (0, 2, 1),
    "vital_sword": (8, 2, 1), "vital_helm": (20, 0, 2), "vital_armor": (14, 0, 4),
    "balance_sword": (4, 4, 0), "balance_helm": (14, 1, 1), "balance_armor": (8, 1, 3),
    RIDGE_SWORD: (0, 12, 0),
}
SET_BONUS = {
    "fury": ((0, 3, 0), (0, 4, 0)),
    "vital": ((12, 0, 2), (18, 0, 3)),
    "balance": ((6, 1, 1), (8, 2, 1)),
}
# The unique quest reward is never rolled from a field monster's drop table.
FIELD_GEAR = tuple(definition for definition in BASE if definition != RIDGE_SWORD)
ENHANCEMENT_RULES = (
    # success %, downgrade %, destruction %, ore, refined ore, gold
    (100, 0, 0, 1, 0, 10), (90, 0, 0, 1, 0, 15), (80, 0, 0, 2, 0, 25),
    (60, 10, 0, 3, 0, 40), (40, 15, 0, 4, 0, 60),
    (15, 20, 1, 8, 2, 300), (7, 25, 2, 12, 4, 800),
)
MATERIALS = {"parcel": "보급 꾸러미", "ore": "강화석", "rare_ore": "정제 강화석",
             "ember_heartstone": "잿불 심장석", "sturdy_tusk": "단단한 엄니"}
LEGACY = {f"{old}_{i}": f"{set_id}_{slot}" for old, slot in (("weapon", "sword"), ("armor", "armor"))
          for i, set_id in enumerate(("balance", "fury", "vital"), 1)}
STAT_KEYS = ("hp", "attack", "defense")


def initialize(db):
    db.execute("""CREATE TABLE IF NOT EXISTS item_instances (
        user_id TEXT NOT NULL REFERENCES user_progress ON DELETE CASCADE,
        instance_id TEXT NOT NULL, definition TEXT NOT NULL, rarity TEXT NOT NULL,
        enhancement INTEGER NOT NULL DEFAULT 0 CHECK(enhancement BETWEEN 0 AND 7),
        equipped_slot TEXT CHECK(equipped_slot IN ('sword','helm','armor')),
        hunted INTEGER NOT NULL DEFAULT 0 CHECK(hunted IN (0,1)),
        PRIMARY KEY(user_id, instance_id), UNIQUE(user_id, equipped_slot))""")
    needs_origin = "hunted" not in {col[1] for col in db.execute("PRAGMA table_info(item_instances)")}
    if needs_origin:
        db.execute("ALTER TABLE item_instances ADD COLUMN hunted INTEGER NOT NULL DEFAULT 0 CHECK(hunted IN (0,1))")
    db.execute("""CREATE TABLE IF NOT EXISTS item_stacks (
        user_id TEXT NOT NULL REFERENCES user_progress ON DELETE CASCADE,
        definition TEXT NOT NULL, quantity INTEGER NOT NULL CHECK(quantity>=0),
        PRIMARY KEY(user_id,definition))""")
    return needs_origin


def recover_hunt_origins(db):
    # Old loot receipts store the definition and rarity, never the instance ID.
    # If a quest could also have granted a surviving copy, protect the whole group.
    db.execute("""WITH drops AS (
          SELECT a.user_id, json_extract(e.value,'$.definitionId') AS gear,
              json_extract(e.value,'$.rarity') AS rarity, count(*) AS amount
          FROM game_actions a JOIN user_rewards r ON r.user_id=a.user_id AND r.reward_id=a.action_id
              JOIN json_each(a.result,'$.entries') e
          WHERE json_extract(e.value,'$.type')='equipment'
          GROUP BY a.user_id,gear,rarity
        ), candidates AS (
          SELECT user_id,definition,rarity,count(*) AS amount FROM item_instances
          WHERE length(instance_id)=32 AND instance_id NOT GLOB '*[^0-9a-f]*'
          GROUP BY user_id,definition,rarity
        )
        UPDATE item_instances AS i SET hunted=1
        WHERE length(i.instance_id)=32 AND i.instance_id NOT GLOB '*[^0-9a-f]*'
          AND i.definition!='ridge_sword' AND EXISTS (
            SELECT 1 FROM candidates c JOIN drops d ON c.user_id=d.user_id AND c.definition=d.gear AND c.rarity=d.rarity
            WHERE c.user_id=i.user_id AND c.definition=i.definition AND c.rarity=i.rarity AND c.amount<=d.amount)
          AND NOT EXISTS (
            SELECT 1 FROM quests q WHERE q.user_id=i.user_id AND (
                q.starter_sword_id=i.instance_id OR (i.rarity='common' AND (
                    EXISTS (SELECT 1 FROM json_each(q.definition,'$.onAccept.items') WHERE value=i.definition)
                    OR (q.completed=1 AND EXISTS (
                        SELECT 1 FROM json_each(q.definition,'$.reward.items') WHERE value=i.definition))))))""")


def item_name(definition):
    if definition == RIDGE_SWORD:
        return "잠든 용의 검"
    set_id, slot = definition.split("_")
    return f"{SET_NAMES[set_id]}의 {SLOTS[slot]}"


def grant(db, user, definition, rarity=None, enhancement=0, instance=None, equipped=False, *, hunted=False):
    if rarity is None:
        rarity = "legendary" if definition == RIDGE_SWORD else "common"
    if (definition not in BASE or rarity not in RARITIES or not 0 <= enhancement <= 5 + RARITIES.index(rarity)
            or definition == RIDGE_SWORD and (rarity != "legendary" or hunted)):
        raise ValueError(f"Invalid equipment {definition}/{rarity}/+{enhancement}")
    instance = instance or uuid4().hex
    slot = definition.split("_")[1] if equipped else None
    db.execute("""INSERT INTO item_instances(user_id,instance_id,definition,rarity,enhancement,equipped_slot,hunted)
                  VALUES (?,?,?,?,?,?,?)""", (user, instance, definition, rarity, enhancement, slot, int(hunted)))
    return instance


def sell(db, user, instance):
    eligible = f"user_id=? AND instance_id=? AND {HUNTED_SPARE}"
    row = db.execute(f"SELECT rarity FROM item_instances WHERE {eligible}", (user, instance)).fetchone()
    if not row or row[0] not in HUNT_PRICES:
        return "ITEM_NOT_SELLABLE"
    price = HUNT_PRICES[row[0]]
    if not db.execute("UPDATE user_progress SET gold=gold+? WHERE user_id=? AND gold<=?",
                      (price, user, 2**31 - 1 - price)).rowcount:
        return "GOLD_LIMIT"
    if not db.execute(f"DELETE FROM item_instances WHERE {eligible}", (user, instance)).rowcount:
        return "ITEM_NOT_SELLABLE"
    return None


def stack(db, user, definition, quantity):
    if definition not in MATERIALS:
        raise ValueError(f"Unknown material {definition}")
    if quantity < 0:
        changed = db.execute("UPDATE item_stacks SET quantity=quantity+? WHERE user_id=? AND definition=? AND quantity>=?",
                             (quantity, user, definition, -quantity))
        if not changed.rowcount:
            raise ValueError("INSUFFICIENT_ITEMS")
        db.execute("DELETE FROM item_stacks WHERE user_id=? AND definition=? AND quantity=0", (user, definition))
    elif quantity:
        db.execute("""INSERT INTO item_stacks VALUES(?,?,?) ON CONFLICT(user_id,definition)
            DO UPDATE SET quantity=quantity+excluded.quantity""", (user, definition, quantity))


def change_slot(db, user, instance, equip):
    row = db.execute("SELECT definition,equipped_slot FROM item_instances WHERE user_id=? AND instance_id=?",
                     (user, instance)).fetchone()
    if row is None:
        return "ITEM_NOT_OWNED"
    slot = row[0].split("_")[1]
    if equip and row[1] is None:
        db.execute("UPDATE item_instances SET equipped_slot=NULL WHERE user_id=? AND equipped_slot=?", (user, slot))
        db.execute("UPDATE item_instances SET equipped_slot=? WHERE user_id=? AND instance_id=?", (slot, user, instance))
    elif not equip and row[1] is not None:
        db.execute("UPDATE item_instances SET equipped_slot=NULL WHERE user_id=? AND instance_id=?", (user, instance))
    else:
        return "EQUIPMENT_STATE_CHANGED"
    return None


def next_enhancement(definition, rarity, level):
    if level >= 5 + RARITIES.index(rarity):
        return None
    success, downgrade, destroy, ore, rare_ore, gold = ENHANCEMENT_RULES[level]
    if definition == RIDGE_SWORD:
        downgrade += destroy
        destroy = 0
    return dict(ore=ore, rareOre=rare_ore, gold=gold, success=success, keep=100-success-downgrade-destroy,
                downgrade=downgrade, destroy=destroy)


def enhance(db, user, instance):
    row = db.execute("SELECT definition,rarity,enhancement FROM item_instances WHERE user_id=? AND instance_id=?",
                     (user, instance)).fetchone()
    if not row:
        return None, "ITEM_NOT_OWNED"
    definition, rarity, before = row
    rule = next_enhancement(definition, rarity, before)
    if not rule:
        return None, "ENHANCEMENT_AT_CAP"
    materials = dict(db.execute("SELECT definition,quantity FROM item_stacks WHERE user_id=?", (user,)))
    if materials.get("ore", 0) < rule["ore"] or materials.get("rare_ore", 0) < rule["rareOre"]:
        return None, "INSUFFICIENT_MATERIALS"
    if not db.execute("UPDATE user_progress SET gold=gold-? WHERE user_id=? AND gold>=?",
                      (rule["gold"], user, rule["gold"])).rowcount:
        return None, "INSUFFICIENT_GOLD"
    stack(db, user, "ore", -rule["ore"])
    stack(db, user, "rare_ore", -rule["rareOre"])
    roll = secrets.randbelow(100)
    if roll < rule["success"]:
        outcome, after = "success", before + 1
    elif roll < rule["success"] + rule["keep"]:
        outcome, after = "keep", before
    elif roll < 100 - rule["destroy"]:
        outcome, after = "downgrade", before - 1
    else:
        outcome, after = "destroy", -1
    if outcome == "destroy":
        db.execute("DELETE FROM item_instances WHERE user_id=? AND instance_id=?", (user, instance))
    else:
        db.execute("UPDATE item_instances SET enhancement=? WHERE user_id=? AND instance_id=?", (after, user, instance))
    return dict(instanceId=instance, outcome=outcome, before=before, after=after), None


def stats(values):
    return dict(zip(STAT_KEYS, values))


def enhancement_bonus(slot, level):
    gains = (1, 1, 1, 2, 2, 5, 9)
    return (5 if slot == "helm" else 1) * sum(gains[:level])


def equipment_snapshot(db, user, level):
    rows = list(db.execute("""SELECT instance_id,definition,rarity,enhancement,equipped_slot,hunted FROM item_instances
        WHERE user_id=? ORDER BY definition,instance_id""", (user,)))
    counts = Counter(definition.split("_")[0] for _, definition, _, _, slot, _ in rows if slot)
    if counts.pop("ridge", 0):
        counts.update(SET_BONUS.keys())
    bonuses = {
        k: dict(setId=k, setName=SET_NAMES[k], setCount=counts[k], setTwo=stats(two), setThree=stats(three))
        for k, (two, three) in SET_BONUS.items()
    }
    early = min(level, 10) - 1
    late = max(level - 10, 0)
    final = [100 + early*12 + late*8, 10 + early*2 + late, early*2 + late]
    items = []
    for instance, definition, rarity, enhancement, equipped, hunted in rows:
        set_id, slot = definition.split("_")
        rare, plus = [0]*3, [0]*3
        index = {"helm": 0, "sword": 1, "armor": 2}[slot]
        rare[index] = (10 if slot == "helm" else 2) * RARITIES.index(rarity)
        plus[index] = enhancement_bonus(slot, enhancement)
        base = BASE[definition]
        offer = next_enhancement(definition, rarity, enhancement)
        if offer is not None:
            fixed = base[index] + rare[index]
            offer.update(stat=STAT_KEYS[index], statBefore=fixed + plus[index],
                         statAfter=fixed + enhancement_bonus(slot, enhancement + 1))
        if equipped:
            for i in range(3):
                final[i] += base[i] + rare[i] + plus[i]
        wildcard = definition == RIDGE_SWORD
        details = list(bonuses.values()) if wildcard else [bonuses[set_id]]
        two, three = SET_BONUS.get(set_id, ((0, 0, 0), (0, 0, 0)))
        items.append(dict(instanceId=instance, definitionId=definition, name=item_name(definition),
                          type="equipment", slot=slot, rarity=rarity, enhancement=enhancement, quantity=1,
                          equipped=bool(equipped), sellGold=HUNT_PRICES[rarity] if hunted and not equipped else 0,
                          setId=set_id, setName="모든 세트" if wildcard else SET_NAMES[set_id],
                          setCount=counts[set_id], setBonuses=details, nextEnhance=offer,
                          base=stats(base), rarityBonus=stats(rare), enhanceBonus=stats(plus),
                          setTwo=stats(two), setThree=stats(three)))
    for set_id, count in counts.items():
        for threshold, bonus in enumerate(SET_BONUS[set_id], 2):
            if count >= threshold:
                for i in range(3):
                    final[i] += bonus[i]
    for definition, count in db.execute("SELECT definition,quantity FROM item_stacks WHERE user_id=? ORDER BY definition", (user,)):
        items.append(dict(instanceId="stack:"+definition, definitionId=definition, name=MATERIALS[definition],
                          type="material", slot="", rarity="common", enhancement=0, quantity=count, equipped=False))
    return items, stats(final)
