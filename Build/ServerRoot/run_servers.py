"""Start and supervise the Windows login and Unity game servers in this folder."""
import ctypes
from ctypes import wintypes
from datetime import datetime
from http.client import HTTPConnection, HTTPException
import json
import os
from pathlib import Path
import secrets
import signal
import socket
import subprocess
import sys
import time


ROOT = Path(__file__).resolve().parent
BACKEND = ROOT / "localServer"
LOGIN_PORT = 8080
GAME_PORT = 7777


def game_executable():
    candidates = set()
    for pattern in ("*.exe", "*/*.exe", "GameServer/*/*.exe", "ServerBuild/*/*.exe"):
        for exe in ROOT.glob(pattern):
            if (exe.parent / (exe.stem + "_Data")).is_dir() and (exe.parent / "UnityPlayer.dll").is_file():
                candidates.add(exe)
    if not candidates:
        raise RuntimeError("Windows 게임 서버 빌드(.exe, 같은 이름의 _Data 폴더, UnityPlayer.dll)를 "
                           "ServerRoot 또는 그 아래 GameServer 폴더에 함께 넣으세요.")
    if len(candidates) > 1:
        paths = "\n".join(str(p.relative_to(ROOT)) for p in sorted(candidates))
        raise RuntimeError(f"게임 서버 빌드가 여러 개입니다. 실행할 빌드 하나만 남겨 주세요.\n{paths}")
    return candidates.pop()


def bind_children_to_launcher():
    # Descendants inherit job membership. If the console window or launcher is killed,
    # Windows closes its private job handle and ends only the processes in this job.
    class BasicLimits(ctypes.Structure):
        _fields_ = [("processTime", ctypes.c_int64), ("jobTime", ctypes.c_int64),
                    ("flags", wintypes.DWORD), ("minWorkingSet", ctypes.c_size_t),
                    ("maxWorkingSet", ctypes.c_size_t), ("activeProcesses", wintypes.DWORD),
                    ("affinity", ctypes.c_size_t), ("priority", wintypes.DWORD),
                    ("scheduling", wintypes.DWORD)]

    class ExtendedLimits(ctypes.Structure):
        _fields_ = [("basic", BasicLimits), ("ioCounters", ctypes.c_uint64 * 6),
                    ("processMemory", ctypes.c_size_t), ("jobMemory", ctypes.c_size_t),
                    ("peakProcessMemory", ctypes.c_size_t), ("peakJobMemory", ctypes.c_size_t)]

    win = ctypes.WinDLL("kernel32", use_last_error=True)
    win.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
    win.CreateJobObjectW.restype = wintypes.HANDLE
    win.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
    win.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
    win.CloseHandle.argtypes = [wintypes.HANDLE]
    job = win.CreateJobObjectW(None, None)
    if not job:
        raise ctypes.WinError(ctypes.get_last_error())
    limits = ExtendedLimits()
    limits.basic.flags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
    if not (win.SetInformationJobObject(job, 9, ctypes.byref(limits), ctypes.sizeof(limits))
            and win.AssignProcessToJobObject(job, wintypes.HANDLE(-1))):  # GetCurrentProcess()
        error = ctypes.get_last_error()
        win.CloseHandle(job)
        raise ctypes.WinError(error)
    # Keep this non-inheritable handle open until Windows tears down this process.


def require_free_port(port, kind):
    with socket.socket(socket.AF_INET, kind) as probe:
        probe.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        try:
            probe.bind(("0.0.0.0", port))
        except OSError as error:
            protocol = "TCP" if kind == socket.SOCK_STREAM else "UDP"
            raise RuntimeError(f"{protocol} {port} 포트를 사용할 수 없습니다: {error}") from error


