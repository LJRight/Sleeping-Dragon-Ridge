#!/bin/zsh
set -eu
project_dir="$(cd -- "$(dirname -- "$0")" && pwd)"
if [[ -x /opt/homebrew/bin/python3 ]]; then
    launcher_python=/opt/homebrew/bin/python3
else
    launcher_python="$(command -v python3)"
fi
exec "$launcher_python" -u - "$project_dir" <<'PY'
import datetime
import fcntl
import os
from pathlib import Path
import secrets
import signal
import socket
import subprocess
import sys
import time

root = Path(sys.argv[1])
auth_script = root / 'localServer' / 'server.py'
children = []
log_handles = []
stopping = False


def request_stop(signum, frame):
    global stopping
    stopping = True


for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
    signal.signal(sig, request_stop)


def find_game_binary():
    numbered_builds = []
    for folder in (root / 'ServerBuild').glob('ServerBuild_*'):
        suffix = folder.name.partition('_')[2]
        if suffix.isascii() and suffix.isdecimal():
            numbered_builds.append((int(suffix), folder))

    candidates = [folder for _, folder in sorted(numbered_builds, reverse=True)]
    candidates.append(root / 'GameServerBuild' / 'ServerBuild_0')
    for folder in candidates:
        for name in ('용이 잠든 능선', 'NetmarbleNeo'):
            binary = folder / name
            if binary.is_file() and os.access(binary, os.X_OK):
                return binary
    return None


def main():
    game_binary = find_game_binary()
    if game_binary is None:
        print('실행 가능한 게임 서버가 없습니다. ServerBuild/ServerBuild_<숫자> 폴더에 용이 잠든 능선 또는 NetmarbleNeo 실행 파일이 있는지 확인해 주세요.')
        return 1
    if not auth_script.is_file():
        print(f'로그인 서버 파일이 없습니다: {auth_script}')
        return 1

    log_root = root / 'ServerLogs'
    log_root.mkdir(exist_ok=True)
    # Keep the lock open for the lifetime of this launcher. Never stop an existing server.
    with (log_root / '.launcher.lock').open('a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            print('통합 서버 실행기가 이미 실행 중입니다. 기존 터미널에서 종료해 주세요.')
            return 1

        for port, kind, label in ((8080, socket.SOCK_STREAM, '로그인 TCP'),
                                  (7777, socket.SOCK_DGRAM, '게임 UDP')):
            try:
                with socket.socket(socket.AF_INET, kind) as probe:
                    probe.bind(('0.0.0.0', port))
            except OSError as exc:
                print(f'{label} {port} 포트를 사용할 수 없습니다: {exc}')
                print('기존 로그인 서버나 Unity 에디터 서버를 먼저 종료해 주세요. 기존 프로세스는 종료하지 않습니다.')
                return 1

        logs = log_root / (datetime.datetime.now().strftime('%Y%m%d-%H%M%S') + f'-{os.getpid()}')
        logs.mkdir()
        specs = [
            ('로그인', [sys.executable, '-u', str(auth_script), 'serve', '--host', '0.0.0.0', '--port', '8080'],
             root / 'localServer', logs / 'login.log'),
            ('게임', [str(game_binary), '-batchmode', '-nographics', '-logFile', '-'],
             game_binary.parent, logs / 'game.log')
        ]
        environment = os.environ.copy()
        if not environment.get('GAME_SERVER_KEY'):
            environment['GAME_SERVER_KEY'] = secrets.token_urlsafe(32)
        try:
            for name, command, cwd, log_path in specs:
                if stopping:
                    return 0
                handle = log_path.open('w')
                log_handles.append(handle)
                process = subprocess.Popen(command, cwd=cwd, stdin=subprocess.DEVNULL,
                                           stdout=handle, stderr=subprocess.STDOUT,
                                           start_new_session=True, env=environment)
                children.append((name, process))
                print(f'{name} 서버 실행 요청: PID {process.pid}\n  로그: {log_path}')
            print('\n이 터미널을 열어 두세요. Ctrl+C로 두 서버를 함께 종료합니다.')
            print('접속 준비 완료 여부와 오류는 각 로그에서 확인하세요.')
            while not stopping:
                for name, process in children:
                    code = process.poll()
                    if code is not None:
                        print(f'\n{name} 서버가 종료됐습니다(코드 {code}). 나머지 서버도 종료합니다. 로그: {logs}')
                        return code if code else 1
                time.sleep(0.25)
            return 0
        finally:
            # Only signal process groups created by this launcher.
            for name, process in children:
                if process.poll() is None:
                    try:
                        os.killpg(process.pid, signal.SIGTERM)
                    except ProcessLookupError:
                        pass
            deadline = time.monotonic() + 8
            for name, process in children:
                try:
                    process.wait(timeout=max(0.1, deadline - time.monotonic()))
                except subprocess.TimeoutExpired:
                    try:
                        os.killpg(process.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                    process.wait()
            for handle in log_handles:
                handle.close()
            if children:
                print('이 실행기가 시작한 두 서버를 종료했습니다.')


try:
    sys.exit(main())
except Exception as exc:
    print(f'서버 실행 실패: {exc}', file=sys.stderr)
    sys.exit(1)
PY
