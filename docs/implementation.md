# What is implemented

This file says how the commands of [concept.md](concept.md) are built today: the shape
of the code, the decisions that were taken where the concept left room, and what is not
built yet.

## The commands

### `abox build --squad <name> [--root <dir>] [--tag <tag>] [--no-verify]`

1. Reads the `<squad>.abox` file of the project and follows its `base` chain.
2. Checks the chain: a cycle, a kit, an MCP server, a parameter or a state directory
   that is declared twice, overlapping state directories, and a parameter that no
   configuration declares are errors. The error names the duplicate and the files that
   declare it.
3. Writes the workload kit: `workload.yaml` (the descriptor) and `workload.dockerfile`
   (the recipe) in the build directory, with the scripts of the workload copied into it.
4. Writes the state mixin: one `host-mount@1` per state directory.
5. Builds and checks every local kit with `sbx kit inspect`, unless `--no-verify` is
   given.
6. Writes the manifest of the build: `build.json` in the build directory.

The build lives in `<state dir>/builds/<squad>/<tag>/`. The tag is `latest` unless the
command or the user configuration says otherwise.

### `abox create --squad <name> [--location <path>] [--tag <tag>] [--local-image] [--yes]`

1. Reads the manifest of the local build. Without one, the command says which build to
   make.
2. Names the sandbox after the folder: `<folder>-abox-<hash>`, where the hash is the
   first five characters of the SHA-256 of the absolute path.
3. Asks before it replaces a sandbox that is already there, and lists what is lost.
   The state directories on the host are kept: the name of the sandbox does not change.
4. Creates the state directories on the host under `<state dir>/<sandbox>/` and passes
   one `--mount` per directory.
5. Registers the MCP servers that have an url and are not registered yet, and passes
   the names of all of them with `--static-mcp`.
6. Creates the sandbox: `sbx create <workload kit> <location> --name <sandbox> --kit
   <mixin>... --kit-arg <name>=<value>... --mount <host>:<path>... --static-mcp <names>`.

`--local-image` says that the sandbox is made from the local build. It is the only
source in this version, so the flag is the default; `--tag` chooses the tag of the
local build.

### `abox run [<launcher>] [--location <path>] [-- <argv>...]`

1. Checks the programs the launcher needs on the host.
2. Checks that the folder has a sandbox. Without one, the command prints the
   `abox create` command to run.
3. Checks the programs the launcher needs in the sandbox. A missing one means the image
   predates the launcher: the command says to create the sandbox again.
4. Starts the launcher.

The launchers of this version are `shell` and `herdr-app`. `shell` runs `bash -l` in
the workspace with a terminal, or the command after `--`.

## The files

### `.abox`

```yaml
base: cape                # the configuration this one inherits from
workload: mti-ca-dotnet   # only the root configuration declares one
kits:
  - pi-config                        # a kit of the kits folder
  - name: elastic-auth               # a kit with arguments
    args:
      url: ${{ parameters.elastic-url }}
parameters:
  elastic-url: https://foobar.com/mcp
mcp:
  - azuredevops                      # a server that is registered on the host
  - name: elastic                    # a server with an url
    url: ${{ parameters.elastic-url }}
state:
  - ~/.config/opencode/state         # a directory that survives a replacement
```

A base chain combines declarations and never overrides them. Across the whole chain
every kit, every MCP server, every parameter and every state directory is declared
once. A kit is compared by its repository, so two tags of one repository are one kit.

The parameters are named values. A kit or an MCP server uses one with
`${{ parameters.NAME }}`; a parameter no configuration declares is an error.

The personal `.abox` files of the user live in the configuration directory. They add to
a squad, they never replace it.

### `.workload`

```yaml
template: docker.io/sbx/shell-docker-image   # the image the base is built on
mise:
  - nodejs@24.4.3
  - dotnet@10.0.403                            # the last entry is the default
scripts:
  - enable-fzf.sh                             # run while the image is built
```

The generator writes one install step per tool, in the reverse order of the list,
because mise activates the first entry of its list. The GitHub token is passed as a
BuildKit secret, so it never lands in a layer. The generated image has
`/home/agent/workspace` as its working directory.

## The code

```
cli/src/Abox/
  Cli/          the commands and the services they use
  Configuration/ the directories and the user configuration
  Kits/         the .abox and .workload files, the chain, the kit generators
  Build/        the manifest of a local build and the store that keeps it
  Services/     build, create and run
  Launchers/    one class per launcher
  Sbx/          every call to sbx
  Health/       the checks every command runs first
  Infrastructure/ the console, the processes, the YAML
  Naming/       the name of a sandbox
```

- One wrapper class (`SbxClient`) makes every `sbx` call. The flags and the version
  check live in that one place. The arguments are always a list, never a shell string.
- Each launcher is one class behind `ILauncher`.
- The services take their dependencies in their constructor, so the tests can hand them
  a fake sbx.

## The decisions this version took

- **Local images only.** A build stays on the machine and `abox create` uses it. There
  is no registry in this version, so `--local-image` is the default and `--tag` chooses
  the tag of the local build.
- **The kits folder.** The kits and the workloads live in the `kits` folder of the
  project, the `.abox` files in the root of the project.
- **The name of a squad is the name of its file.** `abox build --squad AC` finds
  `ac.abox` and calls the squad `ac`.
- **The state directories of a squad are mounted from the host.** sbx mounts a host
  directory only where a kit asks for it, so abox writes a mixin that asks for every
  state directory and passes one `--mount` per directory.
- **The MCP servers are registered on the host.** A server with an url is registered
  with `sbx mcp add` when it is not registered yet; a server without one has to be
  registered already.

## What is not built yet

- `abox publish`, `abox prune` and `abox secrets`.
- The `orca` launcher, and the `hc` helper and the `openssh-server` of the base image.
- The Claude Code gateway to Copilot on GitHub Enterprise (open question 3).
- The `worktrees` of the agents, which are lost when a sandbox is replaced (open
  question 9).
