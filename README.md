# 용이 잠든 능선

마을에서 의뢰를 받아 드래곤을 사냥하고, 장비를 강화해 능선의 수문장에 도전하는 모바일 쿼터뷰 액션 RPG입니다. 일반 공격 3타와 세 가지 스킬로 직접 전투하거나 자동 사냥을 켤 수 있습니다. 보스는 혼자 또는 최대 4인 파티로 도전합니다.

Unity 6000.3.10f1과 Netcode for GameObjects로 만들었습니다. Unity 전용 서버가 이동·타격·보스 패턴을 판정하고, Python·SQLite 백엔드가 기기별 게스트 계정과 퀘스트·장비·거래를 저장합니다.



## AI와 함께 만든 과정

공격이 답답하다는 플레이 피드백을 어떻게 서버 판정 코드로 옮겼는지, 장비 교환에서 같은 물건이 두 번 넘어가는 상황을 어떻게 시험했는지 정리했습니다. 각 사례에서 제가 내린 결정과 AI에 맡긴 작업을 실제 코드·검증 기록과 함께 볼 수 있습니다. [AI 협업 사례와 개발 대화 기록](docs/AI_COLLABORATION.md) · [프로젝트 협업 규칙과 결정 기록](AGENTS.md)

## 플레이 데모

