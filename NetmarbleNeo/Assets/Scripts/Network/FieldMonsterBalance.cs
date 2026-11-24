using System;
using UnityEngine;

[Serializable]
public sealed class FieldMonsterBalance
{
    [InspectorName("몬스터 ID")] public string id;
    [Header("기본 능력치")]
    [InspectorName("최대 체력"), Min(1)] public int maximumHealth;
    [InspectorName("방어력"), Min(0)] public int defense;
    [InspectorName("공격 피해"), Min(0)] public int damage;
    [InspectorName("재생성 대기 (초)"), Min(0f)] public float respawnDelay;

    [Header("탐지와 이동")]
    [InspectorName("깨어 있는 상태로 출현")] public bool startAwake;
    [InspectorName("탐지 반경 (m)"), Min(0f)] public float detectionRadius;
    [InspectorName("활동 영역 반경 (m)"), Min(0f)] public float territoryRadius;
    [InspectorName("추격·복귀 속도 (m/s)"), Min(0f)] public float runSpeed;
    [InspectorName("배회 속도 (m/s)"), Min(0f)] public float walkSpeed;
    [InspectorName("배회 반경 (m)"), Min(0f)] public float wanderRadius;
    [InspectorName("휴식 시간 (최소·최대 초)")] public Vector2 idleSeconds;
    [InspectorName("배회 시간 (최소·최대 초)")] public Vector2 walkSeconds;

    [Header("공격과 피격")]
    [InspectorName("공격 시작 거리 (m)"), Min(0f)] public float attackRange;
    [InspectorName("타격 판정 거리 (m)"), Min(0f)] public float hitRange;
    [InspectorName("타격 각도 (°)"), Range(1f, 360f)] public float attackAngle;
    [InspectorName("공격 동작 (초)"), Min(.01f)] public float attackDuration;
    [InspectorName("타격 시점 (공격 동작의 0~1)"), Range(0f, 1f)] public float attackHitFraction;
    [InspectorName("공격 후 대기 (초)"), Min(0f)] public float attackCooldown;
    [InspectorName("첫 조우 포효 (초)"), Min(0f)] public float screamDuration;
    [InspectorName("피격 경직 (초)"), Min(0f)] public float hitDuration;
    [InspectorName("사망 후 퇴장 (초)"), Min(0f)] public float deathDuration;

    public void NormalizeTimes()
    {
        idleSeconds = OrderedSeconds(idleSeconds);
        walkSeconds = OrderedSeconds(walkSeconds);
    }

    private static Vector2 OrderedSeconds(Vector2 seconds)
    {
        float minimum = Mathf.Max(0f, seconds.x);
        return new Vector2(minimum, Mathf.Max(minimum, seconds.y));
    }
}
