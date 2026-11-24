# iOS·Android 모바일 빌드와 집 서버 접속

iOS·Android 앱은 일반 빌드와 Development Build 모두 `binaryRight.iptime.org`로 접속한다. 게스트 생성·복원에는 `http://binaryRight.iptime.org:8080`을, 로그인 후 게임 접속에는 `binaryRight.iptime.org`의 UDP 7777을 사용한다. 에디터의 **Server And Client** 시나리오는 맥북에서 실행한 로컬 서버를 사용한다.

## ipTIME과 Windows 서버 설정

[Windows 배포 폴더 안내](../Build/ServerRoot/README.md)에 따라 `ServerRoot`에 Windows 게임 서버 빌드를 넣은 뒤 폴더 전체를 데스크톱에 복사한다. `start-servers.cmd`를 실행하면 로그인 서버와 게임 서버가 같은 키를 사용한다. Windows 첫 실행은 빈 계정 DB로 시작한다.

ipTIME에서 DDNS 호스트 `binaryRight.iptime.org`를 등록하고 서버 데스크톱의 이더넷 장치에 DHCP로 같은 내부 IPv4가 할당되도록 예약한다. 서버 PC의 내부 주소를 `192.168.0.2`로 예약한 경우 다음 두 규칙을 등록한다.

| 규칙 | 프로토콜 | 외부 포트 | 내부 IP | 내부 포트 |
|---|---|---:|---|---:|
| 로그인 서버 | TCP | 8080 | 192.168.0.2 | 8080 |
| 게임 서버 | UDP | 7777 | 192.168.0.2 | 7777 |

로그인·게임 서버를 모두 켜 두고 Windows 방화벽에서도 두 포트의 인바운드 연결을 허용한다. 통신사가 집의 공인 IPv4를 바꾸면 ipTIME이 DDNS를 갱신하므로 같은 이름을 계속 사용할 수 있다.

## 모바일 빌드와 설치

1. Unity Build Profiles에서 iOS 또는 Android를 선택하고 빌드한다. iOS는 Unity가 새로 내보낸 Xcode 프로젝트에서 다시 빌드해 설치하고, Android는 새 APK를 설치한다.
2. 휴대폰에서 앱을 열면 기기에 저장한 게스트 계정으로 접속한다. 외부망에서 로그인 서버의 도달 여부를 따로 볼 때는 Wi-Fi를 끄고 휴대폰 브라우저로 `http://binaryRight.iptime.org:8080/health`를 연다. 게임 서버의 UDP 접속은 실제 게임 입장으로 확인한다.

2026-11-24 DDNS 변경에 대한 빌드와 실기기 접속 검증은 생략했다. 아래 기록은 이전 Wi-Fi 개발 빌드에 대한 것이다.

## 11월 20일의 Wi-Fi 개발 빌드 검증

Unity 6000.3.10f1의 격리한 테스트 프로젝트에 게임의 빌드 코드를 넣어 iOS Xcode 프로젝트와 Android Gradle 프로젝트를 생성했다. 두 개발 빌드에서 맥북의 실제 Wi-Fi 주소 `192.168.201.14`를 확인했다. 주소 조회 결과를 `10.16.8.77`로 바꾸고 같은 경로에 다시 내보내자 양쪽에 새 주소가 기록됐으며, iOS의 HTTP 예외도 새 주소와 일치했다. [플랫폼별 빌드 결과](verification/mobile-lan/build-exports.json)

별도 검사에서는 게임의 로그인 코드로 게스트 접속·회원가입 후 자동 로그인·일반 로그인이 같은 호스트를 사용하고, 인증 뒤 게임 접속에도 해당 호스트가 전달되는지 확인했다. 파일은 실제로 읽었고 UI와 서버 응답은 테스트 대역을 사용했다. 휴대폰 설치와 실제 Wi-Fi 게임 접속은 별도 검증 항목이다. [로그인 경로별 검사](verification/mobile-lan/login-paths.json)

## 11월 17일 기준 수동 설정 기록

| 항목 | 값 |
|---|---|
| 11월 17일 맥북 Wi-Fi 인터페이스 / 당시 IPv4 | en0 / 192.168.201.12 |
| Login Scene / LoginClient / Base Url | http://192.168.201.12:8080 |
| Login Scene / LoginClient / Game Server Address | 192.168.201.12 |
| 게임 서버 포트 | 7777 (UDP) |
| WorldSession / NetworkManager / UnityTransport / Server Listen Address | 0.0.0.0 |
| UnityTransport 기본 접속 주소 | 192.168.201.12 |
| 게임 서버 → 로그인 서버 인증 주소 | 기존 http://127.0.0.1:8080 유지 |
| Allow downloads over HTTP | DevelopmentOnly |
| 당시 테스트 프로필의 Development Build | 활성화 |

`0.0.0.0`은 서버 수신용 주소다. 아이폰의 접속 주소로 사용하지 않는다. Unity HTTP 옵션은 프로젝트 공통 설정이므로 iOS 외 플랫폼의 개발 빌드에도 적용된다.

## 11월 20일 당시 iOS 빌드 후처리

