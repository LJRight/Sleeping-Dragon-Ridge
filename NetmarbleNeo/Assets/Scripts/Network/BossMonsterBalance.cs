using System;
using UnityEngine;

[Serializable]
public sealed class BossMonsterBalance
{
    [Header("기본 능력치")]
    [InspectorName("최대 체력"), Min(1)] public int maxHealth = 480;
    [InspectorName("기본 방어력"), Min(0)] public int initialDefense = 30;
    [InspectorName("추격 속도 (m/s)"), Min(.1f)] public float chaseSpeed = 3f;

    [Header("일반 공격")]
    [InspectorName("피해량"), Min(1)] public int basicDamage = 10;
    [InspectorName("사거리 (m)"), Min(.1f)] public float basicRadius = 3f;
    [InspectorName("공격 각도 (도)"), Range(1f, 360f)] public float basicArc = 110f;
    [InspectorName("판정 시점 (0~1)"), Range(0f, 1f), Tooltip("0은 동작 시작, 1은 동작 끝입니다.")]
    public float basicHitFraction = .5f;
    [InspectorName("공격 후 대기 (초)"), Min(0f)] public float basicRecovery = .5f;

    [Header("발톱 공격")]
    [InspectorName("피해량"), Min(1)] public int clawDamage = 16;
    [InspectorName("전방 길이 (m)"), Min(.1f)] public float clawLength = 6f;
    [InspectorName("좌우 폭 (m)"), Min(.1f)] public float clawWidth = 3f;
    [InspectorName("판정 프레임 (30fps)"), Range(0, 90)] public int clawHitFrame = 35;
    [InspectorName("재사용 대기 (초)"), Min(.1f)] public float clawCooldown = 4f;

    [Header("지상 · 공중 화염")]
    [InspectorName("타격당 피해 (대상 최대 체력 %)"), Range(0, 100),
     Tooltip("화염에 맞은 플레이어마다 최대 체력의 이 비율만큼 피해를 줍니다. 방어력을 무시합니다.")]
    public int flameMaxHealthPercent = 4;
    [InspectorName("사거리 (m)"), Min(.1f)] public float flameRadius = 8f;
    [InspectorName("공격 각도 (도)"), Range(1f, 360f)] public float flameArc = 90f;
    [InspectorName("첫 타격 프레임 (30fps)"), Range(0, 80)] public int flameFirstHitFrame = 15;
    [InspectorName("타격 간격 (프레임, 30fps)"), Range(1, 80)] public int flameTickIntervalFrames = 15;
    [InspectorName("타격 횟수"), Min(1), Tooltip("마지막 타격이 클립보다 늦으면 그 타격까지 시전을 유지합니다.")]
    public int flameTickCount = 5;
    [InspectorName("타격당 회복률 (%)"), Range(0, 100), Tooltip("한 명 이상에게 피해를 준 타격마다 보스 최대 체력의 이 비율을 한 번 회복합니다.")]
    public int healPercentPerTick = 3;
    [InspectorName("지상 화염 재사용 대기 (초)"), Min(.1f)] public float flameCooldown = 16f;
    [InspectorName("비행 재사용 대기 (초)"), Min(.1f)] public float flightCooldown = 40f;

    [Header("반사 · 방어 파괴")]
    [InspectorName("반사 피해 (공격자 최대 체력 %)"), Range(0, 100)]
    public int reflectionMaxHealthPercent = 10;
    [InspectorName("반사 재사용 대기 (초)"), Min(.1f)] public float reflectCooldown = 35f;
    [InspectorName("파괴당 방어력 감소"), Min(0)] public int defenseLossPerBreak = 10;
    [InspectorName("1차 파괴 잔여 체력 (%)"), Range(0, 100)] public int firstBreakPercent = 75;
    [InspectorName("2차 파괴 잔여 체력 (%)"), Range(0, 100)] public int secondBreakPercent = 50;
    [InspectorName("3차 파괴 잔여 체력 (%)"), Range(0, 100)] public int thirdBreakPercent = 25;

    public int CountDefenseBreaks(int remainingHealth, int maximumHealth)
    {
        long remaining = (long)remainingHealth * 100;
        int breaks = remaining <= (long)maximumHealth * firstBreakPercent ? 1 : 0;
        if (remaining <= (long)maximumHealth * secondBreakPercent) breaks++;
        if (remaining <= (long)maximumHealth * thirdBreakPercent) breaks++;
        return breaks;
    }
}
