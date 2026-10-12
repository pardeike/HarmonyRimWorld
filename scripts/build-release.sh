#!/bin/sh
set -eu
mod_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
for argument do
    case "$argument" in
        --help|-h|--package-only)
            exec "$mod_root/scripts/build-offline.sh" "$@"
            ;;
    esac
done
# Download only public package metadata/content, then build and deploy offline.
/opt/homebrew/bin/python3 -I "$mod_root/scripts/fetch-harmony.py"
exec "$mod_root/scripts/build-offline.sh" "$@"