`Assets/Editor/IosLocalNetworkBuildSettings.cs`가 iOS 빌드 후 생성된 Info.plist에 다음을 추가한다.

- NSLocalNetworkUsageDescription: 같은 Wi-Fi 개발 서버 연결 목적 설명.
- 개발 빌드에서만 NSAppTransportSecurity / NSAllowsLocalNetworking = true.
- 개발 빌드에서 NSExceptionDomains의 해당 Wi-Fi IPv4 항목에 NSExceptionAllowsInsecureHTTPLoads = true.

iOS 후처리는 내보낸 앱 데이터에서 빌드 시 확정한 주소를 읽어 예외에 사용한다. iPhone에서 접근을 거절했다면 설정에서 앱의 로컬 네트워크 권한을 다시 켠다.

## 11월 17일 당시 동일 Wi-Fi 테스트 절차

1. 맥북과 아이폰을 동일한 일반 Wi-Fi에 연결한다. 게스트망의 기기 격리가 있으면 서로 접속할 수 없다.
2. `localServer/start-lan.command`를 실행한다. Finder의 PATH와 무관하게 Homebrew Python이 있으면 우선 사용한다. 0.0.0.0:8080에서 기존 기본 DB로 실행하며 Ctrl+C로 종료한다.
3. 맥북 Unity에서 Server 역할로 Bootstrap → WorldSession 게임 서버를 실행한다. 기존 ServerAndTwoClients 시나리오를 사용해도 된다. 이 경우 에디터 클라이언트 두 개 외에 iPhone도 추가로 접속한다.
4. Bootstrap을 시작 씬으로 하는 iOS Development Build를 설치한다. 현재 설정된 주소로 가입·로그인한다.
5. 아이폰 Safari에서 빌드 로그의 주소와 8080 포트로 `/health`를 열어 로그인 서버의 응답을 확인한다.

## 11월 17일 맥북 방화벽 확인 결과

방화벽은 활성 상태다. Unity Editor 식별자 `com.unity3d.UnityEditor5.x`는 수신 허용 목록에 있다. `/usr/bin/python3`도 허용되어 있지만, 실제 `python3`는 `/opt/homebrew/opt/python@3.13/bin/python3.13`이며 이 Homebrew 경로의 명시적인 항목은 없었다. 목록에 없다는 사실만으로 차단 여부를 확정할 수는 없다.

Python 실행 시 수신 연결 허용 팝업이 뜨면 허용하거나, 시스템 설정 → 네트워크 → 방화벽 → 옵션에서 실제 사용되는 Python의 수신 연결을 허용한다. 이번 작업에서는 방화벽 규칙을 변경하지 않았다. 공인 IP, 공유기 포트 포워딩은 이 동일 LAN 테스트에 필요하지 않다.

## 11월 17일 확인 범위

실제 Wi-Fi IP·방화벽 목록을 읽고, Unity 설정 저장 결과와 실행 스크립트 구문을 확인했다. 서버를 띄우거나 네트워크 접속 테스트를 하지 않았다.

참고: [Unity 개발 빌드 HTTP 허용](https://docs.unity3d.com/cn/6000.0/ScriptReference/InsecureHttpOption.DevelopmentOnly.html), [Apple 로컬 네트워크 권한](https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy), [Apple ATS IP 예외](https://developer.apple.com/documentation/bundleresources/information-property-list/nsapptransportsecurity/nsexceptiondomains).

## 통합 서버 실행 및 가로 방향 설정 (2026-11-17)

프로젝트 루트의 `start-servers.command`를 더블클릭하면 Python 로그인 서버와 가장 높은 번호의 실행 가능한 게임 서버 빌드를 함께 실행한다. 기존 개별 로그인 서버나 에디터의 Server 역할은 먼저 종료한다. 8080/TCP 또는 7777/UDP 포트가 사용 중이면 실행을 중단하며 기존 프로세스를 강제로 종료하지 않는다.

터미널을 열어두고 사용하며 Ctrl+C를 누르면 이 실행기가 만든 두 서버를 종료한다. 한 서버가 먼저 종료되면 다른 서버도 정리한다. 로그는 `ServerLogs/날짜-시간-PID/login.log`, `game.log`에 각각 저장된다. 스크립트가 출력하는 PID는 프로세스 실행 정보이며 실제 접속 준비 성공 여부는 로그를 확인한다. 현재 저장된 서버 빌드를 실행할 뿐 자동으로 새 빌드를 만들지는 않는다.

Unity 화면 방향을 Auto Rotation + Landscape Left/Right 허용, Portrait/Portrait Upside Down 비허용으로 설정했다. 현재 `ClientBuild/Info.plist`도 iPhone/iPad 가로 두 방향만 허용하도록 반영했다. 아이폰에 이미 설치한 앱은 자동 변경되지 않으므로 Xcode에서 다시 빌드·설치한다. 이후 Unity에서 새 iOS 프로젝트를 내보낼 때는 저장된 Player Settings가 적용된다.

11월 17일에는 통합 실행 파일 실행, 서버 접속, 재빌드·실기기 검증을 진행하지 않았다.
