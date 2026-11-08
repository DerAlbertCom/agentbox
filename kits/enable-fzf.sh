#!/usr/bin/env bash
# fzf for the shell of the sandbox.
#
# The tool is installed next to the other tools instead of with mise, so the list of
# tools of the image stays as the workload declares it and the default tool does not change.
set -euo pipefail

version=0.62.0

case "$(uname -m)" in
  x86_64) arch=amd64 ;;
  aarch64 | arm64) arch=arm64 ;;
  *)
    echo "fzf: unsupported architecture: $(uname -m)" >&2
    exit 1
    ;;
esac

mkdir -p /home/agent/.local/bin
curl -fsSL "https://github.com/junegunn/fzf/releases/download/v${version}/fzf-${version}-linux_${arch}.tar.gz" \
  | tar -xz -C /tmp
install -m 0755 "/tmp/fzf-${version}-linux_${arch}.tar.gz" /home/agent/.local/bin/fzf
