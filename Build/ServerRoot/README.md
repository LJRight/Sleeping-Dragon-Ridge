# 용이 잠든 능선 · Windows 서버

1. Windows PC에 Python 3.11 이상을 설치한다. 로그인 서버는 표준 라이브러리만 사용한다.
2. Unity의 Windows Dedicated Server 빌드 출력 폴더 전체를 `ServerRoot/GameServer/`에 넣는다. 실행 파일과 같은 이름의 `_Data` 폴더, `UnityPlayer.dll`, 나머지 빌드 파일을 함께 복사한다. 빌드 파일을 `ServerRoot` 바로 아래에 두어도 된다.
3. `ServerRoot` 전체를 Windows PC로 옮기고 [start-servers.cmd](start-servers.cmd)를 더블클릭한다. 로그인 서버가 준비되면 게임 서버가 시작된다. 실행하는 동안 창을 열어 두고, 종료할 때는 창에서 `Ctrl+C`를 누른다.

첫 실행은 빈 계정 DB로 시작한다. 계정과 진행도는 `localServer/data/auth.sqlite3`에 저장된다. 서버를 업데이트할 때는 이 파일을 보관한다. 두 서버가 사용하는 인증 키는 실행할 때마다 자동으로 생성된다. 로그는 `ServerLogs` 안의 실행별 폴더에 남는다.

외부 접속을 위해 ipTIME에서 TCP 8080(로그인), UDP 7777(게임)을 Windows PC의 예약된 내부 IP로 포트포워딩하고 Windows 방화벽에서도 허용한다. Windows PC에서 `http://127.0.0.1:8080/health`를 열면 로그인 서버의 상태를 볼 수 있다. 외부에서는 모바일 데이터로 `http://binaryRight.iptime.org:8080/health`를 열고, 게임 서버 연결은 앱에서 마을 입장으로 확인한다.
