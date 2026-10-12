#!/bin/sh
set -eu
mod_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
export OFFLINE_BUILD=1
exec /usr/bin/sandbox-exec -f "$mod_root/scripts/offline.sb" /opt/homebrew/bin/python3 -I "$mod_root/scripts/build.py" "$@"
