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

## sbox

The `sbox` kit installs `sbox`, the command line inside the sandbox for user-facing
actions. It runs on Bun, which the workload must install through mise (`bun@1.4.4` in
`teams/mti-ca/mti-ca.workload`); without Bun, `sbox` says so.

| File in the sandbox | What it is |
|---|---|
| `~/.local/bin/sbox` | the launcher, on PATH |
| `~/.local/share/sbox/sbox.js` | the bundled core from `sbox-cli/` |
| `~/.local/share/sbox/commands/<command>/command.js` | a command, installed by its command kit |
| `~/.local/state/sbox/<command>/` | the state folder of a command |

`sbox --help` lists the installed commands; with no command kit it lists none. The
bundle is produced by the kit's [build step](../docs/authoring.md#kit-build-step)
(`kits/sbox/build`) when `abox build` runs, so it is not committed. The build runs
Bun in Docker through `sbox-cli/bundle` and keeps Bun's package cache in the volume
`agentic-box-bun-cache`.

### Command kits

A command kit adds one `sbox` command. Its build step bundles the command's package
into `command.js`, and its recipe copies that file, with any data the command reads,
into `~/.local/share/sbox/commands/<command>/`. The module default-exports a `Command`
(`summary` and `run(args, ctx)`) from the command API in `sbox-cli/src/api.ts`; see
the [TypeScript coding standards](../CODING_STANDARDS.md#typescript).

## themes

The `themes` kit installs the `sbox theme` command (from `sbox-themes/`) with every
theme and app template. It needs the `sbox` kit. The apps are switched together and
at once, without recreating the sandbox:

| Command | What it does |
|---|---|
| `sbox theme <name>` | renders every app with the theme, saves the choice, tells which apps to restart |
| `sbox theme` | in a terminal, opens an `fzf` picker with a color preview of each theme, the current one selected; otherwise like `--list` |
| `sbox theme --list` | lists the themes, the current one marked with `*` |
| `sbox theme --current` | prints the current theme |
| `sbox theme --apply` | renders the current theme again; the install and startup hooks run it |

The current theme is the saved choice, then the kit arg `THEME` (`SBOX_THEME`,
`tokyo-night` by default), then `tokyo-night`. A saved theme that no longer exists
gets a warning and the fallback, and stays saved.

```yaml
kits:
  - sbox
  - name: themes
    args:
      THEME: nord          # the team's default; the user's choice wins
```

The choice is saved in `~/.local/state/sbox/theme/current`. It survives a restart; to
keep it when the sandbox is recreated, the squad needs the sbox state folder as state:

```yaml
state:
  - ~/.local/state/sbox
```

The apps it themes today:

| App | File in the sandbox | Selected by |
|---|---|---|
| OpenCode | `~/.config/opencode/themes/sbox.json` | `theme` in `~/.config/opencode/cli.json` (merged) |
| Claude Code | `~/.claude/themes/sbox.json` | `theme: custom:sbox` in `~/.claude/settings.json` (merged) |
| pi | `~/.pi/agent/themes/sbox.json` | `theme: sbox` in `~/.pi/agent/settings.json` (merged) |

### Adding a theme

A theme is a folder `sbox-themes/themes/<name>/` with a `colors.toml`, and its
`LICENSE` if it has its own; `sbox-themes/themes/LICENSE` is the license of the
upstream collection. The palettes come unchanged from
[Omarchy](https://github.com/basecamp/omarchy) (`themes/*/colors.toml`), plus
`osaka-jade` from its own repository. Refresh them with:

```sh
cd sbox-themes && bun run sync-themes
```

It adds new themes and updates changed ones, never deletes, and reports the themes
upstream removed. Review the diff before committing. A theme of your own is a new
folder with a `colors.toml` of the same format. Keys it leaves out get upstream's
fallbacks: `orange` falls back to `yellow`, the `bright_*` colors are their base
color mixed 20% with white, `dark_background`/`darker_background` are the
background mixed 25%/50% with black, `color0`–`color15` and the short names
(`bg`, `fg`, …) are aliases, and `mode` is derived from the background.

### Adding an app

An app is a folder `sbox-themes/apps/<app>/` with an `app.yaml` and Liquid templates;
no TypeScript changes. Every app uses one fixed theme named `sbox`, so a switch only
rewrites its content.

```yaml
files:                          # templates rendered to files, relative to the app folder
  - template: sbox.json.liquid
    target: ~/.config/opencode/themes/sbox.json
settings:                       # merged into JSON files on every apply; strings are Liquid
  - file: ~/.config/opencode/cli.json
    merge:
      theme: { name: sbox, mode: "{{ mode }}" }
run:                            # shell commands run from the home folder afterwards
  - bat cache --build
restart: OpenCode               # named in the restart notice; leave out if the app
                                # picks up changes on its next run
```

Every key is optional and an unknown key is an error. Targets start with `~/`. A
template sees every palette key, after the fallbacks, and `mode` (`dark` or `light`):

| Filter | Example | Result |
|---|---|---|
| `mix` | `{{ background \| mix: foreground, 30 }}` | the color 30% of the way to `foreground`; also `"30%"` |
| `strip` | `{{ accent \| strip }}` | `7aa2f7` |
| `rgb` | `{{ accent \| rgb }}` | `122,162,247` |

A template that names a key the palette lacks fails the switch. A `run` command
that fails is reported and fails the command after everything is rendered.

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
