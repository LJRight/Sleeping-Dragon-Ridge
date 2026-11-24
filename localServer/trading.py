"""Commit a mutually approved game-server offer in the caller's BEGIN IMMEDIATE transaction."""
import hashlib
import json
import re

if __package__:
    from . import inventory, progression
else:
    import inventory
    import progression


MAX_AMOUNT = 2**31 - 1  # Unity represents gold, stack counts and revisions as Int32.
MAX_GEAR = 4
MATERIALS = {"ore": "ore", "rareOre": "rare_ore"}
OFFER_FIELDS = {"userId", "revision", "itemIds", "gold", *MATERIALS}
USER_ID = re.compile(r"[A-Za-z0-9_-]{3,32}\Z")


def validate_offer(offer):
    if not isinstance(offer, dict) or set(offer) != OFFER_FIELDS:
        raise progression.ProgressError(400, "INVALID_TRADE")
    user, items = offer["userId"], offer["itemIds"]
    if (not isinstance(user, str) or not USER_ID.fullmatch(user)
            or any(type(offer[key]) is not int or not 0 <= offer[key] <= MAX_AMOUNT
                   for key in ("revision", "gold", *MATERIALS))
            or not isinstance(items, list) or len(items) > MAX_GEAR
            or any(not isinstance(i, str) or not progression.EVENT_ID.fullmatch(i) for i in items)
            or len(set(items)) != len(items)):
        raise progression.ProgressError(400, "INVALID_TRADE")
    return dict(offer, itemIds=sorted(items))


def validate_balances(db, offer, incoming):
    user = offer["userId"]
    _, _, revision, gold, _ = progression.progress_row(db, user)
    if revision != offer["revision"]:
        raise progression.ProgressError(409, "STALE_REVISION")
    if revision == MAX_AMOUNT:
        raise progression.ProgressError(409, "BALANCE_LIMIT")
    stacks = dict(db.execute("SELECT definition,quantity FROM item_stacks WHERE user_id=?", (user,)))
    balances = {"gold": gold, **{key: stacks.get(definition, 0) for key, definition in MATERIALS.items()}}
    for key, balance in balances.items():
        if balance < offer[key]:
            code = "INSUFFICIENT_GOLD" if key == "gold" else "INSUFFICIENT_MATERIALS"
            raise progression.ProgressError(409, code)
        if balance - offer[key] + incoming[key] > MAX_AMOUNT:
            raise progression.ProgressError(409, "BALANCE_LIMIT")


def transfer_side(db, offer, incoming):
    user, recipient = offer["userId"], incoming["userId"]
    for instance in offer["itemIds"]:
        # Legacy instances used a per-account key. A collision must never replace somebody's item.
        moved = db.execute(f"""UPDATE item_instances SET user_id=?
            WHERE user_id=? AND instance_id=? AND {inventory.HUNTED_SPARE}
            AND NOT EXISTS (SELECT 1 FROM item_instances WHERE user_id=? AND instance_id=?)""",
                           (recipient, user, instance, recipient, instance))
        if moved.rowcount != 1:
            raise progression.ProgressError(409, "ITEM_UNAVAILABLE")
    for key, definition in MATERIALS.items():
        inventory.stack(db, user, definition, incoming[key] - offer[key])
    db.execute("UPDATE user_progress SET gold=gold+?,revision=revision+1 WHERE user_id=?",
               (incoming["gold"] - offer["gold"], user))


def commit(db, data):
    if not isinstance(data, dict) or set(data) != {"tradeId", "first", "second"}:
        raise progression.ProgressError(400, "INVALID_TRADE")
    trade_id = data["tradeId"]
    if not isinstance(trade_id, str) or not progression.EVENT_ID.fullmatch(trade_id):
        raise progression.ProgressError(400, "INVALID_TRADE")
    first, second = validate_offer(data["first"]), validate_offer(data["second"])
    if first["userId"] == second["userId"] or not any(
            side[key] for side in (first, second) for key in ("itemIds", "gold", *MATERIALS)):
        raise progression.ProgressError(400, "INVALID_TRADE")

    # Bind the ID to both accounts, both reviewed revisions and every offered asset.
    sides = sorted((first, second), key=lambda offer: offer["userId"])
    signature = hashlib.sha256(json.dumps(sides, sort_keys=True).encode("ascii")).hexdigest()
    prior = db.execute("SELECT fingerprint FROM trade_receipts WHERE trade_id=?", (trade_id,)).fetchone()
    if prior:
        if prior[0] != signature:
            raise progression.ProgressError(409, "TRADE_ID_CONFLICT")
    else:
        # Validate both full offers against pre-trade balances. Incoming assets cannot fund an offer.
        validate_balances(db, first, second)
        validate_balances(db, second, first)
        if set(first["itemIds"]).intersection(second["itemIds"]):
            raise progression.ProgressError(409, "ITEM_UNAVAILABLE")
        transfer_side(db, first, second)
        transfer_side(db, second, first)
        db.execute("INSERT INTO trade_receipts(trade_id,fingerprint) VALUES(?,?)", (trade_id, signature))

    # A retry following a lost acknowledgement must not roll either client's state back.
    return dict(tradeId=trade_id, first=progression.snapshot(db, first["userId"]),
                second=progression.snapshot(db, second["userId"]))
