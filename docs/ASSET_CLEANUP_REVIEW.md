# 용이 잠든 능선 · 제출 전 에셋 정리 검토

**Git 적용 결과 (2026-11-24)**  
현재 활성 빌드 씬 6개의 Unity 의존 목록, 동적 리소스, 빌드 설정과 Android·iOS·Windows 서버 빌드를 대조해 [에셋 제외 규칙](../NetmarbleNeo/Assets/.gitignore)을 적용했다. Git 업로드 대상은 에셋 원본 517개이며, 1,658개(약 2.61 GiB)와 대응하는 메타데이터는 로컬에 보관한다. 별도로 [저장소 제외 규칙](../.gitignore)은 Unity 캐시, 빌드 산출물, 로컬 DB 및 제작용 미디어를 제외한다. 아래 표와 CSV는 적용 전 조사 기록이다.

2026-11-24 기준. Unity `Assets`의 원본 2,177개(3.86 GiB)를 조사했다. 이 중 **1,530개(2.47 GiB)**는 현재 배포 경로에서 쓰인 근거가 없어 별도 보관을 검토할 수 있다. 이 용량은 로컬 원본의 합계다. APK나 Git 전송 용량이 같은 만큼 줄어든다는 뜻으로 사용할 수 없다. 이번 작업으로 생성한 파일은 이 문서와 [파일별 검토표](ASSET_CLEANUP_INVENTORY.csv) 두 개다. 에셋 삭제·이동, 빌드 설정 변경, Git 설정 적용은 승인 대기다.

## 1. Git에 올릴 게임 원본에서 분리할 후보

다음 표의 경로는 Unity의 `Assets/` 아래다. 여러 용도로 쓰는 팩은 CSV에서 **외부 보관 후보**로 표시된 개별 파일만 집계했다. 제작 과정에서 사용한 파일, 고지문, 계속 보관할 에셋이 참조하는 파일은 따로 분류했다.

| 경로 | 후보 수 | 원본 MiB | 분리할 때 보존할 것 |
| --- | ---: | ---: | --- |
| `TerrainSampleAssets/Textures/Terrain/` | 36 | 936.3 | 실사용 지표면인 `Grass_Moss`, `Pebbles_C`, `Rock`, `Soil_Rocks`의 BaseColor·Normal·MaskMap 12장 |
| `TerrainSampleAssets/Textures/Heightmaps/`와 `TerrainBrushes/` | 42쌍 | 549.5 | 높이맵과 해당 브러시를 함께 보관한다. 사냥터 제작에 사용한 `Canyon03`과 `Ridge03` 두 쌍은 제작 원본으로 보존한다. [제작 기록](TERRAIN_TEST_SCENE.md) |
| `AllSkyFree/` | 132 | 311.1 | 하늘 샘플과 데모다. 별도 집계한 공급자 PDF도 팩과 함께 보관한다. 마을·사냥터·보스방이 사용하는 하늘은 `Scenes/TerrainTestScene/Highland Daylight.mat`다. |
| `FreeDragons/Texture/` | 51 | 274.0 | Nightmare 16장(105.0 MiB), TerrorBringer 20장(89.4 MiB) 포함. Boar·SoulEater·보스의 배포 재질과 공용 텍스처는 보존한다. |
| `Art/Environment/Texture/` | 18 | 118.2 | 배포 빌드에 나타난 건물·계단 등의 텍스처 43장은 보존한다. |
| `DoubleL/` | 201 | 114.2 | `Demo` 아래에도 실제 캐릭터가 사용하는 애니메이션이 있다. `Demo` 폴더 전체 제외는 부적합하다. |
| `Fire/` | 80 | 78.8 | 현행 이펙트가 참조하는 플레어 텍스처·재질과 보관할 원본의 의존 파일을 보존한다. |
| `Audio/Monster Roars/`와 `Audio/Monster SFX - 111518/` | 156 | 46.3 | 별도 집계한 `Notes.txt`도 원본과 함께 보관한다. 공격·사망·포효 등에서 재생하는 `Audio/Combat/Enemy/`의 복사본은 보존한다. |
| `Footsteps - Essentials/` | 469 | 34.5 | `Footsteps_DirtyGround/Footsteps_DirtyGround_Walk/`의 WAV 10개를 보존한다. `Player Main`이 이 10개를 직접 참조한다. |
| 기타 지형 샘플, 드래곤 샘플, `Eric VFX Studio`, `Joystick Pack` | 303 | 69.2 | CSV의 파일 목록을 기준으로 검토한다. 이펙트의 원본 메시·머티리얼과 실제 조이스틱 코드·프리팹은 팩 이름만으로 제외할 수 없다. |
| **합계** | **1,530** | **2,532.0** | 파일의 원본 크기 기준. 대응하는 `.meta`는 수와 용량에서 제외했다. |

