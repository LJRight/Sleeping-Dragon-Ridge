"""Local login backend. Python 3.10+, standard library only."""

if __package__:
    from . import progression, trading
else:
    import progression
    import trading

import argparse
from contextlib import closing, contextmanager
import getpass
import hashlib
import hmac
import json
import os
from pathlib import Path
import re
import secrets
import sqlite3
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


ROOT = Path(__file__).resolve().parent
ITERATIONS = 600_000
SESSION_SECONDS = 3600
GUEST_SECONDS = 180 * 24 * 3600
GUEST_PROOF = re.compile(r"[A-Za-z0-9_-]{43,96}\Z")
MAX_BODY = 4096
ID_PATTERN = re.compile(r"[A-Za-z0-9_-]{3,32}\Z")


def valid_credentials(user_id, password):
    return (isinstance(user_id, str) and ID_PATTERN.fullmatch(user_id) is not None
            and isinstance(password, str) and 8 <= len(password) <= 128)


def password_hash(password, salt, iterations=ITERATIONS):
    return hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, iterations)


class Store:
    def __init__(self, path):
        self.path = Path(path).resolve()
        self.path.parent.mkdir(parents=True, exist_ok=True)
        with self.connect() as db:
            db.executescript("""
                CREATE TABLE IF NOT EXISTS users (
                    id TEXT PRIMARY KEY,
                    salt BLOB NOT NULL,
                    password_hash BLOB NOT NULL,
                    iterations INTEGER NOT NULL
                );
                CREATE TABLE IF NOT EXISTS sessions (
                    token_hash TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL REFERENCES users(id),
                    expires_at INTEGER NOT NULL
                );
            """)
            db.execute("BEGIN IMMEDIATE")
            self.insert_progress = progression.initialize(db)
            db.execute("""CREATE TABLE IF NOT EXISTS guest_keys (
                user_id TEXT PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
                creation_hash TEXT NOT NULL UNIQUE, refresh_hash TEXT NOT NULL UNIQUE,
                expires_at INTEGER NOT NULL, revoked INTEGER NOT NULL DEFAULT 0)""")
            db.execute("CREATE TABLE IF NOT EXISTS server_keys (name TEXT PRIMARY KEY, value BLOB NOT NULL)")
            db.execute("INSERT OR IGNORE INTO server_keys VALUES ('guest_issuer',?)", (secrets.token_bytes(32),))
        os.chmod(self.path, 0o600)
        self.dummy_salt = secrets.token_bytes(16)

    @contextmanager
    def connect(self):
        db = sqlite3.connect(self.path, timeout=10)
        try:
            db.execute("PRAGMA foreign_keys = ON")
            with db:
                yield db
        finally:
            db.close()

    def create_user(self, user_id, password):
        if not valid_credentials(user_id, password):
            raise ValueError("ID: 3-32 ASCII letters/digits/_/-. Password: 8-128 characters.")
        salt = secrets.token_bytes(16)
        digest = password_hash(password, salt)
        with self.connect() as db:
            db.execute("INSERT INTO users VALUES (?, ?, ?, ?)",
                       (user_id, salt, digest, ITERATIONS))
            db.execute(self.insert_progress, (user_id,))

    def login(self, user_id, password):
        with self.connect() as db:
            row = db.execute("SELECT salt, password_hash, iterations FROM users WHERE id = ?",
                             (user_id,)).fetchone()
        # Unknown accounts also pay the password hashing cost.
        salt, expected, iterations = row or (self.dummy_salt, bytes(32), ITERATIONS)
        digest = password_hash(password, salt, iterations)
        if not hmac.compare_digest(digest, expected) or row is None:
            return None
        with self.connect() as db:
            return self.issue_session(db, user_id)

    def issue_session(self, db, user_id):
        token = secrets.token_urlsafe(32)
        now = int(time.time())
        expires_at = now + SESSION_SECONDS
        db.execute("DELETE FROM sessions WHERE expires_at <= ?", (now,))
        db.execute("INSERT INTO sessions VALUES (?, ?, ?)", (self.token_hash(token), user_id, expires_at))
        cents, = db.execute("SELECT xp_cents FROM user_progress WHERE user_id=?", (user_id,)).fetchone()
        return {"progress": progression.growth(cents),
                "accessToken": token, "tokenType": "Bearer", "expiresAt": expires_at,
                "user": {"id": user_id}}

    def guest(self, proof, create):
        if not isinstance(proof, str) or not GUEST_PROOF.fullmatch(proof):
            raise APIError(400, "INVALID_GUEST_CREDENTIAL")
        with self.connect() as db:
            db.execute("BEGIN IMMEDIATE")
            now = int(time.time())
            if create:
                issuer, = db.execute("SELECT value FROM server_keys WHERE name='guest_issuer'").fetchone()
                # The same private bootstrap seed recovers its original issued secret after a lost response.
                refresh = hmac.new(issuer, proof.encode("ascii"), hashlib.sha256).hexdigest()
                create_hash = self.token_hash(proof)
                row = db.execute(
                    "SELECT user_id,expires_at,revoked FROM guest_keys WHERE creation_hash=?", (create_hash,)).fetchone()
                if row is None:
                    user = "g_" + secrets.token_hex(12)
                    expires_at = now + GUEST_SECONDS
                    # No usable password exists for a guest. Keep the existing users schema and FKs valid.
                    db.execute("INSERT INTO users VALUES(?,?,?,?)",
                               (user, secrets.token_bytes(16), secrets.token_bytes(32), ITERATIONS))
                    db.execute(self.insert_progress, (user,))
                    db.execute("INSERT INTO guest_keys VALUES(?,?,?,?,0)",
                               (user, create_hash, self.token_hash(refresh), expires_at))
                    row = (user, expires_at, 0)
            else:
                refresh = proof
                row = db.execute("SELECT user_id,expires_at,revoked FROM guest_keys WHERE refresh_hash=?",
                                 (self.token_hash(refresh),)).fetchone()
            if row is None or row[2] or row[1] <= now:
                raise APIError(401, "GUEST_CREDENTIAL_INVALID")
            res = self.issue_session(db, row[0])
            res.update(guestToken=refresh, guestExpiresAt=row[1])
            return res

    def revoke_guest(self, user):
        with self.connect() as db:
            updated = db.execute("UPDATE guest_keys SET revoked=1 WHERE user_id=?", (user,)).rowcount
            if updated:
                db.execute("DELETE FROM sessions WHERE user_id=?", (user,))
            return bool(updated)

    @staticmethod
    def token_hash(token):
        return hashlib.sha256(token.encode("utf-8")).hexdigest()

    def user_for_token(self, token):
        with self.connect() as db:
            row = db.execute("SELECT user_id FROM sessions WHERE token_hash = ? AND expires_at > ?",
                             (self.token_hash(token), int(time.time()))).fetchone()
        return {"id": row[0]} if row else None

    def logout(self, token):
        with self.connect() as db:
            db.execute("DELETE FROM sessions WHERE token_hash = ?", (self.token_hash(token),))


