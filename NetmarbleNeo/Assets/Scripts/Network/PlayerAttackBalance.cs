using System;
using UnityEngine;

public sealed class PlayerAttackBalance : ScriptableObject
{
    [Min(0)] public float basic1Percent = 100f;
    [Min(0)] public float basic2Percent = 100f;
    [Min(0)] public float basic3Percent = 100f;
    [Min(0)] public float spinningSlashPercent = 100f;
    [Min(0)] public float uppercutPercent = 150f;
    [Min(0)] public float jumpAttackPercent = 200f;

    private static PlayerAttackBalance loaded;
    public static PlayerAttackBalance Current
    {
        get
        {
            if (!loaded) loaded = Resources.Load<PlayerAttackBalance>(nameof(PlayerAttackBalance));
            if (!loaded) throw new InvalidOperationException("Missing Resources/PlayerAttackBalance.asset");
            return loaded;
        }
    }

    public int DamageBeforeDefense(int attackPower, PlayerSkill skill, int stage)
    {
        float percent = skill switch
        {
            PlayerSkill.None when stage == 1 => basic1Percent,
            PlayerSkill.None when stage == 2 => basic2Percent,
            PlayerSkill.None when stage == 3 => basic3Percent,
            PlayerSkill.SpinningSlash => spinningSlashPercent,
            PlayerSkill.Uppercut => uppercutPercent,
            PlayerSkill.JumpAttack => jumpAttackPercent,
            _ => throw new ArgumentOutOfRangeException(nameof(skill), $"Unknown attack {skill} at combo stage {stage}.")
        };
        if (percent <= 0f) return 0;
        float damage = attackPower * (percent / 100f);
        return damage >= int.MaxValue ? int.MaxValue : Mathf.Max(1, Mathf.RoundToInt(damage));
    }
}
