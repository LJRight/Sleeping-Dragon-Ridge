"""Provision the TODO-019 fixture in an explicitly selected disposable database."""
import argparse
from pathlib import Path

if __package__:
    from . import server, inventory, progression
else:
    import server
    import inventory
    import progression


def prepare(store, user):
    with store.connect() as db:
        db.execute("BEGIN IMMEDIATE")
        progression.progress_row(db, user)
        if db.execute("SELECT 1 FROM game_actions WHERE user_id=? AND action_id='fixture-019'", (user,)).fetchone():
            return
        db.execute("UPDATE item_instances SET equipped_slot=NULL WHERE user_id=?", (user,))
        for slot in ("sword", "helm", "armor"):
            inventory.grant(db, user, "balance_"+slot, instance="fixture-balance-"+slot, equipped=True)
        inventory.grant(db, user, "balance_sword", "rare", 2, "fixture-balance-spare")
        inventory.grant(db, user, "vital_helm", "legendary", 1, "fixture-vital-helm")
        inventory.stack(db, user, "ore", 3)
        db.execute("UPDATE user_progress SET gold=gold+100,revision=revision+1 WHERE user_id=?", (user,))
        db.execute("INSERT INTO game_actions(user_id,action_id,fingerprint) VALUES (?,'fixture-019','local-test')", (user,))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db", required=True, type=Path)
    parser.add_argument("--account", required=True)
    args = parser.parse_args()
    if not args.db.is_file() or args.db.resolve().is_relative_to((server.ROOT/"data").resolve()):
        parser.error("Choose an existing disposable database outside localServer/data.")
    prepare(server.Store(args.db), args.account)
    print("Inventory verification fixture ready.")
