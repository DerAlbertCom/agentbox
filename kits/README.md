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

Every color scheme kit is derived from one palette, so all its tools agree:

| Tool | File in the sandbox | Selected by |
|---|---|---|
| OpenCode | `~/.config/opencode/themes/<scheme>.json` | `~/.config/opencode/cli.json` |
| Claude Code | `~/.claude/themes/<scheme>.json` | `theme: custom:<scheme>` in `~/.claude/settings.json` |
| pi | `~/.pi/agent/themes/<scheme>.json` | `theme: <scheme>` in `~/.pi/agent/settings.json` |
| bat, delta | `~/.config/bat/themes/<scheme>.tmTheme` | `BAT_THEME`, `delta.syntax-theme` in `~/.config/git/config` |
| fzf | — | `FZF_DEFAULT_OPTS` |
| lazygit, yazi | `~/.config/lazygit/config.yml`, `~/.config/yazi/theme.toml` | the file itself |

The kits add only files no other kit ships. The agents' settings files belong to other
kits, so the install hook `~/.local/share/colorscheme/install.sh` sets their `theme` key
and builds bat's theme cache when the sandbox is created.

Dark: `catppuccin`, `ethereal`, `everforest`, `gruvbox`, `hackerman`, `jade-horse`,
`kanagawa`, `last-horizon`, `lumon`, `matte-black`, `miasma`, `nord`, `retro-82`,
`ristretto`, `solitude`, `starship`, `tokyo-night`, `vantablack`.
Light: `catppuccin-latte`, `flexoki-light`, `lupine`, `rose-pine`, `white`.

To switch a squad's scheme, replace its `colorscheme-<scheme>` kit entry.
