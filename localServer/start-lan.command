#!/bin/zsh
set -eu
server_dir="$(cd -- "$(dirname -- "$0")" && pwd)"
cd -- "$server_dir"
# Finder-launched shells may not include Homebrew in PATH.
if [[ -x /opt/homebrew/bin/python3 ]]; then
    server_python=/opt/homebrew/bin/python3
else
    server_python="$(command -v python3)"
fi
print 'Starting login server on 0.0.0.0:8080. Press Ctrl+C to stop.'
exec "$server_python" server.py serve --host 0.0.0.0 --port 8080
