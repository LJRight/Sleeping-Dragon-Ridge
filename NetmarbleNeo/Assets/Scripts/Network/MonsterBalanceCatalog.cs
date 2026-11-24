using System;
using UnityEngine;

public sealed class MonsterBalanceCatalog : ScriptableObject
{
    [SerializeField] private FieldMonsterBalance[] fieldMonsters;
    [SerializeField, Range(0f, 1f)] private float goldenBoarChance = .15f;
    [SerializeField] private BossMonsterBalance boss;

    private static MonsterBalanceCatalog loaded;
    private static MonsterBalanceCatalog Current
    {
        get
        {
            if (!loaded) loaded = Resources.Load<MonsterBalanceCatalog>(nameof(MonsterBalanceCatalog));
            if (!loaded) throw new InvalidOperationException("Missing Resources/MonsterBalanceCatalog.asset");
            return loaded;
        }
    }

    public static BossMonsterBalance Boss => Current.boss;
    public static float GoldenBoarChance => Current.goldenBoarChance;

    public static FieldMonsterBalance FindField(string id)
    {
        foreach (FieldMonsterBalance entry in Current.fieldMonsters)
            if (entry != null && entry.id == id) return entry;
        throw new ArgumentException($"Unknown field monster '{id}' in MonsterBalanceCatalog.", nameof(id));
    }

    private void OnValidate()
    {
        if (fieldMonsters != null)
            foreach (FieldMonsterBalance entry in fieldMonsters) entry?.NormalizeTimes();
    }
}
