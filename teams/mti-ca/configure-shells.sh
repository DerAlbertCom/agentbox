#!/usr/bin/env bash
set -euo pipefail

export REQUESTS_CA_BUNDLE=/etc/ssl/certs/ca-certificates.crt
export AWS_CA_BUNDLE="$REQUESTS_CA_BUNDLE" UV_SYSTEM_CERTS=true
mise settings set auto_update_check_duration 365d
mise settings set use_versions_host false
mise reshim
mkdir -p "$HOME/.local/bin" "$HOME/.local/share/fzf-tab-completion"
mise_binary=$(command -v mise)
if [ "$mise_binary" != /usr/local/bin/mise ]; then
  sudo ln -sf "$mise_binary" /usr/local/bin/mise
fi
sudo add-apt-repository --yes --no-update ppa:git-core/ppa
sudo apt-get update
sudo apt-get install -y --no-install-recommends git
sudo rm -rf /var/lib/apt/lists/*
az extension add --name azure-devops --yes
az config set extension.use_dynamic_install=no_prompt
ln -sf "$HOME/.local/share/mise/shims/herdr" "$HOME/.local/bin/herdr"
curl -fsSL https://raw.githubusercontent.com/lincheney/fzf-tab-completion/7014e0a7cd68fe3530e2f58c45740d17e98f05b8/bash/fzf-bash-completion.sh \
  -o "$HOME/.local/share/fzf-tab-completion/fzf-bash-completion.sh"
printf '%s\n' 'set completion-ignore-case on' > "$HOME/.inputrc"
cat >> "$HOME/.bashrc" <<'BASH'

export LANG=C.UTF-8 LC_ALL=C.UTF-8 EDITOR=edit VISUAL=edit
export STARSHIP_LOG=error CARAPACE_MATCH=2
export REQUESTS_CA_BUNDLE=/etc/ssl/certs/ca-certificates.crt
export AWS_CA_BUNDLE="$REQUESTS_CA_BUNDLE" UV_SYSTEM_CERTS=true
. /opt/sandbox/startup/dotnet-env.sh
command -v mise >/dev/null 2>&1 && eval "$(mise activate bash)"
command -v starship >/dev/null 2>&1 && eval "$(starship init bash)"
command -v carapace >/dev/null 2>&1 && source <(carapace _carapace)
if command -v fzf >/dev/null 2>&1 && [ -r ~/.local/share/fzf-tab-completion/fzf-bash-completion.sh ]; then
  bind 'set show-all-if-ambiguous off'
  bind 'set show-all-if-unmodified off'
  source ~/.local/share/fzf-tab-completion/fzf-bash-completion.sh
  bind -x '"\t": fzf_bash_completion'
fi
_abox_wrappers_path() {
  local entry next="$HOME/.local/bin"
  local -a entries
  IFS=: read -r -a entries <<< "$PATH"
  for entry in "${entries[@]}"; do
    [[ "$entry" == "$HOME/.local/bin" ]] || next+=":$entry"
  done
  export PATH="$next"
}
PROMPT_COMMAND+=(_abox_wrappers_path)
_abox_wrappers_path
BASH

pwsh -NoLogo -NoProfile -Command '
  $ErrorActionPreference = "Stop"
  Install-PSResource -Name CompletionPredictor -Version "[0.1.1]" -Scope CurrentUser -TrustRepository
  Install-PSResource -Name powershell-yaml -Version "[0.4.12]" -Scope CurrentUser -TrustRepository
  Install-PSResource -Name Pester -Version "[6.2.0]" -Scope CurrentUser -TrustRepository
'
rm -rf "$HOME/.cache/mise" "$HOME/.local/share/mise/downloads"