- [일반 플레이 영상 보기](https://youtu.be/bKdLibyQhYE)
- [멀티플레이 보스전 영상 보기](https://youtu.be/0eIhy1JeAC8)

## 다운로드

[Google Drive에서 전체 빌드 보기](https://drive.google.com/drive/folders/1UMrcuIIOONqgl8K_5yOPpZ_tQqiBUcCG?usp=share_link)

| 플랫폼 | 배포 파일과 실행 방법 | Google Drive |
| --- | --- | --- |
| Android | ARM64용 `1.0.0.apk`를 휴대폰에서 열어 설치합니다. 설치 권한을 묻는 경우 파일을 연 브라우저나 파일 앱에 허용합니다. | [APK 받기](https://drive.google.com/file/d/1HbNAsPmYrP-qzWRJv2-AzTUxYUosEWtK/view) |
| iOS | Unity Hub 기본 경로에 Unity 6000.3.10f1이 설치된 Mac에서 `Ridge-iOS-Xcode.zip`을 풉니다. `Ridge-iOS/Unity-iPhone.xcodeproj`를 Xcode로 열고 본인의 서명 Team과 연결한 iPhone을 선택해 **Run**합니다. | [Xcode 프로젝트 받기](https://drive.google.com/file/d/1A3Tm3hOiFaHPa_q33OWxsY_iiy5R8WuH/view) |
| Windows 서버 | x64용 `Ridge-Server-Windows-x64.zip`을 풀고 `ServerRoot/start-servers.cmd`를 실행합니다. Python 3.11 이상이 필요합니다. | [서버 ZIP 받기](https://drive.google.com/file/d/1_0nzV0BbbpsKiDZFrmZT88wDpw2lTvow/view) |

**플레이만 하려면** 운영자 서버가 켜져 있을 때 모바일 앱을 실행하세요. 앱이 `binaryRight.iptime.org`에 접속해 게스트로 로그인합니다. Windows 서버 ZIP은 직접 서버를 운영할 때 사용합니다.

## 내 Windows PC와 휴대폰으로 테스트

### 1. PC에서 서버 실행

1. Windows x64 PC에 Python 3.11 이상을 설치합니다. 명령 프롬프트에서 `py -3 --version` 또는 `python --version`으로 확인합니다.
2. 서버 ZIP을 쓰기 가능한 폴더에 풀고 **`ServerRoot/start-servers.cmd`**를 더블클릭합니다. 로그인 서버가 준비되면 게임 서버가 시작됩니다. 서버 간 인증 키는 실행기가 생성하고 공유합니다.
3. PC 브라우저에서 `http://127.0.0.1:8080/health`를 열어 `"status":"ok"`를 확인합니다. 실행 창에서 `Ctrl+C`를 누르면 두 서버가 종료됩니다. 계정과 진행도는 `ServerRoot/localServer/data/auth.sqlite3`에 저장됩니다.

### 2. 휴대폰에서 닿는 주소와 포트 설정

| 테스트 환경 | 모바일 빌드에 넣을 호스트 | 서버 쪽 설정 |
| --- | --- | --- |
| 같은 집·사무실 LAN | PC에서 `ipconfig`로 확인한 IPv4. 예: `192.168.0.23` | PC와 휴대폰을 같은 LAN에 연결하고 Windows 방화벽에서 **TCP 8080**과 **UDP 7777** 인바운드를 허용합니다. |
| 휴대폰 데이터 등 외부망 | 내 공유기 공인 IPv4를 가리키는 DDNS 호스트 | 위 방화벽 설정에 더해 서버 PC의 내부 IP를 예약하고 공유기에서 **외부 TCP 8080 → PC TCP 8080**, **외부 UDP 7777 → PC UDP 7777**을 전달합니다. |

실행기는 두 서비스를 해당 포트에서 수신하게 합니다. Windows 방화벽과 공유기 규칙은 서버 운영자가 설정합니다. 휴대폰 브라우저에서도 `http://호스트:8080/health`가 열리면 로그인 경로가 연결된 것입니다. UDP 게임 접속은 앱에서 마을에 입장하고 조이스틱으로 움직여 확인합니다.

<details>
<summary>Windows 방화벽에서 두 포트 허용하기</summary>

서버 PC의 집 네트워크 프로필이 **개인(Private)**이라면 관리자 PowerShell에서 한 번 실행합니다. 같은 LAN에서만 시험할 경우 각 명령 끝에 `-RemoteAddress LocalSubnet`을 붙이면 됩니다.

```powershell
New-NetFirewallRule -DisplayName "Ridge Login TCP 8080" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8080 -Profile Private
New-NetFirewallRule -DisplayName "Ridge Game UDP 7777" -Direction Inbound -Action Allow -Protocol UDP -LocalPort 7777 -Profile Private
```

</details>

### 3. 내 서버용 모바일 앱 빌드

배포 APK의 접속 주소는 빌드할 때 정해집니다. 다른 PC의 서버를 시험하려면 **모바일 앱의 `Host` 한 줄을 변경하고 앱을 다시 빌드**합니다. 서버 실행본은 그대로 쓸 수 있습니다.

1. Unity Hub에서 **6000.3.10f1**로 저장소 안쪽의 **`NetmarbleNeo/`** 폴더를 엽니다. 대상에 맞는 Android Build Support(SDK·NDK·OpenJDK) 또는 iOS Build Support가 필요합니다. 원본 공개가 제한된 에셋 팩을 별도 제공하는 경우 먼저 같은 경로에 복원합니다. [소스 구성과 에셋 안내](docs/REPOSITORY_LAYOUT.md#무엇을-어디에-올리나)
2. [MobileServerEndpoint.cs](NetmarbleNeo/Assets/Scripts/MobileServerEndpoint.cs)의 `Host`를 PC의 LAN 주소 또는 본인의 DDNS 이름으로 바꿉니다. 예를 들어 `public const string Host = "192.168.0.23";`처럼 **호스트만** 입력합니다. 로그인 URL, 게임 주소, 플랫폼별 HTTP 허용 설정이 이 값을 사용합니다.
3. **Android**는 `File → Build Profiles → Android`에서 활성 플랫폼을 전환하고, `Build App Bundle`을 끈 상태로 Build한 새 APK를 설치합니다. **iOS**는 Mac에서 iOS로 전환해 Build한 뒤, 생성된 `Unity-iPhone.xcodeproj`를 Xcode로 엽니다. 서명 Team과 연결한 iPhone을 선택해 Run하고, 같은 LAN에서 시험할 때는 앱의 로컬 네트워크 접근을 허용합니다.

Windows 게임 서버까지 소스에서 다시 만들려면 Unity Hub에 **Windows Dedicated Server Build Support**를 설치합니다. Build Profiles에서 **Windows Server / Intel 64-bit**로 빌드한 결과 전체를 `Build/ServerRoot/GameServer/`에 넣고 같은 실행기를 사용합니다. [Windows 서버 폴더 안내](Build/ServerRoot/README.md)

## 에디터 한 대에서 실행

Python 3.11 이상과 Unity가 있으면 휴대폰 없이도 테스트할 수 있습니다. `Bootstrap` 씬을 열고 에디터 상단에서 **Server And Client → ▶**를 선택합니다. 메인 에디터가 로그인 서버와 게임 서버를 시작하고, **Player 2**가 게스트로 접속합니다. **■**로 이번 실행의 서버를 함께 종료합니다. [에디터 시나리오](docs/BOOTSTRAP_TEST.md#에디터에서-서버와-클라이언트-한-개씩-실행하기)

## 프로젝트 둘러보기

- [디렉터리 구조와 GitHub 배포 기준](docs/REPOSITORY_LAYOUT.md)
- [게임 기획](docs/PROJECT_PLAN.md) · [퀘스트와 성장 흐름](docs/QUEST_STORY_FLOW.md) · [파티 보스전](docs/BOSS_PROTOTYPE.md)
- [Unity 게임 코드](NetmarbleNeo/Assets/Scripts) · [로그인 백엔드와 저장 구조](localServer/README.md)
