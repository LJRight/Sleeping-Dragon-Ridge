# Mossfang Goblin 제작 기록

2026-11-17 사용자가 Blender 설치 후 저폴리 고블린 모델·리깅·이동 및 공격 애니메이션 제작 진행을 승인했다. 설치된 Blender 5.3.0을 사용했다. 외부 모델·텍스처·모션 없이 Python으로 메시와 스킨 가중치, 골격 키프레임을 작성한 원본 프로토타입이다.

## 결과물

- 원본: `ArtSource/Goblin/Mossfang_Goblin.blend`
- 제작 스크립트: `ArtSource/Goblin/create_goblin.py`
- Unity 폴더: `NetmarbleNeo/Assets/Art/Goblin`
- Unity 프리팹: `Mossfang Goblin.prefab`
- FBX: `Goblin_Model.fbx`, `Goblin_Walk.fbx`, `Goblin_Attack.fbx`
- 미리보기: `ArtSource/Goblin/previews/Goblin_Walk.gif`, `Goblin_Attack.gif` 및 동일 이름 MP4

초록 피부, 큰 귀와 엄니, 가죽 장비, 한쪽 어깨 철제 갑옷과 오른손 도끼를 가진 근접 몬스터다. 메시 1개, 정점 1,386개, 삼각형 2,308개, 본 18개, 머티리얼 1개와 64×64 팔레트 텍스처로 구성했다. 렌더링용 바닥·조명·카메라는 Blender 원본에만 있고 FBX에는 포함하지 않았다.

## 모션과 사용 방법

| 클립 | 길이 | 동작 |
|---|---|---|
| Walk | 약 1.07초, 반복 | 제자리 걷기, 발 들기, 골반 상하·좌우 움직임, 상체 비틀기와 팔 반동 |
| Attack | 1.5초, 단발 | 준비 → 도끼 들기 → 전진하며 내려찍기 → 회복 |

Unity Project 창에서 각 애니메이션 FBX의 Animation 미리보기를 재생해 확인할 수 있다. 별도 프리팹의 Animator는 `Goblin Preview.controller`를 사용한다. 기본 상태는 Walk이고 `Attack` Trigger를 받으면 공격을 재생한 뒤 Walk로 돌아온다. 이는 아트 확인용 구성이다. 기존 몬스터 프리팹·씬·AI·네트워크·피격 로직에 연결하지 않았다.

리그는 Generic이며 Humanoid 리타게팅용 Avatar가 아니다. 두 클립 모두 제자리 모션이고 프리팹의 Apply Root Motion은 꺼져 있다. Walk의 기준 이동 속도는 약 0.56m/s이다. 실제 몬스터 속도가 더 빠르면 클립 재생 속도나 보폭을 맞춰야 발 미끄러짐을 줄일 수 있다. Attack의 의도한 타격 시점은 시작 후 약 0.67초이다. Animation Event와 서버 타격 타이밍은 아직 연결하지 않았다. Idle·피격·사망은 이번 제작 범위에 포함하지 않았다.

Blender에서는 `Goblin_Rig`를 선택하고 Dope Sheet의 Action Editor에서 `Goblin_Walk` 또는 `Goblin_Attack`을 선택한다. 30fps 기준 Walk는 1~33프레임, Attack은 1~46프레임이다. Unity에서는 FBX 시간 변환 후 길이를 기준으로 사용한다.

## 확인한 내용과 한계

- Blender에서 두 모션 전체 프레임을 렌더링하고 주요 자세 시트를 육안 확인했다.
- 모든 정점에 가중치가 있으며 정점당 최대 본 영향은 2개였다.
- Walk 시작·끝 본 행렬이 일치하며 루트 이동은 0이었다.
- 세 FBX를 Blender로 다시 읽어 메시 1개·본 18개 및 해당 모션 존재를 확인했다.
- Unity에서 Generic Avatar 유효성과 Walk 약 1.07초/반복, Attack 1.5초/비반복 임포트를 확인했다.
- 수치 결과: `ArtSource/Goblin/asset_report.json`, `checks.json`.

게임 Play Mode, 실제 서버 동기화, 모바일 성능은 검증하지 않았다. 모션은 직접 구성한 스타일화된 첫 버전으로, 모션 캡처나 전문 애니메이터의 최종 폴리싱 결과를 뜻하지 않는다. 실제 게임 카메라 거리·이동 속도·전투 타이밍에 맞춘 사용자 검토와 조정이 남아 있다.

## 재생성

저장소 루트에서 다음 명령을 실행하면 원본과 FBX·텍스처를 다시 생성한다. 수동 수정한 원본/FBX도 덮어쓰므로 수정본은 먼저 별도 보관한다. Unity 프리팹·머티리얼·컨트롤러는 이미 저장된 에셋을 사용한다.

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --python ArtSource/Goblin/create_goblin.py
```

`render_motion.py`는 저장된 원본에서 모션 프레임 이미지를 렌더링하며, `check_asset.py`는 가중치·루프·FBX 재임포트 결과를 기록한다.
