#!/bin/zsh
set -eu
cd -- "${0:A:h}"

if [[ -x /opt/homebrew/bin/python3 ]]; then
    exec /opt/homebrew/bin/python3 server.py reset-backend
fi
exec python3 server.py reset-backend