def wait_for_login(process):
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError(f"로그인 서버가 준비 전에 종료되었습니다 (코드 {process.returncode}).")
        connection = HTTPConnection("127.0.0.1", LOGIN_PORT, timeout=1)
        try:
            connection.request("GET", "/health")
            response = connection.getresponse()
            if response.status == 200 and json.load(response).get("status") == "ok":
                return
        except (OSError, HTTPException, ValueError):
            pass
        finally:
            connection.close()
        time.sleep(0.25)
    raise RuntimeError("로그인 서버가 30초 안에 준비되지 않았습니다.")


def stop_children(children):
    for process in children:
        if process.poll() is None:
            try:
                process.send_signal(signal.CTRL_BREAK_EVENT)
            except OSError:
                pass
    deadline = time.monotonic() + 5
    for process in children:
        try:
            process.wait(timeout=max(0, deadline - time.monotonic()))
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()


def run_pair(game, logs):
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    session_logs = logs / stamp
    session_logs.mkdir()
    game_log = session_logs / "game.log"
    login_log = session_logs / "login.log"
    database = BACKEND / "data" / "auth.sqlite3"
    env = dict(os.environ, NEO_AUTH_URL=f"http://127.0.0.1:{LOGIN_PORT}", NEO_GAME_PORT=str(GAME_PORT),
               NEO_DEDICATED_SERVER="1", GAME_SERVER_KEY=secrets.token_urlsafe(32), PYTHONIOENCODING="utf-8")
    login_command = [sys.executable, "-u", str(BACKEND / "server.py"), "--db", str(database),
                     "serve", "--host", "0.0.0.0", "--port", str(LOGIN_PORT)]
    game_command = [str(game), "-batchmode", "-nographics", "-logFile", str(game_log)]
    children = []
    print(f"게임 빌드: {game}\n계정 DB: {database}\n로그: {session_logs}")
    with login_log.open("wb") as login_output:
        try:
            login = subprocess.Popen(login_command, cwd=BACKEND, env=env, stdin=subprocess.DEVNULL,
                                     stdout=login_output, stderr=subprocess.STDOUT,
                                     creationflags=subprocess.CREATE_NEW_PROCESS_GROUP)
            children.append(login)
            wait_for_login(login)
            print(f"로그인 서버 준비 완료 (TCP {LOGIN_PORT}).")
            unity = subprocess.Popen(game_command, cwd=game.parent, env=env, stdin=subprocess.DEVNULL,
                                     creationflags=subprocess.CREATE_NEW_PROCESS_GROUP)
            children.append(unity)
            print(f"게임 서버 시작 (UDP {GAME_PORT}). 두 서버를 종료하려면 Ctrl+C를 누르세요.")
            while True:
                for name, process in (("로그인", login), ("게임", unity)):
                    if process.poll() is not None:
                        raise RuntimeError(f"{name} 서버가 종료되었습니다 (코드 {process.returncode}). 로그: {session_logs}")
                time.sleep(0.5)
        finally:
            stop_children(children)


def main():
    if sys.platform != "win32" or sys.version_info < (3, 11):
        raise RuntimeError("Windows 및 Python 3.11 이상이 필요합니다.")
    import msvcrt

    game = game_executable()
    if not (BACKEND / "server.py").is_file():
        raise RuntimeError(f"로그인 백엔드를 찾지 못했습니다: {BACKEND}")
    logs = ROOT / "ServerLogs"
    logs.mkdir(exist_ok=True)
    with (logs / ".launcher.lock").open("a+b") as lock:
        lock.seek(0)
        try:
            msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError as error:
            raise RuntimeError("이 폴더의 통합 서버 실행기가 이미 열려 있습니다.") from error
        require_free_port(LOGIN_PORT, socket.SOCK_STREAM)
        require_free_port(GAME_PORT, socket.SOCK_DGRAM)
        bind_children_to_launcher()
        run_pair(game, logs)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n두 서버를 종료했습니다.")
    except (OSError, RuntimeError) as error:
        print(f"서버 실행 오류: {error}", file=sys.stderr)
        sys.exit(1)