첫 승인 범위로는 **하늘 샘플, 높이맵·브러시 42쌍, 발소리 469개**가 적합하다. 이 세 묶음의 후보만 약 **895 MiB**다. 통째로 삭제할 필요 없이 프로젝트 밖에 원래 상대 경로와 `.meta`를 유지해 보관할 수 있다. 파일 일부를 옮길 때는 계속 쓰는 폴더의 `.meta`를 남긴다.

## 2. 현재 빌드에 들어가지만 제외를 검토할 항목

| 대상 | 확인한 근거 | 승인 후 검토할 변경 |
| --- | --- | --- |
| `FreeDragons/Texture/DragonBoarPBR/Green/Metallic.png` | iOS 비압축 약 **21.3 MiB**. 실제 Blue·Gold 재질 모두 Metallic 워크플로를 쓰면서 초록 텍스처를 `_SpecGlossMap`에도 참조한다. | Blue·Gold의 활성 `_MetallicGlossMap`을 보존하고 미사용 슬롯 참조만 해제한 뒤, 실제 외형과 다음 빌드에서 포함 여부를 비교한다. 초록 원본은 먼저 보관한다. [Blue 재질](../NetmarbleNeo/Assets/FreeDragons/Materials/DragonBoarPBR/BluePBR.mat) · [Gold 재질](../NetmarbleNeo/Assets/FreeDragons/Materials/DragonBoarPBR/GoldPBR.mat) |
| Windows 전용 서버의 `Assets/Audio/` | 전체 사용자 에셋 24.9 MiB 중 **약 13.33 MiB**가 이 폴더의 오디오다. 로그인·마을·사냥터·보스 BGM 네 곡이 약 11 MiB다. | 전용 서버 패킹 단계에서 클라이언트용 오디오 데이터가 빠지도록 별도 검토한다. 화염·승리음 등 클라이언트가 들을 효과음과 판정에 필요한 애니메이션 타이밍은 보존한다. |
| `Art/Expedition UI/input_field.png` | iOS 19.4 KiB. 기기 토큰 로그인으로 바뀐 뒤에도 `KnightUIArt.asset`의 스프라이트 목록에 남아 있다. 사용처를 찾지 못했다. | UI 카탈로그에서 이 항목만 분리한다. 장비 등급 프레임과 실제 로그인 버튼은 유지한다. [UI 카탈로그](../NetmarbleNeo/Assets/Resources/KnightUIArt.asset) |
| 구형 `Prefabs/Player.prefab`과 `Prefabs/Dummy Monster.prefab` | iOS에서 각각 12.3 KiB와 0.9 KiB. `WorldSession`은 `Player Main`을 생성하지만 네트워크 등록 목록에는 구형 두 개도 남아 있다. | 배포용 등록 목록을 클라이언트·서버에 동일하게 적용한다. 현행 Player Main·Boar·SoulEater·Boss 등록 및 구형 플레이어를 쓰는 테스트 씬은 보존한다. [등록 목록](../NetmarbleNeo/Assets/DefaultNetworkPrefabs.asset) |
| `Resources/UI/HUD/PrototypeNameplate.prefab` | iOS 0.9 KiB. APK에도 있다. 현재 배포 대상에서 빠진 `Prototype` 씬의 주민 이름표로만 호출된다. | 시제품의 참조를 살리면서 `Resources` 밖으로 분리한다. 다른 UI 프리팹 38개에는 코드 또는 씬·프리팹 참조가 있다. |
| TMP의 `LiberationSans SDF - Outline.mat` | iOS 1.6 KiB. APK에도 있다. 프로젝트에서 직접 참조나 이름 로드를 찾지 못했으나 TMP 기본 리소스다. | 우선순위가 낮다. 패키지 쪽 사용 여부를 확인한 뒤 결정한다. 이름이 비슷한 `- Drop Shadow.mat`은 로그인 씬에서 사용한다. |

