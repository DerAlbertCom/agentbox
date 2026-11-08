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
abox build --squad ac                       # make the kits of the squad, check them
abox create --squad ac --location <folder>  # make a sandbox of the build for a folder
abox secrets set                            # store the values the kits ask for
abox run --location <folder>                # open the sandbox of the folder
```

- `build` writes the kits into the build directory (`<state dir>/builds/`) and checks
  every local kit with sbx. With `--no-verify` it writes them without checking, and
  `--tag` stores the build under another name than `latest`.
- `create` names the sandbox after the folder it works on and asks before it replaces
  an existing one. `abox create --location <folder>` again replaces the sandbox.
- `run` starts a launcher (a shell, unless said otherwise) in the sandbox. When the
  squad has a newer build than the sandbox was made of, it offers to replace first.

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
| `state` | Directories inside the sandbox that keep their content when the sandbox is replaced. |
| `secrets` | The services whose values are kept on the host. See [Secrets](#secrets). |

**How the name of a squad is found.** `--squad ac` looks for `ac.abox` in the team
folders of the project (`teams/<team>/ac.abox`), also in the parent directories of the
project, so the command works from anywhere in it, and matches the name without case.
The name of the file is the name of the squad.

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

mise:                                        # the tools of the image
  - nodejs@24.22.2
  - dotnet@10.0.403                          # the last entry is the default

scripts:                                     # run while the image is built
  - enable-fzf.sh
```

- Pin the `template` by tag or digest, not by `latest`. The default template carries a
  Docker Engine inside the sandbox, so agents can build and run containers there.
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