class LoginLimiter:
    """Per-IP fixed window; local single-process testing only."""
    def __init__(self, limit=10, window=60):
        self.limit, self.window = limit, window
        self.entries = {}
        self.lock = threading.Lock()

    def allow(self, ip):
        now = time.monotonic()
        with self.lock:
            self.entries = {key: value for key, value in self.entries.items()
                            if value[0] > now}
            until, count = self.entries.get(ip, (now + self.window, 0))
            if count >= self.limit:
                return False
            self.entries[ip] = (until, count + 1)
            return True


class APIError(Exception):
    def __init__(self, status, code):
        self.status, self.code = status, code


def make_server(host, port, store, editor_session=None):
    limiter = LoginLimiter()
    registration_limiter = LoginLimiter()

    class Handler(BaseHTTPRequestHandler):
        server_version = "LocalLogin/1.0"
        sys_version = ""

        def setup(self):
            super().setup()
            self.connection.settimeout(5)

        def log_message(self, format, *args):
            # Do not log bodies, credentials, tokens, or URL query strings.
            pass

        def reply(self, status, data):
            body = json.dumps(data, ensure_ascii=False).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            if status == 429:
                self.send_header("Retry-After", "60")
            self.end_headers()
            self.wfile.write(body)

        def read_json(self):
            if self.headers.get_content_type() != "application/json":
                raise APIError(415, "JSON_REQUIRED")
            if self.headers.get("Transfer-Encoding"):
                raise APIError(400, "INVALID_BODY")
            lengths = self.headers.get_all("Content-Length", [])
            if len(lengths) != 1:
                raise APIError(400, "INVALID_BODY")
            try:
                length = int(lengths[0])
            except ValueError:
                raise APIError(400, "INVALID_BODY") from None
            if not 0 < length <= MAX_BODY:
                raise APIError(413, "BODY_TOO_LARGE")
            try:
                data = json.loads(self.rfile.read(length).decode("utf-8"))
            except (ValueError, RecursionError):
                raise APIError(400, "INVALID_JSON") from None
            if not isinstance(data, dict):
                raise APIError(400, "INVALID_BODY")
            return data

        def token(self):
            auth = self.headers.get("Authorization", "")
            parts = auth.split()
            if len(parts) != 2 or parts[0].lower() != "bearer" or len(parts[1]) > 256:
                raise APIError(401, "UNAUTHORIZED")
            if not store.user_for_token(parts[1]):
                raise APIError(401, "UNAUTHORIZED")
            return parts[1]

        def dispatch(self):
            if self.command == "GET" and self.path == "/health":
                health = {"status": "ok"}
                if editor_session:
                    health["editorSession"] = editor_session
                return self.reply(200, health)
            if self.command == "POST" and self.path in ("/auth/guest/create", "/auth/guest/resume"):
                if not limiter.allow(self.client_address[0]):
                    raise APIError(429, "TOO_MANY_ATTEMPTS")
                data = self.read_json()
                if set(data) != {"guestToken"}:
                    raise APIError(400, "INVALID_GUEST_CREDENTIAL")
                return self.reply(200, store.guest(data["guestToken"], self.path.endswith("/create")))
            if self.command == "POST" and self.path == "/auth/register":
                if not registration_limiter.allow(self.client_address[0]):
                    raise APIError(429, "TOO_MANY_ATTEMPTS")
                data = self.read_json()
                if set(data) != {"id", "password"} or not valid_credentials(data["id"], data["password"]):
                    raise APIError(400, "INVALID_CREDENTIAL_FORMAT")
                try:
                    store.create_user(data["id"], data["password"])
                except sqlite3.IntegrityError as error:
                    if error.sqlite_errorcode == sqlite3.SQLITE_CONSTRAINT_PRIMARYKEY:
                        raise APIError(409, "ID_ALREADY_EXISTS") from None
                    raise
                return self.reply(201, {"user": {"id": data["id"]}})
            if self.command == "POST" and self.path == "/auth/login":
                if not limiter.allow(self.client_address[0]):
                    raise APIError(429, "TOO_MANY_ATTEMPTS")
                data = self.read_json()
                if set(data) != {"id", "password"} or not valid_credentials(data["id"], data["password"]):
                    raise APIError(400, "INVALID_CREDENTIAL_FORMAT")
                result = store.login(data["id"], data["password"])
                if result is None:
                    raise APIError(401, "INVALID_CREDENTIALS")
                return self.reply(200, result)
            if self.command == "GET" and self.path == "/progress/me":
                user = store.user_for_token(self.token())
                if not user:
                    raise APIError(401, "UNAUTHORIZED")
                with store.connect() as db:
                    db.execute("BEGIN")
                    result = progression.snapshot(db, user["id"])
                return self.reply(200, result)
            if self.path.startswith("/internal/"):
                secret = os.environ.get("GAME_SERVER_KEY", "")
                supplied = self.headers.get("X-Game-Server-Key", "")
                if not secret or not hmac.compare_digest(secret.encode(), supplied.encode()):
                    raise APIError(403, "SERVER_KEY_REQUIRED")
                if self.command == "POST" and self.path == "/internal/trade/commit":
                    data = self.read_json()
                    with store.connect() as db:
                        db.execute("BEGIN IMMEDIATE")
                        result = trading.commit(db, data)
                    return self.reply(200, result)
                if self.command == "POST" and self.path in ("/internal/boss/start", "/internal/boss/clear"):
                    data = self.read_json()
                    with store.connect() as db:
                        db.execute("BEGIN IMMEDIATE")
                        result = progression.boss_attempt(db, self.path.rsplit("/", 1)[1], data)
                    return self.reply(200, result)
                if not self.path.startswith("/internal/progress/"):
                    raise APIError(404, "NOT_FOUND")
                parts = self.path.removeprefix("/internal/progress/").split("/")
                user = parts[0]
                if not ID_PATTERN.fullmatch(user) or parts[1:] not in ([], ["action"]):
                    raise APIError(404, "NOT_FOUND")
                if self.command == "GET":
                    if len(parts) != 1:
                        raise APIError(404, "NOT_FOUND")
                    with store.connect() as db:
                        db.execute("BEGIN")
                        result = progression.snapshot(db, user)
                    return self.reply(200, result)
                data = self.read_json()
                with store.connect() as db:
                    db.execute("BEGIN IMMEDIATE")
                    result = (progression.action if len(parts) == 2 else progression.checkpoint)(db, user, data)
                return self.reply(200, result)
            if self.command == "GET" and self.path == "/auth/me":
                return self.reply(200, {"user": store.user_for_token(self.token())})
            if self.command == "POST" and self.path == "/auth/logout":
                store.logout(self.token())
                return self.reply(200, {"ok": True})
            raise APIError(404, "NOT_FOUND")

        def handle_request(self):
            try:
                self.dispatch()
            except (APIError, progression.ProgressError) as error:
                self.reply(error.status, {"error": error.code})
            except (TimeoutError, ConnectionError):
                self.close_connection = True
            except sqlite3.Error:
                self.reply(503, {"error": "STORAGE_UNAVAILABLE"})

        do_GET = handle_request
        do_POST = handle_request

    server = ThreadingHTTPServer((host, port), Handler)
    server.daemon_threads = True
    return server


