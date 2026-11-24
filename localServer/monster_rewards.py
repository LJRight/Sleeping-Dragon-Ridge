"""Authoritative field payouts and values copied into newly accepted quest contracts."""
import json
from pathlib import Path

FIELD_IDS = {"A1", "A2", "A3", "A4", "B1", "B1G"}
GEAR_CHANCES = ("commonGearChance", "rareGearChance", "legendaryGearChance")


def load(path):
    data = json.loads(Path(path).read_text(encoding="utf-8"))

    def numbers(row, names, maximum, minimum=0):
        for key in names:
            if type(row.get(key)) is not int or not minimum <= row[key] <= maximum:
                raise ValueError(f"{path}: {key} must be an integer from {minimum} to {maximum}")

    if set(data["field"]) != FIELD_IDS:
        raise ValueError(f"{path}: expected a reward row for each field monster")
    for monster, row in data["field"].items():
        numbers(row, ("xp", "gold"), 100000)
        numbers(row, ("oreCount", "refinedOreCount"), 9999)
        numbers(row, (*GEAR_CHANCES, "oreChance", "refinedOreChance"), 100)
        if sum(row[key] for key in GEAR_CHANCES) > 100:
            raise ValueError(f"{path}: {monster} equipment probabilities exceed 100%")
    numbers(data["questDropChance"], ("ember_heartstone", "sturdy_tusk"), 100, minimum=1)
    numbers(data["bossReport"], ("xp", "gold"), 100000)
    numbers(data["bossReport"], ("oreCount", "refinedOreCount"), 9999)
    return data


BALANCE = load(Path(__file__).with_name("monster_rewards.json"))
