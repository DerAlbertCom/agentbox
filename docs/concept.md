# agentbox (`abox`)

In this repository we develop `abox`, an AI agent sandbox built on Docker Sandboxes (the product; its CLI is `sbx`, https://docs.docker.com/ai/sandboxes/). Each folder gets its own sandbox. Inside it, four agents (GitHub Copilot, OpenCode, pi, Claude Code) are configured against GitHub Copilot on GitHub Enterprise Cloud with data residency.

Users run only `abox`, which calls `sbx` for them. The one exception is the one-time install of `sbx` (see Preflight).

Items marked *(proposed)* fill gaps that the decisions so far do not cover. "Verify" marks a fact the implementation must check first; all of them are listed under Verify before building.

## Glossary

- **Team**: uses one base image and has several squads. Different teams can use different base images.
- **Squad**: a named configuration, selected with `--squad AC`. Publishes as one v3 kit set: one workload plus mixins.
- **Workload**: a `.workload` file. Builds the squad's v3 workload kit: the base image, the mise tools, and the scripts.
- **Base image**: the image built from a workload. Maintainers build it; users get it through their squad's layers.
- **Kit**: a v3 kit, either a workload (full root filesystem) or a mixin (layers added on top). Used for team and squad customization, agent configuration, and auth. Spec: https://github.com/docker/sandbox-kit-spec
- **Config**: an `.abox` file. Composes a sandbox from `base`, `workload`, `kits`, `parameters`, `mcp`, and `state`.
- **Sandbox**: one per folder.
- **Workspace**: the folder's files inside the sandbox, at `/home/agent/workspace`.
- **State**: host directories that persist when a sandbox is replaced.
- **Launcher**: what `abox run` starts: a shell, or a desktop app on the host that works with the sandbox.

## Roles and commands

Maintainers (team members who build base images) use `build` and `publish`. Everyone else uses `create`, `run`, `prune`, and `secrets`. `sbx` runs behind these commands; users never call it.

Exit codes: 0 on success, 1 on an `abox` error, 130 on cancellation. `run` passes through the exit code of the shell or command it starts.

- **`abox create --team <name> [--squad <name>] [--location <path>] [--root <dir>] [--tag <tag> | --local]`**
  - Both `create` and `run` resolve `--location`, then the user config `defaultLocation`, then the current folder. The team name defaults to `defaultTeam`. Only `--local` resolves team sources from the `teams` folder at the location or its parents; `--root` selects a separate source project. Without `--squad`, local create asks which squad of the team to use, when there is more than one; remote create requires it.
  - Creates the sandbox from the squad's kit set in JFrog, under `rwest.jfrog.io/<pullRepository>`. The user setting `pullRepository` defaults to `mtiae-docker-release` and is independent of the workload's publish repository. The default tag is `latest`, pulled with `--pull always`, because sbx's default for kit sets is `missing`, which never refreshes a tag. `--tag` selects another tag. `--local` uses a prebuilt local workload image and its runtime kits with `--pull never`. Remote create needs no source repository or local build metadata: state and MCP settings come from the published set's bundled `squad.json`. Personal `.abox` kit customizations currently require `--local`.
  - A sandbox that already exists for the location is replaced after confirmation. The confirmation lists what will be lost. State directories are kept: the sandbox name does not change, so their host paths do not change either.
  - An existing sandbox keeps the image it was created from. A newer `latest` reaches only sandboxes created after it (inferred; verify).
- **`abox run [<launcher>] [--location <path>] [-- <argv>...]`** starts a launcher for the folder's sandbox. The default launcher is `shell`. See Launchers.
  - Starts the sandbox if it is stopped. Never creates a sandbox: if none exists, prints the `abox create` command to run.
  - `shell` works offline. Other launchers list their needs under Launchers.
  - If JFrog has a newer `latest`, asks whether to replace the sandbox now. Skipped offline. *(proposed placement)*
- **`abox build --team <names>`** resolves each team's folder under `teams` (from now on) and builds the kit set of every squad of it that no other squad of the team names as its `base`: one workload and the mixins reached through the chain. Rejects duplicate kit repositories and file collisions between kits. Layers are shared, not repacked, so squads with common kits share blobs.
- **`abox publish --team <name>`** pushes the kit set to `rwest.jfrog.io/<publishRepository>` (selected by the squad's resolved `.workload`) under `agentbox-rwest-<team>/<squad>:<tag>`, `latest` by default, with the resolved squad config. Every squad keeps the same name when others are added. Publication uses the v3 BuildKit frontend through Docker Buildx; its registry credentials stay in Docker's host credential store. Only builds explicitly excluding personal `.abox` configuration can be published.
- **`abox prune [--full] [--sbx]`** removes the template images of sbx that stopped being used: it keeps the newest assembled kit set and the kit sources it is built from, and removes the older sets and the kit sources nothing uses. `--full` reaches for everything sbx holds: what a sandbox uses, sbx refuses and keeps. `--sbx` stops the running sandboxes first. Never touches secrets, or images of other repositories, nor anything an image of unknown age or id holds. *(proposed: also keep anything a stopped sandbox uses; also prune the local builds of the build store)*
- **`abox secrets`** manages host-side secrets for kits, through `sbx secret` behind the scenes. *(proposed subcommands: `set`, `list`, `remove`)*

## Preflight

Every command except `--help`, `--version` and `publish` runs these checks first.
`publish` uses Docker/Buildx instead and does not require sbx. `abox` never installs
anything itself. *(proposed)*

1. **Installed:** `sbx` must be on `PATH` (`sbx.exe` on Windows). If not, print the install steps for the current OS (below) and exit with an error.
2. **Version:** `sbx version --json` must be 0.51.0 or later. An older version gets an upgrade hint.
3. **Health and sign-in:** abox runs `sbx diagnose --format json` (about 0.5 s here). A failing check stops the command and prints its `hint`. If the `Authentication` check fails, abox starts the browser sign-in and checks again. *(proposed)*

Install steps, from https://docs.docker.com/ai/sandboxes/install/. sbx needs neither Docker Desktop nor Docker Engine.

- **macOS** (Sonoma 14 or later, Apple silicon only): `brew trust docker/tap`, then `brew install docker/tap/sbx`.
- **Windows 11** (64-bit Intel or AMD): `winget install -h Docker.sbx`. For all users, from an elevated prompt: `msiexec.exe /i DockerSandboxesMachine.msi /quiet`. Local sandboxes also need the Windows Hypervisor Platform: `Enable-WindowsOptionalFeature -Online -FeatureName HypervisorPlatform -All` (elevated PowerShell). Open a new terminal after installing so `PATH` updates.
- **Linux, Ubuntu 24.04 or later:** `curl -fsSL https://get.docker.com | sudo SBX=1 sh` installs Docker Engine and sbx. For sbx only: `curl -fsSL https://get.docker.com | sudo REPO_ONLY=1 sh`, then `sudo apt install docker-sbx`. Local sandboxes need KVM: `sudo usermod -aG kvm $USER`, then sign out and back in.
- **Other Linux distributions:** install from release artifacts. Docker does not test them.
- **All systems:** sign-in happens on first use. abox starts the browser sign-in. *(proposed)*

## Launchers

`abox run` starts a launcher. A launcher is the only way `abox` opens something for the user in a sandbox.

| Launcher | Opens | Needs on the host | Needs in the sandbox | Reaches the sandbox through |
|---|---|---|---|---|
| `shell` (default) | a shell in the workspace | nothing extra | the image's shell | internal exec |
| `herdr-app` | a space in the Herdr desktop app (herdr.dev) | `herdr-app` and `hc` on `PATH` | `hc` | `hc` machine transport through `abox pipe`; no SSH |
| `orca` *(proposed; not built)* | a workspace in the Orca desktop app (orca.dev) | the Orca app and its CLI | `sshd` | SSH to a loopback port |

Platforms: the Herdr app runs on macOS (Apple silicon), Linux (x86_64 and arm64), and Windows (x86_64). Orca runs on macOS, Windows, and Linux. sbx itself needs Apple silicon on macOS.

Herdr's terminal program `herdr` is a separate product that does not share agents with the app. It is not a launcher.

**Common steps.** Every launcher:

1. Checks its host needs. A missing program prints its install step and stops.
2. Checks that the folder has a sandbox. If not, prints the `abox create` command and stops.
3. Starts the sandbox if it is stopped.
4. Checks its sandbox needs with `command -v` through the internal exec. A missing program means the image predates the launcher: print "run `abox create` again for this folder" and stop.
5. Runs its own steps.

**Launcher contract.** A launcher:
- has a unique lowercase id, a one-line summary for `abox run --help`, and its host and sandbox needs;
- receives the folder, the sandbox name, the workspace path (`/home/agent/workspace`), and the argv after `--`;
- uses argv as the command for its first terminal, if it has one. `shell` runs argv instead of a shell;
- returns 0 when its work is done, or the command's exit code for `shell`;
- reaches the sandbox only through the internal exec and `abox pipe`, as described in this section. *(proposed)*

### `shell`

Runs `bash -l` in the workspace with a TTY, through the internal exec. The shell starts in the image's `WORKDIR`, which is the workspace.

### `herdr-app`

Herdr runs agents in terminals that stay alive in `hc` (herdr-core), so they keep running when the window closes. `hc machine add --via` reaches a machine with a command instead of SSH. The command gets the shell line as its last argument and must pass bytes both ways untouched.

1. Register the sandbox as a machine, unless `hc machine ls --json` already lists the sandbox name: `hc machine add --via "abox pipe <sandbox> -- sh -c" --name <sandbox>`. `abox pipe` is a hidden entry point that runs a command in the sandbox with stdin and stdout attached. It is not in `--help`. `abox` must be on `PATH` for `hc` to call it. (verify: quoting, and whether TARGET can be omitted with `--via`)
2. Start `herdr-app` detached if no window is open. `herdr-app` with no arguments opens the window. (verify: readiness; `herdr-app status` returns JSON with an `errors` list)
3. Open a space on the machine: `herdr-app do space.new --name <folder> --folder /home/agent/workspace --machine <sandbox>`, adding `--argv '<JSON array>'` when argv is given. Argument names come from `herdr-app commands`.
4. Exit 0. The terminals keep running in `hc` inside the sandbox.

Notes: the machine name is the sandbox name, so a replaced sandbox keeps its entry. Terminals on the machine end when the sandbox stops (verify). The host's `hc` and the one in the image must be the same protocol generation (verify).

### `orca` *(proposed; not built)*

Orca is a desktop agent IDE: each task gets a git worktree and agent terminals. It works on remote machines over SSH, and its CLI (`orca`) talks to the running app. The CLI is registered under Settings → General → Orca CLI.

1. Start `sshd` in the sandbox as root, through the internal exec. Idempotent.
2. Keep abox's SSH key pair in `ssh/` under the abox config directory, created on first use. Add its public key to the sandbox user's `authorized_keys` with the internal exec and stdin, idempotently.
3. Publish sandbox port 22 on loopback with sbx, and read the host port back.
4. Write an OpenSSH host entry `abox-<sandbox>` (127.0.0.1, that port, user `agent`, the key) to `ssh/config` in the abox config directory. Open question 10 decides how Orca reads it.
5. Open the workspace on that target through the Orca CLI. `--host ssh:<target-id>` exists; the exact command is unverified. Open question 11 covers worktrees.

Open question 8 decides whether it ships in v1.

### Adding a launcher *(proposed)*

Implement the contract above, register the launcher under its id, and add a row to the table and a section here. Use only the internal exec, `abox pipe`, the sandbox's workspace path, and the common steps.

## Configuration files

References never include a file extension. The teams of a project live in a folder per team under `teams/`: `abox build` resolves `--team` to such a folder (case-insensitive), and `abox create` and `abox secrets` resolve `--team` and `--squad` to an `.abox` file in the team folder: `AC` → `teams/<team>/ac.abox` (case-insensitive). `base` names are the file of the squad's own team folder first, then the team folders of the project; one that several other teams hold is an error. The `workload` named by the root config lives in the team folder, or, when it is shared, in the `kits` folder of the project.

**`.workload`** (YAML)
- `template`: container image to build on, as in `sbx run --template`. Default `docker.io/sbx/shell-docker-image`, a shell image with Docker Engine inside (`docker.io/sbx/shell-image` is the variant without it). Pin a tag or digest rather than `latest`. *(proposed)* Named `template` so `base` only means the parent `.abox`. *(name proposed)*
- `publishRepository`: JFrog repository name on `rwest.jfrog.io`, required for publishing but optional for local builds. One lowercase registry path segment, without a host, slash or tag. Inherited with the root config's workload, recorded in the build manifest, and revalidated before publishing; changing it requires a rebuild. The included workloads use `mtiae-developer-container-images-local`.
- `apt`: package names, installed with one `apt-get install` before the tools.
- `mise`: tools. The last entry is the default. The generator writes each tool's list reversed, because mise activates the first entry.
- `scripts`: run as the last steps of the generated Dockerfile, during the image build.

**`.abox`** (YAML)
- `base`: name of the parent config. Chains are allowed. Cycles are errors.
- `workload`: name of a `.workload`. Only in the root config, the one without `base`.
- `kits`: kit names.
- `parameters`: named values. Kits receive them in the form the Docker Sandbox kit spec expects. MCP declarations can use them too. Fixed when the set is published (inferred from the kit-set docs; verify).
- `mcp`: neutral MCP configuration. Each server is provided to every agent in the sandbox, in that agent's config format.
- `state`: sandbox directories to persist. See State.

A `base` chain combines declarations and never overrides them. Across the whole chain, every kit, every MCP declaration, every parameter name, and every state entry must be unique. Kits are compared by repository name, so two tags of one repository are duplicates, as sbx also rejects them. A parameter is declared once but can be used by any number of kits and MCP declarations. Duplicates are validation errors, checked whenever a chain is resolved (build, create). The error names the duplicate and the files that declare it *(proposed)*. A kit or MCP declaration that uses an undeclared parameter is also an error *(proposed)*. A child can add, but cannot replace or remove what its base declares.

Examples:

```yaml
# teams/<team>/cape.abox (root)
workload: sample
```

```yaml
# teams/<team>/sample.workload
template: docker.io/sbx/shell-docker-image     # default, can be omitted
publishRepository: mtiae-developer-container-images-local
mise:
  - nodejs@24.4.3
  - dotnet@9.0.303
  - dotnet@10.0.403        # default: last entry
scripts:
  - enable-fzf.sh
```

```yaml
# ac.abox (squad)
base: cape
kits:
  - elastic-auth
  - pi-config
  - azure-devops-auth
  - github-enterprise-auth
  - opencode-config
parameters:
  elastic-url: https://foobar.com/mcp
mcp:                           # assumed: names of servers defined in kits
  - azuredevops
  - elastic
  - database
state:                         # directories; ~ is /home/agent
  - ~/.config/opencode/state   # verify path
  - ~/.local/state/shell       # shell history lives here, HISTFILE set by a kit *(proposed)*
```

## Tools and the base image

- The base image contains mise, the workload's tools, the scripts, and two launcher helpers: `hc` (herdr-core) and `openssh-server` *(proposed)*. Nothing else.
- Each build generates the Dockerfile from the workload. It has one install step per tool, so anonymous GitHub rate limits don't stall the build. The build passes a GitHub token as a BuildKit secret *(proposed)*, so the token never lands in an image layer.
- The generated Dockerfile sets `WORKDIR /home/agent/workspace` *(proposed; see Sandbox layout)*.
- Inside a sandbox, `mise upgrade` updates tools without a new sandbox. Exact pins (`nodejs@24.4.3`) don't move with plain `mise upgrade`, so they need `mise upgrade --bump`, which also rewrites the mise config in the sandbox. Upgrades last until the sandbox is replaced.
- A kit may also install tools via mise and run scripts. Install hooks run during sandbox creation. Startup hooks run alongside the agent, which may start before they finish (sbx docs). Workload scripts run at image build.

## Sandbox layout

- The workspace is the folder, at `/home/agent/workspace`. The sbx@1 capability places an assigned workspace at the image's absolute `WORKDIR`. Images are per squad, not per folder, so the path is the same for every folder. (verify on a test sandbox)
- The runtime sets `WORKSPACE_DIR` to that path, plus `SANDBOX_NAME` and `SANDBOX_ID` (sbx@1). Agents and launchers read `WORKSPACE_DIR`.
- Agents and tools use `~/worktrees` by default. It is not mounted from the host, so its contents are lost when a sandbox is replaced (open question 9).
- Credentials never live inside the sandbox. Kits configure the Docker Sandbox credentials proxy. The values come from `abox secrets`.

## Naming

- Sandbox name: `<folder>-abox-<hash>`, for example `rwe-abox-3AF9D`. The name is also the Herdr machine name and the key for the sandbox's state directories.
- `<folder>`: last segment of the path, normalized to pass the sandbox name rules (verified with `sbx create --name`: at least two characters, starting with a letter or digit, only letters, digits, hyphens, and periods). *(proposed normalization: lowercase, anything outside `a-z0-9` becomes `-`, leading hyphens trimmed; if empty, use `folder`)*
- `<hash>`: first five hex characters, uppercase, of SHA-256 over the normalized absolute path. *(proposed normalization: forward slashes, case-folded on Windows)*
- Collisions are accepted as a risk.
- Moving a folder gives it a new sandbox. The old one is orphaned.

## Base directories

Same rule for every XDG directory: use the variable if it is set, otherwise the spec default. Empty or relative values count as unset, as the XDG spec says.

- Linux and macOS: config `~/.config/abox/`, state `~/.local/state/abox/`.
- Windows: the XDG spec doesn't cover Windows. XDG variables are honored if set. Otherwise `%HOME%\.abox\config\` and `%HOME%\.abox\state\`, using `%USERPROFILE%` when `HOME` is not set.

## State

- Declared with `state` in `.abox`: directories in the sandbox, where `~` means `/home/agent`. Examples: agent sessions, a shell history directory.
- Scope: per user and per sandbox.
- Host location: `<abox state dir>/<sandbox>/<path relative to ~>`. For example, `~/.config/opencode/state` is kept at `<abox state dir>/<sandbox>/.config/opencode/state`. *(proposed)*
- Mounting: sbx mounts host directories only at paths that a kit declares with `host-mount@1`, and `--mount HOST:SANDBOX_PATH` works only when a sandbox is created. So `abox create` writes a local mixin that declares one `host-mount@1` per entry, passes it with `--kit`, and passes one `--mount` per entry. *(proposed; verify on a test sandbox)*
- Survives re-create: the sandbox name, and so the host path, does not change.
- Entries are directories, because `host-mount@1` mounts one host directory. Overlapping entries across the chain are errors, as sbx rejects overlapping host mounts. *(proposed validation)*
- Not sbx volumes: `sbx volume` is cloud-only.

## User configuration

- Location: the `abox` config directory (see Base directories).
- Contents: default team, default location, GitHub Enterprise tenant (default `rwe`, giving `rwe.ghe.com`), build settings, and personal `.abox` files (proposed: stored here), which can add but never replace team values. *(proposed)* Also the SSH key pair for `orca`, in `ssh/`.

## Agents

- All four agents use GitHub Copilot on GitHub Enterprise with data residency. The tenant comes from the user config.
- Claude Code: the documented way to change its backend is `ANTHROPIC_BASE_URL` (route requests through a proxy or gateway), with `ANTHROPIC_AUTH_TOKEN`. The Claude Code docs have no GitHub Copilot option (checked: env vars, model config, LLM gateway). Copilot access therefore needs a gateway, which must also respect data residency. *(proposed)*

## Implementation notes *(proposed)*

- `cli/` is a .NET 10 console app using System.CommandLine. It builds one executable, `abox`, for Windows, Linux, and macOS. Tests go in `cli/test`.
- One wrapper class makes every `sbx` call, so flags and version checks live in one place. Arguments come from the table below and are passed as argument lists, never as shell strings.
- Each launcher is one class implementing one launcher interface, in `cli/src/Launchers/`.
- Two internal helpers are not user commands: `exec` (run a command in a sandbox, starting it if stopped) and `pipe` (hidden; used by `hc`).

### `sbx` mapping (internal)

| abox | sbx |
|---|---|
| `create` | `sbx create <squad kit ref> <location> --name <sandbox> --pull always\|never [--kit <mixin>]... [--mount <host>:<path>]...` |
| existence check | `sbx ls --json`, matched by name |
| `exec`, `pipe`, `run` | `sbx exec [-i] [-t] [-u <user>] [-w <dir>] <sandbox> <command>`; starts a stopped sandbox |
| `orca` port | `sbx ports <sandbox> --publish 127.0.0.1::22` |
| `secrets` | `sbx secret` (set, ls, rm) |
| preflight | `sbx version`; `sbx diagnose --format json`; `sbx login` when Authentication fails |

Never call `sbx prune` (it removes every stopped sandbox on the machine) or `sbx reset`.

## Verify before building

1. `abox pipe` passes bytes both ways with no TTY and prints nothing extra. `herdr-app` depends on it.
2. `hc machine add --via`: quoting, and whether TARGET can be omitted.
3. The `hc` install method for the image, that `hc` starts its server on first use, and that its version matches the host's protocol generation.
4. Workspace placement at `WORKDIR`, on a test sandbox with sbx 0.51.0.
5. State mounts: a generated `host-mount@1` mixin passed with `--kit`, and `--mount` per entry.
6. Orca: CLI command names on each OS, and whether its CLI can add an SSH target.
7. Windows locations and executable names of `herdr-app`, `hc`, and Orca.

## Open questions

1. Do any team members use Intel Macs? sbx's prerequisites list Apple silicon only.
2. **Resolved:** `state` stays unique across the chain; duplicate and overlapping state directories are rejected.
3. Which gateway gives Claude Code access to Copilot?
4. `abox secrets` runs `sbx secret` behind the scenes. Service secrets reach the sandbox through the proxy, and registry credentials (JFrog pulls and pushes) stay on the host. Confirm?
5. Does the credentials proxy cover `api.github.com`? If not, `mise` inside a sandbox hits anonymous rate limits.
6. Should `publish` move `latest`, or require additional approval? The current implementation moves the chosen tag after candidate verification, so test publication must use a dedicated `--tag`. This is not an agreed product policy; future remote `create` must account for moving tags and sbx compatibility.
7. `create` while offline: fail, or use a cached image?
8. Which launchers ship in v1? Proposed: `shell` and `herdr-app`. `orca` waits for open questions 10 and 11.
9. Worktrees are lost when a sandbox is replaced. Show that in the confirmation, declare `~/worktrees` as state, or use `abox create --clone`, which keeps commits on the host through a `sandbox-<name>` git remote?
10. Orca takes SSH hosts through its own form, with a picker over `~/.ssh` config files. Should abox write the `abox-<sandbox>` entry into `~/.ssh/config` (with your consent), or should you add the Orca target once by hand?
11. Orca creates git worktrees inside the repository, which is bind-mounted from the host. Accept that, or use `--clone` for Orca sandboxes?
12. Should every squad image include `hc` and `openssh-server`, or only squads whose users run `herdr-app` or `orca`? Proposed: every image.