def reset_backend(path):
    path = Path(path).resolve()
    if not path.is_file():
        print(f"초기화할 DB 파일이 없습니다: {path}", file=sys.stderr)
        return 1
    print(f"대상 DB: {path}\n모든 계정, 로그인 토큰, 장비, 퀘스트와 거래 기록이 삭제됩니다.")
    try:
        answer = input("로그인·게임 서버를 모두 종료했다면 RESET을 입력하세요: ")
    except (EOFError, KeyboardInterrupt):
        answer = ""
    if answer != "RESET":
        print("초기화를 취소했습니다.")
        return 0

    # Older releases have different foreign keys. Delete children before their parents.
    tables = (
        "trade_receipts", "boss_attempts", "game_actions", "user_rewards",
        "tutorial_history", "quests", "item_instances", "item_stacks", "user_equipment",
        "user_progress", "sessions", "guest_keys", "users", "server_keys",
    )
    try:
        with closing(sqlite3.connect(path.as_uri() + "?mode=rw", uri=True, timeout=5)) as db, db:
            db.execute("PRAGMA foreign_keys = ON")
            db.execute("BEGIN IMMEDIATE")
            existing = {name for name, in db.execute("SELECT name FROM sqlite_master WHERE type='table'")}
            user_columns = {row[1] for row in db.execute("PRAGMA table_info(users)")}
            if "sessions" not in existing or not {"id", "salt", "password_hash", "iterations"} <= user_columns:
                raise ValueError("이 게임의 로그인 DB 형식이 아닙니다.")
            for table in tables:
                if table in existing:
                    db.execute(f"DELETE FROM {table}")
    except (ValueError, sqlite3.Error) as error:
        print(f"초기화 실패: {error}", file=sys.stderr)
        return 1
    print("백엔드 초기화를 완료했습니다. 서버를 다시 시작하세요.")
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db", default=os.getenv("LOCAL_SERVER_DB", str(ROOT / "data" / "auth.sqlite3")))
    commands = parser.add_subparsers(dest="command", required=True)
    serve = commands.add_parser("serve")
    serve.add_argument("--host", default=os.getenv("LOCAL_SERVER_HOST", "127.0.0.1"))
    serve.add_argument("--port", type=int, default=int(os.getenv("LOCAL_SERVER_PORT", "8080")))
    serve.add_argument("--editor-parent", action="store_true",
                       help="Report the bound port to Unity and exit when its stdin pipe closes")
    create = commands.add_parser("create-user")
    create.add_argument("id")
    revoke = commands.add_parser("revoke-guest")
    revoke.add_argument("id")
    commands.add_parser("reset-backend", help="Erase all accounts, tokens and game progress from the selected DB")
    args = parser.parse_args()
    if args.command == "reset-backend":
        sys.exit(reset_backend(args.db))
    store = Store(args.db)
    if args.command == "revoke-guest":
        if not store.revoke_guest(args.id):
            parser.exit(1, "Guest not found.\n")
        print("Guest credential and sessions revoked.")
        return
    if args.command == "create-user":
        password = getpass.getpass("Password (8-128 characters): ")
        if password != getpass.getpass("Confirm password: "):
            parser.exit(1, "Passwords do not match.\n")
        try:
            store.create_user(args.id, password)
        except ValueError as error:
            parser.exit(1, f"{error}\n")
        except sqlite3.IntegrityError:
            parser.exit(1, "ID already exists.\n")
        print("User created.")
        return
    editor_session = os.getenv("NEO_EDITOR_SESSION") if args.editor_parent else None
    with make_server(args.host, args.port, store, editor_session) as server:
        if args.editor_parent:
            # The editor owns the write end. EOF also arrives if Unity crashes.
            def watch_editor():
                sys.stdin.buffer.read()
                server.shutdown()

            threading.Thread(target=watch_editor, daemon=True).start()
            print(server.server_port, flush=True)
        else:
            print(f"Local login server listening on {args.host}:{server.server_port}", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            print("\nServer stopped.")


if __name__ == "__main__":
    main()
