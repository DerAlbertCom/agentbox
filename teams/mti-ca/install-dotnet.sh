#!/usr/bin/env bash
set -euo pipefail

export DOTNET_DOTNETUP_DATA_DIR="$HOME/.local/share/dotnetup"
export DOTNET_ROOT="$DOTNET_DOTNETUP_DATA_DIR/dotnet"
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
export PATH="$HOME/.dotnetup:$DOTNET_ROOT:$PATH"
version=0.3.0-preview.1.26554.3
case "$(uname -m)" in
  x86_64) rid=linux-x64 ;;
  aarch64|arm64) rid=linux-arm64 ;;
  *) exit 1 ;;
esac
mkdir -p "$HOME/.dotnetup" "$HOME/.local/bin"
curl -fsSL "https://ci.dot.net/public/dotnetup/$version/dotnetup-$rid" -o "$HOME/.dotnetup/dotnetup"
checksum=$(curl -fsSL "https://ci.dot.net/public-checksums/dotnetup/$version/dotnetup-$rid.sha512")
printf '%s  %s\n' "$checksum" "$HOME/.dotnetup/dotnetup" | sha512sum -c -
chmod +x "$HOME/.dotnetup/dotnetup"
dotnetup env set none --dotnetup-on-path false
dotnetup sdk install 10 --interactive false --no-progress --require-muxer-update
dotnet --list-sdks | grep -Fx "10.0.404 [$DOTNET_ROOT/sdk]"
dotnetup sdk install 11 --interactive false --no-progress --require-muxer-update
dotnet --list-sdks | grep -Fx "11.0.100 [$DOTNET_ROOT/sdk]"
rm -rf "$DOTNET_DOTNETUP_DATA_DIR/downloadcache"
ln -sf "$DOTNET_ROOT/dotnet" "$HOME/.local/bin/dotnet"
