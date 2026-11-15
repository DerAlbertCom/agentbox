# Authoring squads

This guide is for the author of a squad: the person who says which base image the
sandboxes of a team are built on, which kits they carry and what they may reach. It says
what the configuration files look like and how they fit together. Where the ideas and
the decisions behind a feature live, it points to [concept.md](concept.md); how a
feature is built is in [implementation.md](implementation.md).

## How a project is put together

```
.
├── teams/
│   └── sample/
│       ├── ac.abox            a squad: which kits, which parameters, what it may reach
│       ├── cape.abox          the squad the other squads inherit from
│       └── sample.workload    the workload: the base image of the team
├── kits/
│   └── pi-config/             a kit: a directory with a descriptor
│       └── pi-config.yaml
└── cli/                       the abox command line
```

A team is a folder under `teams`: it holds the `.abox` files of its squads, its
workload and the scripts the workload runs. The kits stay in the `kits` folder of the
project, so every team can use them. Everything is YAML, every key that abox reads is
named in this guide, and an unknown key is an error.

## From files to a sandbox

```
abox build --team sample                     # make the kits of the team, check them
abox create --team sample --local --location <folder>  # test the local build for a folder
abox secrets set --team sample --squad ac    # store the values the kits of the squad ask for
abox run --location <folder>                 # open the sandbox of the folder
```

- `build` makes the kits of the team: the workload and every squad of it that no other
  squad of the team names as its `base`. A squad that others build on comes with the
  squad that inherits from it. The kits go into the build directory
  (`<state dir>/builds/`). A normal build checks additional local kit declarations
  with `sbx kit inspect`, passing their resolved arguments, unless `--no-verify` is
  given. Inspection does not build images; sbx 0.51.0 cannot inspect recipe-based
  local kits, which require `--no-verify` and are processed by sbx at creation.
  A normal build always builds the workload image, even with `--no-verify`.
  `--tag` stores the build under another name than `latest`.
- `create` names the sandbox after the folder it works on and asks before it replaces
  an existing one. Remote create requires `--squad`; only `--local` asks which squad
  of the team to use when it is omitted, and uses the only one without asking.
  `--tag` uses another tag than `latest`. By default it pulls the published set from
  the user's `pullRepository` (`mtiae-docker-release` by default), with state and MCP
  settings from its bundled `squad.json`; no local sources or build metadata are
  needed. `--local` uses the prebuilt local workload and its runtime kits with
  `--pull never`, and reads state and MCP settings from the local build manifest.
  Local team discovery starts at the sandbox location and searches its parents;
  `--root <dir>` selects a separate source project.
  `abox create --team sample --local --location <folder>` again replaces the sandbox.
- `run` starts a launcher (a shell, unless said otherwise) in the sandbox. When the
  squad has a newer build than the sandbox was made of, it offers to replace first.

The user configuration (in the abox config directory) can fix the defaults:
`defaultTeam` is the team the commands use when they are not asked for one.
An explicit `--team` replaces that default; `build` and `publish` accept repeated
`--team` selections, while `create` and `secrets` select one team. Without either
an explicit team or a default, these commands report the same missing-team error.

### Finding sources from nested folders

`build`, `publish` and `secrets` search for the selected team starting in the current
directory and continuing through its parents. Local `create` starts at its resolved
`--location`/`defaultLocation` instead. For each command, `--root <dir>` overrides
that start directory (`create` requires `--local` with it). Relative roots are
resolved against the invocation directory before searching parents, just like their
absolute equivalents; a root need not itself contain the teams folder.

Once the team is found, the project that owns that teams folder also supplies the
local kits and shared workloads, not the nested invocation folder or sandbox
location. `teamsDirectory` (default `teams`) and `kitsDirectory` (default `kits`)
select the configured directories within that owning project, including nested
paths such as `config/teams` and `shared/kits`. The workload beside the root
configuration still wins over a shared workload. Personal configuration additions
use the selected project's kits too. Builds record the owning project root so a
later local replacement can find the same sources.

For example, from `project/src/app`, with sources under `project/teams` and
`project/kits`, `abox build --team sample`, `abox publish --team sample` and
`abox secrets list --team sample --squad ac` use the same project sources as when
run from `project`. `abox create --local --team sample --squad ac --location .`
selects the same team's local build. A separate workspace can use
`abox create --local --team sample --squad ac --location <workspace> --root <project>`.

## The squad: `.abox`

