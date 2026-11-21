# The kits of the team

This folder holds the kits and the workloads that the squads of the project name.

```
kits/
  pi-config/               a kit: a directory named and shaped like its descriptor
    pi-config.yaml
  opencode-config/
    opencode-config.yaml
  github-enterprise-auth/
    github-enterprise-auth.yaml
  elastic-auth/
    elastic-auth.yaml
  host-access-cape/
    host-access-cape.yaml
```

How to write a workload, a kit or a squad is [docs/authoring.md](../docs/authoring.md):
the squads and the workloads of the teams of this project live in `teams/<team>/`, the
shared kits are here. The descriptor of a kit follows the
[kit spec](https://github.com/docker/sandbox-kit-spec); the kits of this folder are
working examples to copy from.

## Color schemes

A color scheme kit is named `colorscheme-<scheme>` and declares `provides: [colorscheme]`,
so a sandbox carries at most one of them. It themes the agents (OpenCode, Claude Code,
pi) and the terminal tools of the sandbox (bat, delta, fzf, lazygit, yazi); the terminal
colors themselves come from the host terminal.

- `colorscheme-tokyo-night` follows the Omarchy Tokyo Night theme. The Claude Code and
  pi themes come from Omarchy's palette; the bat/delta syntax theme and the yazi theme
  are taken from [folke/tokyonight.nvim](https://github.com/folke/tokyonight.nvim)
  extras (Apache-2.0, commit `cdc07ac`).
