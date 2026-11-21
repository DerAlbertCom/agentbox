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
| `sbox theme <name>` | renders every app with the theme, saves the choice, recolors the terminal (see [Terminal colors](#terminal-colors)), notes that some apps show it only after a restart |
| `sbox theme` | in a terminal, opens an `fzf` picker with a color preview of each theme, the current one selected, and switches like `<name>`; Esc changes nothing; otherwise like `--list` |
| `sbox theme --list` | lists the themes, the current one marked with `*` |
| `sbox theme --current` | prints the current theme |
| `sbox theme --apply` | renders the current theme again; the install and startup hooks run it |

The current theme is the saved choice, then the kit arg `THEME` (`SBOX_THEME`,
`tokyo-night` by default), then `tokyo-night`. A saved theme that no longer exists
gets a warning and the fallback, and stays saved; a `THEME` that is not installed
gets a warning and `tokyo-night`.

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
| bat | `~/.config/bat/themes/sbox.tmTheme` | `BAT_THEME=sbox`, set by the kit; the switch rebuilds bat's cache |
| delta | `~/.config/git/sbox.gitconfig` (only `[delta]`) | an `[include]` of it in `~/.config/git/config` (added once); `syntax-theme = sbox`, bat's theme, and `light` or `dark` by the theme's mode |
| fzf | `~/.config/fzf/sbox` | `FZF_DEFAULT_OPTS_FILE`, set by the kit |
| lazygit | `~/.config/lazygit/sbox.yml` | `LG_CONFIG_FILE`, set by the kit: `config.yml`, then `sbox.yml` (an empty `config.yml` is created when there is none) |
| yazi | `~/.config/yazi/theme.toml` | the file itself |
| terminal | `~/.config/sbox/terminal-colors.sh`, the terminal color script | the login script and `sbox theme`, see [Terminal colors](#terminal-colors) |

The terminal tools pick up a switch on their next run; some apps, such as the agents,
may show it only after a restart. A switch overwrites only the theme files it renders
(the `sbox` files and yazi's `theme.toml`) and merges its keys into the settings
files. The apps' other
config files, such as `~/.config/git/config`, `~/.config/bat/config` and lazygit's
`config.yml`, stay the user's: the first switch only adds the `[include]` to the git
config and an empty `config.yml` when there is none. The theme comes after the user's
settings in those files, so it wins for the keys it sets; `~/.gitconfig` is read last
and wins over both.

### Terminal colors

The `terminal` app renders the **terminal color script**, a small `sh` script that
prints OSC 4 (the colors 0–15), OSC 10 (foreground), OSC 11 (background) and OSC 12
(cursor) with the theme's colors. They change only the live terminal session, never
the host terminal's configuration. The script prints whenever it runs, so you can run
it by hand, for example after a program reset the colors:
`sh ~/.config/sbox/terminal-colors.sh`.

The terminal is recolored only when `SBOX_TERMINAL_THEME=1` is set, which `abox run`
does unless the user turned it off, and only in a terminal:

- **Login.** The kit installs `/etc/profile.d/sbox-terminal-theme.sh` for login shells
  (`bash -l`). With the variable, a terminal on stdout and a rendered terminal color
  script, it runs the script, errors suppressed, and prints the resets (OSC 104, 110,
  111, 112) when the shell exits (`exit`, Ctrl+D, `logout`). Otherwise it does nothing.
  Login does not start `sbox`; a login before the first render, while the startup hook
  still runs on a fresh sandbox, gets no colors.
- **Switch.** `sbox theme <name>` and a picker selection run the freshly rendered
  script after rendering. Esc, `--apply`, `--list`, `--current` and `--preview` never
  recolor.

Only the terminal that runs `sbox theme` is recolored; other terminals of the sandbox
get the new colors at their next login. Try it without `abox run` with
`sbx exec -it -e SBOX_TERMINAL_THEME=1 <sandbox> bash -l`. Terminals that ignore the
reset sequences keep the theme's colors after exit. See
[ADR 0002](../docs/adr/0002-terminal-colors-from-the-sandbox.md).

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
