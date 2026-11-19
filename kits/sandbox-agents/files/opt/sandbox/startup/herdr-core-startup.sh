#!/bin/sh
# Image-baked so kit startup can run this before home-file overlays are applied.
set -eu

export HOME=/home/agent
export PATH="$HOME/.local/bin:$HOME/.local/share/mise/shims:${PATH:-/usr/local/bin:/usr/local/sbin:/usr/sbin:/usr/bin:/bin}"
if [ -r /opt/sandbox/startup/dotnet-env.sh ]; then . /opt/sandbox/startup/dotnet-env.sh; fi
# Core uses SHELL for default terminals; sandbox startup may inherit /bin/sh.
export SHELL=/bin/bash

# The CLI detaches the daemon itself and reuses an existing core. Bound startup
# so a broken core cannot hang a non-interactive sandbox launch indefinitely.
if ! timeout 15s herdr-core server start >/dev/null; then
    echo "Herdr Core startup failed; check ~/.local/state/herdr-core/default/core.log" >&2
    exit 1
fi