위 용량은 Unity가 보고한 파일별 **비압축** 근삿값이다. 실제 다운로드 절감량은 같은 플랫폼·설정으로 변경 전후 빌드를 비교해야 알 수 있다. 네트워크 프리팹 수치는 프리팹 자체 크기다. `Resources` 아래의 리소스는 코드 호출이 없어도 플레이어에 포함될 수 있다. [Unity의 Resources 설명](https://docs.unity3d.com/6000.0/Documentation/Manual/LoadingResourcesatRuntime.html)

## 3. 별도로 보존할 자료

- **제출 씬과 제작 씬.** 배포 씬은 `Bootstrap`, `Login Scene`, `BossRoom`, `HuntingField`, `village`, `WorldSession` 여섯 개다. `Prototype`, `Player Controll Test`, `TerrainTestScene`, `Spinning Slash Preview` 네 씬은 제작·검증 기록이다. `Scenes/TerrainTestScene/` 하위 파일 **18개**는 iOS에도 실렸고, 마을·필드·보스가 하늘·바위·지형을 공유한다. [배포 씬 설정](../NetmarbleNeo/ProjectSettings/EditorBuildSettings.asset) · [프리뷰 제작 기록](LOCAL_COMBAT_TEST.md)
- **동적으로 읽는 자산.** `Resources/UI`, 장비 등급별 프레임, 목적지별 로딩 일러스트, 음향·몬스터·플레이어 공격 밸런스 카탈로그, 점프 공격 이동 데이터는 사용처를 보존한다. 모바일 아이콘 3종, 앱 게스트 토큰을 다루는 Android·iOS 플러그인, 빌드 후처리·밸런스 편집기, `Packages/manifest.json`, `packages-lock.json`, `ProjectSettings/` 및 유지할 에셋·폴더의 `.meta`도 프로젝트 복원에 필요하다.
- **이전 시안과 재편집 원본.** `Audio/AI Generated/DragonAwakens.wav`와 `RidgeHunt.wav`(합계 49.6 MiB), 구 버튼 이미지 `combat_basic.png`·`combat_spin.png`, 일부 피격음 변형, 스킬 애니메이션 원본 FBX는 빌드 미등재와 별개로 사용자가 보관 여부를 결정할 항목이다. `LoginKnight.png`는 기획 문서에 원화로 직접 연결돼 있다. `ArtSource/`, `docs/verification/`, `outputs/`의 기획 엑셀 두 개도 포트폴리오와 재편집 자료로 보관한다. [기획 문서](PROJECT_PLAN.md)
- **공개 Git의 제3자 원본.** CSV의 ‘프로젝트 보존’은 로컬 및 허가된 작업 사본 기준이다. 공개 Git에는 각 팩의 원본 재배포 허용 범위를 따로 확인한다. 공개 권한을 확인하지 못한 필수 팩은 별도로 보관하고, 저장소에서 다시 설치할 수 있게 출처·버전·필요 경로와 프로젝트에서 수정한 부분을 기록하는 방안을 권한다. `TerrainSampleAssets/ThirdPartyNotices.txt`, `LiberationSans - OFL.txt`, `EmojiOne Attribution.txt` 등은 해당 자료와 함께 보존한다. [지형 팩의 개별 고지](../NetmarbleNeo/Assets/TerrainSampleAssets/ThirdPartyNotices.txt) · [Unity Asset Store 약관 부록 1 §2.2](https://unity.com/legal/as-terms)

## 4. Assets 밖에서 Git 업로드만 제외할 항목

루트와 Unity 프로젝트에서 `.git`이나 공용 `.gitignore`를 찾지 못했다. `localServer/.gitignore`는 존재한다. 아래는 첫 Git 등록 시 적용할 제안이다.

| 제외 권장 경로 | 보관 기준 |
| --- | --- |
| `NetmarbleNeo/Library/`, `Temp/`, `Logs/`, `UserSettings/`, `.utmp/`, IDE 생성 `*.csproj`, `*.slnx`, 크래시 로그, `.DS_Store` | Unity 및 개인 작업 환경의 생성물. 조사 중 Android 빌드가 진행되면서 `Library`만 10 GiB 이상이었다. |
| `Build/ios/`, `iOSClientBuild/`, `ServerBuild/`, `Build/android/`, `Build/ServerRoot/GameServer/`, `ServerLogs/` | 빌드 결과·디버그 심볼·서버 로그. 완성 APK, Xcode 산출물, 전용 서버 실행본은 릴리스 파일로 별도 보관한다. `.apk.cpgz`도 제외한다. 수동 변경한 Xcode 설정이 있다면 먼저 복원 방법을 보존한다. |
| `localServer/data/`, `Build/ServerRoot/localServer/data/`, `.env`, Python 캐시, 로컬 서명 키·인증서 | 개발 계정과 기기 토큰이 든 실제 SQLite DB, 실행 시 생성되는 개인 값은 소스 Git에서 제외한다. Python 코드와 보상·퀘스트 정의 JSON은 유지한다. |
| `NetmarbleNeo/Assets/Resources/PerformanceTestRunInfo.json`, `PerformanceTestRunSettings.json` 및 각 `.meta` | 성능 테스트 패키지가 빌드 때 만들고 정리하는 임시 파일이다. Git에서만 제외해도 다음 빌드 때 패키지가 다시 생성할 수 있다. |

**`Build/` 전체에 무시 규칙을 걸면 Windows 서버 이관 파일도 빠진다.** `Build/ServerRoot/start-servers.cmd`, `run_servers.py`, `README.md`, `localServer/` 안의 배포용 소스와 정의 파일은 유지한다. 루트의 `localServer/`와 `start-servers.command`도 유지 대상이다. [Windows 서버 실행 안내](../Build/ServerRoot/README.md)

## 조사 범위와 검토표 읽는 법

- `Assets/` 원본을 파일별로 조사했다. 숨김 파일·폴더와 `.meta`는 파일 수와 용량에서 제외했고, `.meta`의 GUID와 임포터 참조는 분석에 사용했다. 성능 테스트 임시 파일 2개는 조사 시점에 있어 표에 포함했다.
- 씬·프리팹·재질·컨트롤러 등에서 읽을 수 있는 GUID 연결을 따라가고, 프로젝트 설정, 코드의 동적 로딩·에디터 경로, 제작 문서 링크, 플랫폼별 빌드 입력 캐시와 대조했다. 보관·보류 원본의 확인된 의존 파일도 외부 보관 후보에서 제외했다. CSV의 직접 참조 수는 확인한 `Assets/` 파일 사이의 참조 수다. 0개인 행에도 코드·네이티브·문자열 참조가 있을 수 있다.
- iOS는 11월 24일 **11:25:52**, Windows 전용 서버는 **11:27:45** 성공 빌드의 상세 보고서에 나온 `Assets/` 경로 470개·464개를 사용했다. CSV의 ‘미등재’는 해당 파일이 상세 목록에 나타나지 않았다는 의미다. 컴파일된 스크립트·셰이더 묶음·OS 아이콘처럼 개별 경로가 보이지 않는 자료도 있다.
- Android는 **02:26 성공 APK** 안에서 확인한 씬 6개와 `Resources` 경로 59개를 표시했다. 해당 성공 로그에는 전체 파일별 크기가 없다. Android의 ‘미확인’은 미사용 판단의 근거로 사용하지 않았다. 이후 진행 중이던 Android 빌드의 입력 경로는 보존 쪽 근거로만 사용했다.
- CSV의 ‘외부 보관 후보’를 선택하면 1,530행을 볼 수 있다. 그 밖의 분류는 유지 551개, 제작·테스트 보관 38개, 문서·권리 보관 12개, 개별 검토 38개, 빌드 제외 검토 6개, Git 제외 자동 생성 2개다. 서버 오디오 분리는 여러 유지 파일에 걸치는 플랫폼별 제안이어서 별도 파일로 중복 집계하지 않았다.
