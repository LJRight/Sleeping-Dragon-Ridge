# 디렉터리 구조와 GitHub 배포 기준

저장소의 최상위는 `NetmarbleNeo/`, `localServer/`, `Build/`, `docs/`가 **나란히 들어 있는 폴더**다. Unity Hub에서는 그 안쪽의 `NetmarbleNeo/`를 연다. 이 구조를 유지해야 에디터의 **Server And Client** 시나리오가 옆 폴더의 Python 백엔드를 찾을 수 있다.

## 현재 작업 폴더

```text
Sleeping-Dragon-Ridge/             GitHub에 대응하는 최상위 폴더
├── README.md                      프로젝트 소개, 플레이 데모, 다운로드, 실행 방법
├── .gitignore                     빌드 결과·로컬 데이터 제외 규칙
├── AGENTS.md                      프로젝트 협업 기록
├── NetmarbleNeo/                  Unity 프로젝트
│   ├── Assets/                    게임 소스·씬·프리팹·아트
│   │   └── .gitignore             미사용 에셋 및 해당 .meta 제외
│   ├── Packages/                  패키지 목록과 버전 잠금 파일
│   ├── ProjectSettings/           Unity 버전, 플랫폼·렌더링 설정, 빌드 씬 목록
│   └── Library/, Temp/, Logs/ 등  로컬 캐시·로그·IDE 파일
├── localServer/                   Python 로그인·진행도·거래 백엔드 원본과 테스트
├── Build/
│   ├── ServerRoot/                Windows 서버 배포 폴더
│   │   ├── start-servers.cmd      두 서버를 함께 실행
│   │   ├── run_servers.py         인증 키 생성, 프로세스 시작·종료
│   │   ├── README.md              Windows 운영 안내
│   │   ├── localServer/           서버 ZIP에 동봉하는 Python 백엔드와 정의 JSON
│   │   └── GameServer/            Windows x64 실행 파일, DLL, 게임 데이터
│   ├── android/                   Android APK와 빌드 부속 파일
│   ├── ios/                       Unity가 생성한 Xcode 프로젝트
│   └── releases/                  Drive에 올릴 iOS 프로젝트 ZIP과 Windows 서버 ZIP
├── docs/                          설계·구현 문서와 verification/ 플레이 기록
├── outputs/                       몬스터·장비·강화 기획 엑셀
├── ArtSource/                     제작 스크립트, 설명, 원본 미디어
├── start-servers.command          macOS용 로컬 서버 실행기
├── ServerBuild/                   macOS 게임 서버 빌드 이력
├── iOSClientBuild/                이전 Xcode 출력
├── ServerLogs/                    로컬 서버 실행 로그
└── Backups/                       작업용 백업
```

## 무엇을 어디에 올리나

| 경로 | 게시 기준 |
| --- | --- |
| `README.md`, `.gitignore`, `AGENTS.md`, `NetmarbleNeo/Packages/`, `NetmarbleNeo/ProjectSettings/` | 소스 저장소에 포함한다. |
| `NetmarbleNeo/Assets/` | 게임 코드, 빌드 후처리, 플러그인, 여섯 배포 씬, 동적 리소스와 의존 에셋 및 대응 `.meta`를 선별한다. 현재 규칙은 원본 517개를 Git 대상으로 둔다. **공개 저장소에는 아래의 제3자 원본 조건도 적용한다.** [에셋 제외 규칙](../NetmarbleNeo/Assets/.gitignore) |
| `localServer/`, `Build/ServerRoot/`, `start-servers.command` | Python 소스, 퀘스트·보상 정의, 실행기, 안내는 포함한다. 운영 DB·환경 변수 파일·로그 및 `GameServer/` 실행본은 Git 제외다. 백엔드를 수정해 배포할 때에는 `ServerRoot/localServer/`의 배포용 사본에도 반영한다. |
| `docs/`, `outputs/`, `ArtSource/` | 포트폴리오 문서·플레이 기록과 기획 엑셀을 포함한다. `ArtSource/`의 제작 스크립트·설명은 포함하고, 원본 미디어·ZIP과 엑셀 검사 출력은 로컬에 둔다. |
| `Build/android/1.0.0.apk`, `Build/releases/`의 ZIP 두 개 | Google Drive의 플랫폼별 폴더로 배포한다. Android는 APK, iOS는 Xcode 프로젝트 ZIP, Windows 서버는 실행기와 백엔드를 포함한 ZIP을 제공한다. |
| `iOSClientBuild/`, `ServerBuild/`, `ServerLogs/`, `Backups/` | 이전 빌드와 운영 로그, 백업은 로컬에 둔다. |
| Unity의 `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `.utmp/`, IDE 생성 파일 | 로컬에 둔다. [저장소 제외 규칙](../.gitignore) |

**외부 에셋 원본을 공개할 수 있는지는 팩별 라이선스로 결정한다.** 예를 들어 `TerrainSampleAssets`는 현재 빌드에 사용되며 팩의 고지에 표준 Asset Store EULA가 명시돼 있다. 게임에 내장해 배포할 권한과 원본 텍스처·모델을 공개 Git에 복사할 권한은 별개다. 원본 공개 허가가 확인되지 않은 팩은 공개 커밋에서 제외하고, 소스 빌드에 필요한 팩 이름·버전·공급처·복원 경로를 함께 안내한다. 완성된 Android 앱과 Windows 서버는 Google Drive에서 배포한다. [.gitignore 적용 전후 조사](ASSET_CLEANUP_REVIEW.md) · [지형 팩 고지](../NetmarbleNeo/Assets/TerrainSampleAssets/ThirdPartyNotices.txt) · [Unity Asset Store 약관 §2.2](https://unity.com/legal/as-terms)

## Google Drive 배포와 YouTube 데모 게시

Google Drive에 `Android/`, `iOS/`, `Windows-Server/` 폴더를 만들고 아래 파일을 각각 올린다. 폴더마다 보기 권한으로 공유 링크를 복사하면, 다음 빌드로 파일을 교체해도 README의 폴더 주소를 계속 쓸 수 있다.

| 공유 위치 | 올릴 자료 | README에서 찾을 주석 |
| --- | --- | --- |
| Drive · `Android/` | `Build/android/1.0.0.apk` (약 389 MiB) | `DRIVE_ANDROID_URL` |
| Drive · `iOS/` | `Build/releases/Ridge-iOS-Xcode.zip`. Xcode의 GameAssembly 빌드 단계가 Unity 6000.3.10f1의 `usymtool`을 사용한다. Unity Hub 기본 설치 경로를 변경한 Mac에서는 해당 단계의 도구 경로를 맞춘 뒤 본인 Team으로 서명한다. | `DRIVE_IOS_URL` |
| Drive · `Windows-Server/` | `Build/releases/Ridge-Server-Windows-x64.zip` (약 55 MiB). 압축을 풀면 `ServerRoot/start-servers.cmd`로 두 서버를 시작할 수 있다. | `DRIVE_WINDOWS_SERVER_URL` |
| YouTube | 플레이 데모 영상을 공개 또는 일부 공개로 올리고 영상의 공유 링크를 복사한다. | `YOUTUBE_DEMO_URL` |

주소가 준비될 때마다 README 원문에서 해당 주석 옆의 준비 중 문구를 실제 링크로 바꾼다. Drive에는 폴더별 주소를, YouTube에는 영상 하나의 주소를 넣는다. [README](../README.md)
