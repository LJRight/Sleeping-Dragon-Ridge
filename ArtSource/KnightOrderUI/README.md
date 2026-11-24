# 용흔의 능선 UI 이미지

가로 모바일 수동 전투를 위한 흑철·은빛 기사단 UI 세트. 실제 스프라이트는 `PNG/`, 화면에 조립한 예시는 `Previews/`에 있다. 스프라이트에는 글자와 수치가 들어 있지 않다.

| 폴더 | 구성과 용도 |
|---|---|
| `PNG/Combat/` | 조이스틱 받침과 손잡이, 기본 공격, 단일 찌르기, 범위 베기, 회복 물약 |
| `PNG/Status/` | 체력과 경험치 게이지, 현재 목표 패널 |
| `PNG/Growth/` | 일반·레어·전설 장비 슬롯, 장착 표시, 비교 패널 |
| `PNG/Common/` | 로그인·회원가입 입력칸, 기본·보조 버튼, 공통 창, 승리·패배 문장식, 일시정지 버튼 |

## 시안

| 파일 | 용도 |
|---|---|
| `01_Combat_19.5x9.png` | 2340×1080. 좌우 조작부를 화면 가장자리에서 안쪽으로 옮기고 경험치바 아래에 홈 표시기 여백을 둔 예시 |
| `02_Combat_16x9.png` | 1920×1080. 같은 조작부를 양쪽 모서리에 고정해 중앙 전투 공간을 확보한 예시 |
| `03_Login_16x9.png` | 기존 기사 원정대 배경과 ‘용흔의 능선’ 로고를 그대로 배치한 로그인 창 |
| `04_Equipment_16x9.png` | 기존 장비 아이콘 9개, 등급·장착 표시, 검 교체 전후의 능력치 |
| `05_Victory_16x9.png`, `06_Retry_16x9.png` | 한 공통 창에 결과 문장식과 동작 버튼을 교체한 예시 |
| `07_UIKit.png` | 주요 이미지의 용도와 조합을 한눈에 보는 견본 |

배경 원본은 `ArtSource/LoginBackground/KnightExpedition.png`, 타이틀은 `ArtSource/GameLogo/용흔의 능선.png`, 장비 원본은 `ArtSource/EquipmentIcons/`에 있다. 전투 화면의 능선과 드래곤은 배치 검토용 생성 배경이다. 시안의 이름·수치·문구와 화면 여백은 예시이며, 실기기 실행 검증은 별도다.

## Unity에서 조립하기

- 개별 PNG를 프로젝트에 가져와 **Sprite (2D and UI) · Single**로 설정한다. UI 안의 한글, 레벨, 현재 체력 등은 별도 텍스트로 표시한다.
- 공통 창·버튼·입력칸은 Sprite Editor에 아래 테두리 값을 넣고, **Mesh Type = Full Rect**, UI Image의 **Type = Sliced**로 표시한다. 네 모서리의 비늘과 베벨을 보존하면서 가운데 영역의 폭과 높이를 조정할 수 있다.
- 장비 슬롯은 바탕 → 기존 장비 이미지 → 등급 프레임 → 장착 표시 순서로 겹친다. 세트와 등급은 독립적으로 바꿀 수 있다. `Combat`의 조이스틱은 받침과 손잡이의 중심을 맞추고 손잡이만 움직인다.
- 실제 가로 화면에서는 조작부를 기기의 `Screen.safeArea` 안에 배치한다. 스프라이트의 픽셀 크기와 버튼의 터치 영역은 독립적으로 정할 수 있다.
- 모든 개별 PNG의 너비와 높이는 4의 배수다. ETC2는 4×4 블록을 쓰며 ASTC는 블록 크기를 선택할 수 있다. 원본 PNG의 규격과 기기에서 선택할 GPU 압축 포맷은 별도로 설정한다. [Unity 텍스처 포맷](https://docs.unity3d.com/6000.0/Documentation/Manual/texture-formats-reference.html)

| 공통 스프라이트 | 좌 | 하 | 우 | 상 |
|---|---:|---:|---:|---:|
| panel_common | 36 | 36 | 36 | 36 |
| input_field | 22 | 15 | 22 | 15 |
| button_primary | 28 | 20 | 28 | 20 |
| button_secondary | 36 | 18 | 36 | 18 |

목표 패널은 가로로 늘릴 때 양옆 40px를 유지한다. 비교 패널의 기준 크기는 768×512이며 텍스트 여백은 좌우 72px, 상하 52px를 권장한다.

### 게이지 채움 위치

동일한 접두어의 `_track.png`를 배경에 두고 `_fill.png`를 위에 올린다. 좌표는 트랙의 왼쪽 위에서부터 잰 원본 픽셀이다. UI Image의 **Type = Filled**, **Fill Method = Horizontal**, **Fill Origin = Left**를 사용하면 채움 비율에 따라 왼쪽부터 표시된다. 트랙과 채움은 함께 확대·축소한다. [Unity UI Image](https://docs.unity3d.com/Packages/com.unity.ugui@2.0/api/UnityEngine.UI.Image.FillMethod.html)

| 접두어 | 트랙 | 채움 시작 x, y | 채움 크기 |
|---|---:|---:|---:|
| player_hp | 512×64 | 66, 24 | 400×16 |
| boss_hp | 768×96 | 108, 46 | 552×20 |
| experience | 768×24 | 28, 8 | 712×8 |

### 제작 기록

Codex 내장 ImageGen으로 금속 장식과 아이콘을 만들고, 각 요소를 독립적인 알파 PNG로 정리했다. 시안에서는 최종 PNG, 기존 프로젝트 그림, 별도 텍스트 레이어를 조립했다. 사용한 프롬프트는 [생성 기록](GenerationPrompts.txt)에 모았다.
