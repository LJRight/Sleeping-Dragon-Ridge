# 회원가입·자동 로그인 검증

검증일: 2026-11-16. 사용자 승인 범위: Unity 회원가입 UI에서 로그인 서버에 요청하여 DB 계정을 만들고 자동 로그인·게임 접속까지 연결한다.

## 구현

- 기존 `localServer/server.py`에 `/auth/register` API가 이미 있어 재사용했다.
- `LoginClient.cs`: Sign Up → 가입 요청 → 201 응답 → 로그인 요청 → LoginSession.Connect로 연결했다.
- 가입부터 자동 로그인까지 중복 Submit을 막는다. 가입 성공 후 로그인만 실패하면 다음 Submit은 로그인만 재시도한다.
- Login Scene에 상태 문구와 Back 버튼을 연결했다. 비밀번호 입력을 Password로 마스킹하고 제출 버튼 문구를 Submit으로 표시했다.
- 기존 영문 UI와 Latin 폰트에 맞춰 상태 안내는 영어로 표시한다.

## 조건과 방법

macOS, Unity 6000.3.10f1, NGO 2.14.0, Multiplayer Play Mode 2.0.2. `ServerAndTwoClients` 시나리오의 메인 Server와 Player 2·3 Client를 실행했다. 로컬 HTTP 서버는 127.0.0.1:8080에서 **별도 임시 SQLite DB**로 실행했다. 기존 `localServer/data/auth.sqlite3`에는 테스트 계정을 추가하지 않았다.

Unity CLI로 실제 실행 중인 TMP_InputField에 값을 넣고 LoginClient의 UI 콜백을 호출했다. 실제 HTTP 요청·SQLite 저장·NGO 접속은 실행했으며, 사람이 마우스·키보드로 조작한 테스트는 아니다. Back은 Button.onClick을 호출했다. 테스트 비밀번호와 토큰은 기록에 포함하지 않는다.

## 관측 결과

| 항목 | 결과 |
|---|---|
| 잘못된 아이디·짧은 비밀번호 | 형식 오류 안내, Login Scene 유지, DB 계정 0개 |
| Player 2 신규 가입 signup_a | DB 계정 1개, 세션 1개, 자동 로그인 후 Prototype에서 Player 스폰·플레이 준비 완료 |
| 같은 프레임 Submit 두 번 | 계정·세션이 중복 생성되지 않음 |
| Player 3에서 signup_a 가입 시도 | 중복 아이디 안내 표시, DB 계정·세션 각각 1개 유지 |
| Back 버튼 | 입력 패널에서 최초 선택 버튼으로 복귀 |
| 백엔드 주소를 미사용 포트 18081로 변경 | 가입 확인 불가 안내, 계정 추가 없음 |
| 주소 8080 복원 후 signup_b 재시도 | 신규 가입·자동 로그인·Prototype 입장·Player 스폰 성공 |
| 최종 서버 접속 상태 | 클라이언트 2명, 인증 계정 매핑 1→signup_a, 2→signup_b |
| 최종 DB | 사용자 2개·세션 2개. 두 사용자 모두 salt 16바이트·hash 32바이트·600000회 해시를 입력 비밀번호로 재계산하여 일치 확인 |
| 콘솔 | 실행 검증 중 세 인스턴스 오류 0개. 클라이언트 경고 0개, 서버 수신 큐 가득 참 경고 7개 |
| 비밀번호 마스킹 | 씬 저장 후 다시 열어 Password 설정 확인, 임시 문자열 표시가 별표로 바뀜 확인 |

서버의 `Receive queue is full` 경고는 남아 있으며, 이번 회원가입 작업에서 Transport 큐 설정이나 장시간 네트워크 안정성은 수정·검증하지 않았다. 씬 수정용 첫 eval은 비활성 Submit 버튼을 GameObject.Find로 찾지 못해 예외가 발생했으며, 비활성 자식 조회로 수정하여 저장했다. 이는 가입 실행 경로의 오류와 구분한다.

## 종료 상태와 범위

테스트 서버와 시나리오를 종료하고 자동 tick을 해제했다. 테스트용 자격 증명 파일을 삭제했다. 실제 테스트는 `localServer`에서 `python3 server.py serve`를 실행한 뒤 시나리오의 각 클라이언트에서 서로 다른 아이디로 Sign Up하면 된다.

이번에는 가입·로그인·접속과 위 오류 흐름을 검증했다. 가입 성공 직후 로그인 응답만 실패시키는 경우, 모바일 실기기·인터넷 배포·부하 테스트는 수행하지 않았다. 이전 이동 검증은 [BOOTSTRAP_TEST.md](BOOTSTRAP_TEST.md)에 별도로 기록되어 있다.
