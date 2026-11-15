#!/bin/sh
# Verification only: fixed synthetic id in a disposable sandbox, not an arbitrary copier.
set -eu
umask 077
IFS= read -r expected_size || exit 65
IFS= read -r expected_digest || exit 65
case "$expected_size" in ''|*[!0-9]*) exit 65 ;; esac
case "$expected_digest" in ''|*[!0-9a-f]*) exit 65 ;; esac
[ "${#expected_digest}" -eq 64 ] || exit 65
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
# EOF can mean cancelled/truncated stdin, not a successfully delivered file.
[ "$(wc -c < "$temporary" | tr -d ' ')" = "$expected_size" ] || exit 65
[ "$(sha256sum "$temporary" | cut -d ' ' -f 1)" = "$expected_digest" ] || exit 65
chmod 600 "$temporary"
mv -f "$temporary" "$directory/secrets.json"
