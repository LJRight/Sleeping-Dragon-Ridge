# 로컬 조작·전투 테스트

2026-11-17에는 서버 없는 테스트 씬과 콤보 전환을 구현했다. 11월 19일에는 회전 베기와 HUD를 추가하고 이 씬을 Unity Play 모드에서 검증했다. [회전 베기 검증 수치](verification/spinning-slash/skill-local.json)

## 사용 방법

1. `Assets/Scenes/Player Controll Test.unity`를 연다.
2. Multiplayer Play Mode 시나리오가 Bootstrap을 시작 씬으로 지정했다면 시나리오 실행을 해제하고 현재 씬을 직접 Play한다. 로컬 테스트에 Server/Client 태그는 필요 없다.
3. Game 창에서 조이스틱으로 이동하고 공격 버튼을 누른다. 로그인 서버와 게임 서버는 필요 없다.
4. 드래곤은 (0, 0, 12)에 배치되어 있으며 접근하면 기존 AI가 동작한다. 씬에서 위치와 컴포넌트 수치를 조정해 조작감을 비교할 수 있다.
5. 체력 소진 후에는 Play를 종료하고 다시 시작한다. 부활·자동 리스폰은 이번 범위에 포함하지 않았다.

## 구현

### 회전 베기

기본 공격 왼쪽 위의 **회전 베기** 버튼을 누른다. 1초 동안 캐릭터가 회전하고, 파란 원이 공격 반경을 표시한다. 시전 중에는 이동·회전 입력과 일반 공격을 잠그고, 0.37초에 반경 2.5m 안의 몬스터에게 현재 공격력으로 한 번씩 피해를 준다. 기본 공격은 바닥 표시 없이 전방 120도 부채꼴 안의 몬스터를 판정한다.

`Player Main`의 Animator가 `Spinning Slash Skill` 상태를 재생한다. `Spinning Slash` 클립의 `PlaySlash` 이벤트를 옮기면 파티클·효과음·기합의 재생 시점이 함께 바뀐다. 현재 상태의 Speed는 `2.1111112`로, 90 Samples인 190프레임 클립을 1초에 재생한다. 65프레임에 배치된 이벤트는 시전 후 약 0.34초에 도달한다. [회전 베기 상태](<../NetmarbleNeo/Assets/Animation/Player/Player Animator.controller>) · [이벤트 수신 코드](../NetmarbleNeo/Assets/Scripts/VFX/Slash.cs)

재사용 대기 시간은 시전 시작부터 5초다. 버튼의 원형 음영이 줄어들고 남은 초가 사라지면 다시 사용할 수 있다. 대기 시간이 끝나기 전에도 회전 동작을 마치면 이동과 기본 공격은 가능하다. `Player` 프리팹의 `Player Combat → Spinning Slash Cooldown`에서 대기 시간을 조정할 수 있다.

### 로컬 전투 구조

- `LocalCombatTest`가 정확한 씬 이름 `Player Controll Test`로 로컬 경로를 구분한다. 씬 이름을 변경할 때는 상수도 함께 수정한다.
- 플레이어와 드래곤을 씬에 직접 배치했다. NetworkManager, Bootstrap, 서버 스포너는 배치하지 않았다. NetworkTransform은 해당 씬 인스턴스에서만 비활성화했다.
- NetworkHealth, InputHandler, PlayerCombat 및 두 몬스터 AI는 로컬 상태와 Time.timeAsDouble을 사용한다. 로컬 입력은 RPC를 호출하지 않고, 로컬 상태는 NetworkVariable에 쓰지 않는다.
- 기존 Prototype의 Canvas, EventSystem, Plane, Global Volume을 복사하고 카메라 추적을 연결했다. HUD와 공격 범위 표시는 로컬 모드에서도 동작하도록 변경했다.
- 로그인 세션은 이 씬에서 네트워크 초기화를 건너뛴다.
- 플레이어 사망은 기존 프로토타입처럼 이동·공격 중지 및 애니메이션 정지다. 새로운 플레이어 사망 모션을 추가한 것은 아니다. 드래곤은 기존 사망 모션 후 로컬 오브젝트를 비활성화한다.

## 콤보 이벤트 규칙 (멀티플레이와 로컬 공통)

