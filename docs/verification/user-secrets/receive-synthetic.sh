#!/bin/sh
# Verification only: run as the application user in a disposable container/sandbox.
# This deliberately accepts only the fixture id, not arbitrary secret ids or paths.
set -eu
umask 077
if [ -n "${APPDATA:-}" ]; then
    root="$APPDATA/Microsoft/UserSecrets"
else
    root="${HOME:?}/.microsoft/usersecrets"
fi
directory="$root/abox-issue5-synthetic-only"
mkdir -p "$directory"
chmod 700 "$directory"
temporary=$(mktemp "$directory/.secrets.XXXXXX")
trap 'rm -f "$temporary"' EXIT HUP INT TERM
cat > "$temporary"
chmod 600 "$temporary"
mv -f "$temporary" "$directory/secrets.json"
printf 'Synthetic transfer finished; values not printed.\n'
