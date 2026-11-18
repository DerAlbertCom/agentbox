#!/bin/sh
# Sourced by shells and sandbox launchers, which may not inherit Docker ENV.
: "${HOME:=/home/agent}"
: "${DOTNET_DOTNETUP_DATA_DIR:=$HOME/.local/share/dotnetup}"
: "${DOTNET_ROOT:=$DOTNET_DOTNETUP_DATA_DIR/dotnet}"
: "${DOTNET_NOLOGO:=1}"
: "${DOTNET_CLI_TELEMETRY_OPTOUT:=1}"
case ":${PATH:-}:" in
    *":$DOTNET_ROOT:"*) ;;
    *) PATH="$DOTNET_ROOT:${PATH:-/usr/local/bin:/usr/bin:/bin}" ;;
esac
case ":$PATH:" in
    *":$HOME/.dotnetup:"*) ;;
    *) PATH="$HOME/.dotnetup:$PATH" ;;
esac
export HOME DOTNET_DOTNETUP_DATA_DIR DOTNET_ROOT DOTNET_NOLOGO DOTNET_CLI_TELEMETRY_OPTOUT PATH
