using System;
using System.Linq;

[Serializable]
public sealed class ExpeditionSnapshot
{
    public int level, gold, revision;
    public float totalXp;
    public int[] levelXpThresholds;
    public string objective;
    public string starterSwordInstanceId;
    public bool bossDiscovered;
    public float experience;
    public ExpeditionStats stats;
    public ExpeditionItem[] items;
    public ExpeditionQuest[] quests;
    public ExpeditionEnhanceResult enhanceResult;

    public ExpeditionItem Item(string id) => items?.FirstOrDefault(i => i.instanceId == id);
    public ExpeditionItem Worn(string slot) => items?.FirstOrDefault(i => i.equipped && i.slot == slot);
    public ExpeditionQuest Quest(string id) => quests?.FirstOrDefault(q => q.id == id);
}

[Serializable]
public sealed class ExpeditionStats
{
    public int hp, attack, defense;
    public static int Value(ExpeditionStats stats, int row)
    {
        if (stats == null) return 0;
        return row == 0 ? stats.hp : row == 1 ? stats.attack : stats.defense;
    }

    public string Format()
    {
        var parts = new System.Collections.Generic.List<string>(3);
        if (hp != 0) parts.Add($"체력 +{hp}");
        if (attack != 0) parts.Add($"공격력 +{attack}");
        if (defense != 0) parts.Add($"방어력 +{defense}");
        return parts.Count == 0 ? "추가 효과 없음" : string.Join("  ", parts);
    }
}

[Serializable]
public sealed class ExpeditionItem
{
    public string instanceId, definitionId, name, type, slot, rarity, setId, setName;
    public int enhancement, quantity, setCount, sellGold;
    public bool equipped;
    public ExpeditionStats @base, rarityBonus, enhanceBonus, setTwo, setThree;
    public ExpeditionSetBonus[] setBonuses;
    public ExpeditionEnhanceOffer nextEnhance;
    public bool IsGear => type == "equipment";
    public bool IsRidgeSword => definitionId == "ridge_sword";
    public string Icon => IsRidgeSword ? "fury_sword" : definitionId;
    public string Rank => rarity switch { "rare" => "레어", "legendary" => "전설", _ => "일반" };
    public string GradeFrame => "slot_" + (rarity == "rare" || rarity == "legendary" ? rarity : "common");
}

[Serializable]
public sealed class ExpeditionSetBonus
{
    public string setId, setName;
    public int setCount;
    public ExpeditionStats setTwo, setThree;
}

[Serializable]
public sealed class ExpeditionEnhanceOffer
{
    public int ore, rareOre, gold, success, keep, downgrade, destroy;
    public string stat;
    public int statBefore, statAfter;
}

[Serializable]
public sealed class ExpeditionEnhanceResult
{
    public string actionId, instanceId, outcome;
    public int before, after;
}

[Serializable]
public sealed class ExpeditionQuest
{
    public string id, name, category, type, state, completion, npcId, reportNpcId, dialogue, instruction, target;
    public int required, count, rewardGold, rewardXp, minLevel;
    public string[] requires;
    public ExpeditionGoal[] goals;
    public string rewardLabel;
    public bool Ongoing => state == "active" || state == "ready";
    public string StateText => state switch
    {
        "available" => "수락 가능", "active" => "진행 중", "ready" => type == "collect" ? "전달 가능" : "보고 가능",
        "completed" => "완료", _ => "잠김"
    };
}

[Serializable]
public sealed class ExpeditionGoal
{
    public string id, target, label;
    public string[] dropFrom;
    public int required, count;
    public string Caption => !string.IsNullOrWhiteSpace(label) ? label : target switch
    {
        "A1" => "영혼 포식자", "A2" => "화염의 영혼 포식자", "A3" => "맹독의 영혼 포식자",
        "A4" => "빙결의 영혼 포식자", "B1" => "용비늘 멧돼지", "B1G" => "황금의 용비늘 멧돼지",
        "move" => "이동", "open_inventory" => "인벤토리 열기", "attack_hit" => "기본 공격 적중",
        "equip" => "장비 착용", "visit_warden" => "경비대장 방문", "visit_smith" => "대장장이 방문",
        "parcel" => "보급 꾸러미", "ore" => "강화석", "rare_ore" => "정제 강화석",
        "use_skill" => "스킬 사용", "skill_hit" => "스킬 적중", "enhance" => "장비 강화",
        "use_potion" => "회복 물약 사용", _ => target
    };
    public string Progress => $"{Caption}   {Math.Min(count,required)}/{required}";
}
