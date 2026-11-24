# 플레이어 이동 애니메이션과 3타 콤보

2026-11-17 구현. 사용자 지정 `Assets/Prefabs/Player Test.prefab`과 `Assets/Animation/Player Animator.controller` 사용. **요청에 따라 플레이 실행·멀티클라이언트 검증은 하지 않았다.** 에디터에서 컴포넌트를 연결하기 위한 스크립트 임포트와 에셋 저장을 진행했다.

## 프리팹 연결

네트워크 스폰 대상은 기존 `Player.prefab`을 유지한다. 그 아래 `Player Visual`이라는 이름으로 `Player Test.prefab`을 중첩 연결했다. 기존 캡슐 렌더러와 Front 표시를 숨겼다. 플레이어 루트는 CharacterController 중심이고 모델 원점은 발 기준이므로 모델의 로컬 Y는 -1이다.

모델 Animator는 루트 모션을 끄고 Player Animator를 사용한다. 위치·회전은 기존 서버 CharacterController와 NetworkTransform이 담당한다. 별도 NetworkAnimator는 추가하지 않았다. 서버 전용 인스턴스는 Animator를 평가하지 않아도 이동·콤보·피해를 계산한다.

## 이동

서버가 수신한 카메라 기준 이동 입력 벡터의 크기를 0~1로 제한한다. 0.1 이하는 이동하지 않는다. 기존 최대 속도 5 × 입력 크기로 실제 이동한다.

| 입력 크기 | Animator 상태 | MoveBand |
|---|---|---|
| 0~0.1 | Idle | 0 |
| 0.1 초과~0.4 | Move State / Walk | 1 |
| 0.4 초과~0.75 | Move State / Run | 2 |
| 0.75 초과~1 | Move State / Sprint | 3 |

MoveAmount(float)는 서버가 전달한 입력 크기, MoveBand(int)는 표시 상태, AttackStep(int)은 0(공격 없음) 또는 1~3이다. 이동 상태 전환은 0.12초 블렌딩하며 큰 입력 변화에서는 Walk를 반드시 거치지 않고 해당 상태로 바로 전환한다. 벽에 막혀도 입력을 밀고 있으면 이동 애니메이션이 재생되는 입력 크기 기준이다.

PlayerAnimationView에서 Idle/Run/Sprint Threshold를 조정할 수 있다. Idle Threshold를 바꾸면 InputHandler의 Input Deadzone도 함께 조정한다.

## 콤보

1. Attack 버튼으로 Attack 1 시작.
2. Attack 1 도중 다시 클릭하면 시점에 관계없이 Attack 2를 한 번 예약.
3. Attack 2 도중 클릭하면 Attack 3을 한 번 예약.
4. 예약이 없으면 현재 공격을 끝낸 후 이동/Idle로 복귀. Attack 3은 무조건 종료하고 다음 공격은 Attack 1부터 시작.

버튼은 살아 있는 로컬 플레이어가 접속해 있는 동안 공격 중에도 계속 활성 상태다. 누르고 있기만 해서 연속 공격하지 않는다. 1·2타는 진행 중 언제든 다음 한 타만 예약하며, 이미 예약한 타의 중복 클릭과 3타 진행 중 클릭은 추가 타를 만들지 않는다. 서버가 공격 단계와 공격 식별자를 확인해서 이전 타의 지연된 입력이나 중복 요청을 다른 타에 적용하지 않는다.

공격 중 수평 이동·회전은 중단하되 수직 지면 처리는 유지한다. 조이스틱 입력은 계속 수신해서 공격이 끝나면 최신 입력 방향으로 이동한다. 사망 시 콤보와 이동을 중단한다.

원본 클립 재생 속도 1을 유지한다. 현재 Attack 1 약 2.633초, Attack 2 약 2.767초, Attack 3 약 2.167초다. 첫째·둘째 타 전체 구간에서 다음 타를 예약할 수 있다. 셋째 타에는 다음 타 예약이 없다.

## 피해와 동기화

각 타 진행률 35%에 서버가 한 번 범위를 검사한다. 피해 10, 거리 2.5, 전방 120도, 몬스터 대상만 적용하는 기존 규칙을 유지한다. 타당 같은 대상은 한 번만 맞는다. 세 타가 모두 맞으면 총 피해 30이다.

타격 시점 35%는 초기 임의 설정이며, 실제 검 궤적에 맞춘 시각 검증을 하지 않았다. PlayerCombat의 Hit Normalized Times에서 각 타의 시점을 조절한다. 공격 애니메이션 이벤트나 클라이언트 메시지가 직접 피해를 결정하지 않는다.

콤보 단계·공격 식별자·서버 시작 시각·다음 타 예약 상태를 하나의 NetworkVariable 스냅샷으로 전달한다. PlayerAnimationView가 클라이언트에서 Animator에 반영하며, 뒤늦은 접속이나 오래 지연된 스냅샷은 서버 시각에 맞춰 공격 재생 위치를 보정한다. 로컬 예측·지연 보상은 없다. 높은 지연에서는 공격이 끝나기 직전에 누른 요청이 서버에 늦게 도착하여 거절될 수 있다.

## 조절할 곳과 제한

- PlayerCombat: Attack Durations, Hit Normalized Times, Damage/Attack Range/Attack Angle.
- Attack Durations는 설정 시 Animator 클립 길이/상태 속도로 저장했다. 클립이나 상태 속도를 바꾸면 해당 값도 함께 변경해야 한다.
- Animator의 Attack State 기본 상태는 Attack 1, Move State 기본 상태는 Walk다. 공격 전환은 Any State의 AttackStep 조건으로 처리하여 늦게 접속한 클라이언트도 현재 공격 단계로 진입할 수 있다.
- 몬스터 반격·리스폰·사망 애니메이션·벽에 의한 공격 차단은 여전히 범위 밖이다.

## 클라이언트 콤보 로그

자신의 플레이어에 대해 서버가 확정한 새 공격 단계가 전달될 때 `[Combo] 1`, `[Combo] 2`, `[Combo] 3`을 Console에 한 번씩 출력한다. 다음 타 예약만 변경되는 경우나 매 프레임에는 출력하지 않는다. 서버 전용 인스턴스와 다른 플레이어의 콤보는 해당 로그를 출력하지 않는다.