```yaml
# ac.abox — the squad of a team
base: cape                                   # the squad this one inherits from

kits:                                        # the kits every sandbox of the squad gets
  - github-enterprise-auth                   # a kit of the kits folder
  - docker.io/acme/elastic-auth:1.0          # a published kit, used as it is
  - name: elastic-auth                       # a kit that takes arguments
    args:
      url: ${{ parameters.elastic-url }}

parameters:                                  # named values kits and MCP servers use
  elastic-url: https://foobar.com/mcp

mcp:                                         # the MCP servers every agent gets
  - azuredevops                              # a server registered on the host
  - name: elastic                            # a server abox registers itself
    url: ${{ parameters.elastic-url }}

state:                                       # directories that survive a replacement
  - ~/.config/opencode/state

secrets:                                     # the services the kits ask for
  - service: github
    prompt: The GitHub token for GitHub Enterprise Cloud
  - service: elastic
    host: api.elastic.co
    env: ELASTIC_API_KEY
    prompt: The API key for Elastic
```

| Key | What it does |
|---|---|
| `base` | The squad this one inherits from. A chain is allowed; a cycle is an error. |
| `workload` | The `.workload` file of the team. Only the squad without a `base` declares one, its image is the base of every sandbox of the team. |
| `kits` | The kits of the squad. See [Kits](#kits). |
| `parameters` | Named values. Kits and MCP servers read them with `${{ parameters.NAME }}`. |
| `mcp` | The MCP servers of every agent. A server without `url` has to be registered on the host already (`sbx mcp add`); one with `url` abox registers itself. |
| `state` | Directories inside the sandbox that keep their content when the sandbox is replaced. See [State](#state). |
| `secrets` | The services whose values are kept on the host. See [Secrets](#secrets). |

**How a squad is found.** The commands take `--team <name>`; the squad is the file of
that team folder, matched without regard to case: `AC` → `teams/sample/ac.abox`. A
`base` is the file of the squad's own team folder first, then the team folders of the
parent directories — a base that several other teams hold is an error.

**How inheritance works.** A squad that has a `base` adds kits, parameters, MCP
servers, state directories and secrets to what its base declares. It never replaces or
removes anything. Across the whole chain everything is declared once: two kits of one
repository (even with different tags), two MCP servers of one name, two parameters of
one name, two state directories or two secrets of one service are errors.

**The files of the user.** The user can add personal `.abox` files in the abox
configuration directory. They add to a squad, they never replace it.

## Kits

A kit gives every agent of the sandbox a capability: a configuration file for the
agent, an accessible host, or a credential. The descriptor of a kit follows the
[kit spec](https://github.com/docker/sandbox-kit-spec); abox builds a local kit as it
is and passes it to sbx.

A kit of the `kits` folder is a directory with the descriptor of the same name in it:

```yaml
# kits/pi-config/pi-config.yaml
# syntax=docker/sandbox-kit:3
schemaVersion: "3"
kind: mixin
displayName: pi configuration
version: "1.0.0"

capabilities:
  - type: com.docker.sandbox/agent-context@2
    config:
      directory: /home/agent/.pi/agent
      filename: AGENTS.md
      content: |
        You work in a sandbox.
```

- A name without a slash or a colon is a directory in the `kits` folder. A name with a
  slash or a colon, like `docker.io/acme/pi-config:1.0`, is a published kit that sbx
  resolves and that abox does not build.
- A kit entry with `args` hands the values to the kit in the way the kit spec says.
- This repository keeps working kits to copy from: `kits/pi-config` writes an agent
  configuration, `kits/github-enterprise-auth` reaches a host through a credential the
  host holds.

## MCP servers

An `mcp` entry is either a scalar registered-server name or a mapping with `name`
and an optional `url`:

```yaml
mcp:
  - azuredevops
  - name: elastic
    url: ${{ parameters.elastic-url }}
```

A name-only entry must already be registered on the host. For a URL entry, abox
registers the server if its name is missing; it does not replace an existing host
registration. URLs support the same parameter references as kit arguments. MCP
names must be unique across the squad's base chain and personal additions. Local
build manifests and published squad metadata preserve these names and resolved URLs.

Only `name` and `url` are supported mapping keys. `command`, `args` and `headers`
are rejected as unknown keys, even when empty: abox does not support launching MCP
commands or configuring arguments, authentication headers or other transports.
The error identifies the source `.abox` file and offending `mcp` field before
generating that squad's build output, registering servers or replacing a sandbox.
Remove the unsupported fields and use an existing host registration by name, or
declare a name and URL.

## State

State declarations use sandbox paths, not host paths: `~` means `/home/agent`.
Use `~`, `~/`, `~/directory` or an absolute Linux path beginning with a single `/`.
The root `/` is not allowed. Relative paths, `~other-user`, leading `//` (UNC-like
syntax), backslashes, drive/stream colons and `.` or `..` path segments are rejected.
Names must be portable across supported hosts: no control characters,
`< > " | ? *`, trailing dots/spaces or Windows device names such as `CON`, `NUL`,
`COM1` and `LPT1` (including device names with extensions).

For declarations in `.abox`, surrounding whitespace is trimmed and repeated
interior forward slashes and trailing slashes are collapsed. For example,
`~/.config//opencode/state/` means `/home/agent/.config/opencode/state`.
Stored local manifests and published metadata must already contain the canonical
absolute sandbox path and its matching host-relative path; invalid stored mappings
are rejected, never silently repaired. Rebuild a local squad or rebuild/republish
an invalid published squad.

The existing host layout is preserved under `<abox state dir>/<sandbox>/`:

| Sandbox declaration | Path below the sandbox's host state root |
|---|---|
| `~/.config/opencode/state` or `/home/agent/.config/opencode/state` | `.config/opencode/state` |
| `/var/lib/cache` | `var/lib/cache` |
| `~`, `~/` or `/home/agent` | `home/agent` (the existing whole-home mapping) |

Every entry must be distinct and non-overlapping both in the sandbox and in its
host mapping. Host comparisons ignore case and Unicode normalization differences
on every platform, so a squad cannot work on Linux but alias another entry on
Windows or macOS. For example, `~/.cache` conflicts with `~/.CACHE/sessions` and
with `/.cache`, which maps to the same host directory. The whole-home declaration
cannot be combined with any of its sandbox descendants.

All state declarations are validated before preparing host state directories,
registering MCP servers or deleting/replacing a sandbox; no mount request is made
for invalid state. Valid state keeps the same host paths and contents on recreate.
This does not add protection against malicious concurrent filesystem changes.

## Secrets

A kit asks for a service with the `credential@1` capability of the kit spec. The kit
never sees the value: the host keeps it and injects it where the sandbox talks to that
service. The `secrets` section declares how the value is obtained:

- A service sbx knows carries `service` and `prompt`. `abox secrets set` asks for the
  value once and stores it under the name of the service.
- A service sbx does not know carries `service`, `host` and `env` besides: the value is
  stored as a custom secret, and the variable named in `env` stands in inside the
  sandbox where the sandbox talks to `host`.
- A value that is built from parts carries `inputs` (each with its own `prompt`),
  `separator` (default `:`) and `encoding` (`plain` or `base64`).

`abox secrets list` shows the services of a squad, the kits that ask for them and
whether the values are stored; `abox secrets remove` takes the values off the host.

## The workload: `.workload`

The workload is the `.workload` file of the squad without a `base`. It says what the
base image of the team is:

```yaml
# teams/sample/sample.workload
template: docker.io/sbx/shell-docker-image   # the image the base is built on
publishRepository: mtiae-developer-container-images-local

apt:                                         # the packages of the image
  - git

mise:                                        # the tools of the image
  - nodejs@24.22.2
  - dotnet@10.0.403                          # the last entry is the default

scripts:                                     # run while the image is built
  - enable-fzf.sh
```

- Pin the `template` by tag or digest, not by `latest`. The default template carries a
  Docker Engine inside the sandbox, so agents can build and run containers there.
- `publishRepository` is the JFrog repository name, not a URL or image reference.
  `abox publish` uses `rwest.jfrog.io/<publishRepository>` for the squad's set and
  all locally built components. Use one lowercase registry path segment: letters
  and digits separated by `.`, `_`, `__` or one or more `-`; no host, slash, tag or
  whitespace. It is optional for local builds, but required to publish; there is no
  fallback repository. Each squad inherits the workload selected by its root
  `.abox`, including this setting. Rebuild with `--no-personal` after changing it.
- The `apt` entries are package names; they are installed with one `apt-get install`
  step while the image is built, as root and before the tools, so the tools then land
  in the home of the agent user with the packages behind them.
- The tools are installed with mise. The versions of one tool are installed in one
  step, so a tool can carry several versions; the last entry decides which one an
  interactive shell starts with.
- The workload of a team lives in the team folder, next to its `.abox` files. A
  workload that several teams use can also live in the `kits` folder of the project;
  abox looks in the team folder first.
- The scripts live next to the file, are copied into the build and run at the end of
  the image build.
- `abox build` turns the file into a v3 kit: a descriptor and a Dockerfile in the build
  directory.

## What abox checks

Every command runs the same checks first: sbx is installed, new enough and healthy.

When a build resolves the chain of a squad it reports what it can prove wrong, and the
error names the file that is wrong:

- a cycle, a duplicate or an overlap between two squads of one chain
- a parameter that a kit or an MCP server reads and that no configuration declares
- an unknown key in any YAML file