- 고정된 종료 0.4초 전 방식은 클립 이벤트 시간 기반으로 교체했다.
- Player 프리팹의 PlayerCombat.attackClips에는 Animator가 사용하는 1·2·3타 클립을 순서대로 연결했다. 1타와 2타 각각에 `ComboTransitionOpen`, `ComboInputClose` 이벤트를 정확히 하나씩 추가한다. 인수는 없다. 3타에는 필요 없다.
- 클립 경로는 `Assets/DoubleL/Demo/Anim/OneHand_Up_Attack_1_InPlace.anim`, `OneHand_Up_Attack_2_InPlace.anim`이다.
- 위치는 사용자가 직접 조절한다. 이번 작업은 이벤트를 임의 배치하지 않았다. 기존 1타의 이름 없는 이벤트는 원하는 이벤트 이름으로 지정하거나 제거해야 한다.
- 0 <= 전환 시점 < 마감 시점 <= 클립 길이여야 한다. 누락·중복·역순이면 경고하고 해당 타에서 다음 타 연결을 막는다. 단독 공격은 가능하다.
- 전환 전 입력은 한 번 예약하고 추가 입력은 무시한다. 예약이 있으면 전환 시점에 다음 타를 시작한다. 전환 이후 마감 이전 입력은 서버에서 수신·확정한 즉시 다음 타로 전환한다. 마감 시각부터 입력을 무시한다.
- 서버와 로컬은 Awake에서 AnimationClip.events를 읽어 시간을 캐시한다. Animator가 꺼진 서버도 동일하게 처리한다. 이벤트 위치 변경 후 Play를 다시 시작해야 한다.
- Animator GameObject에 ComboAnimationEvents 수신 컴포넌트를 추가했다. 콜백은 의도적으로 전투 상태를 바꾸지 않는다. 블렌딩 중 이전 클립의 이벤트가 도착해도 새 타의 입력을 닫지 않도록, 실제 판정은 오직 전투 시계가 담당한다.
- 현재 Animator.speed 및 공격 상태 speed는 1 기준이다. 상태 속도·속도 파라미터를 변경하면 시간 변환도 별도 구현해야 한다.
- 피해 시점은 기존 클립 진행률 35% 설정이다. 이보다 이르게 콤보 전환을 배치하면 이전 타의 아직 발생하지 않은 피해는 취소된다. 타격 이후 전환을 원하면 35% 이후에 전환 이벤트를 놓는다.
- PlayerAnimationView.attackBlendSeconds 기본 0.12초로 블렌딩한다. 3타와 다음 입력 없는 공격은 끝까지 재생한다. 단계 숫자 로그는 유지한다.
- 클립 수정 내용을 멀티플레이에 반영하려면 서버와 클라이언트를 모두 다시 빌드한다. 클라이언트 이벤트로 서버 판정을 요청하지 않으며 서버 수신 기준으로 마감을 판단한다.

## 확인 범위

2026-11-17에는 에디터에서 로컬 씬을 저장하고 구성과 참조를 확인했다.

2026-11-19에는 Unity Play 모드와 macOS 독립 실행 빌드에서 HUD 버튼을 눌러 회전 베기를 검증했다. 정면과 후방의 몬스터는 각각 한 번 맞았고 반경 밖의 몬스터는 피해를 받지 않았다. 시전 중 조이스틱을 끝까지 밀어도 플레이어의 위치와 방향은 유지됐다. 시전을 마친 뒤 기본 공격은 120도 부채꼴로 돌아왔고, 회전 베기는 5초가 지나고 다시 사용할 수 있었다. 2.63초가 남았을 때 버튼은 숫자 3과 약 53%의 음영을 표시했다. [시전 화면](verification/spinning-slash/skill-casting.png) · [재사용 대기 화면](verification/spinning-slash/skill-cooldown.png) · [macOS 측정값](verification/spinning-slash/skill-local.json)

같은 날 macOS 전용 서버에 독립 실행 클라이언트 두 개로 접속했다. 서버에서 시전자 뒤 1.75m에 배치한 몬스터의 체력이 28에서 18로 줄었고, 4.30m에 둔 몬스터의 체력은 100을 유지했다. 두 클라이언트는 같은 시전 시작 시각과 회전 동작, 검기, 파란 원을 확인했다. 재사용 대기 표시는 시전자의 버튼에만 나타났고, 연타 중 서버가 승인한 두 시전 사이의 간격은 5.046초였다. Android·iOS 실기기 검증은 남아 있다. [멀티플레이 시전자 화면](verification/spinning-slash/skill-field.png)
