# What is implemented

This file says how the commands of [concept.md](concept.md) are built today: the shape
of the code, the decisions that were taken where the concept left room, and what is not
built yet. It is the file to read first when you pick this work up.

## The commands

### `abox build --squad <name> [--root <dir>] [--tag <tag>] [--no-verify]`

1. Reads the `<squad>.abox` file of the project and follows its `base` chain. The file is
   looked for in the directory of the command and in its parent directories, so the
   command works from anywhere in the project. `--squad` can be given more than once.
2. Checks the chain: a cycle, a kit, an MCP server, a parameter, a state directory or a
   secret that is declared twice, overlapping state directories, and a parameter that no
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
7. Keeps a record of the sandbox in `<state dir>/sandboxes/<sandbox>.json`: the squad it
   comes from and the build it was made of.

`--local-image` says that the sandbox is made from the local build. It is the only
source in this version, so the flag is the default; `--tag` chooses the tag of the
local build.

### `abox run [<launcher>] [--location <path>] [-- <argv>...]`

1. Checks the programs the launcher needs on the host.
2. Checks that the folder has a sandbox. Without one, the command prints the
   `abox create` command to run.
3. Asks the runtime where the workspace of the sandbox is (`WORKSPACE_DIR`).
4. Offers to replace the sandbox when the build of its squad is newer than the one the
   sandbox was made of. abox knows the squad of a sandbox because it keeps a record of
   the sandboxes it created, in `<state dir>/sandboxes/`.
5. Checks the programs the launcher needs in the sandbox. A missing one means the image
   predates the launcher: the command says to create the sandbox again.
6. Starts the launcher.

The launchers of this version are `shell` and `herdr-app`. `shell` runs `bash -l` in
the workspace with a terminal, or the command after `--`.

### `abox secrets list|set|remove [--squad <name>] [--service <name>]`

The kits of a squad ask for a service with the `credential@1` capability. The `secrets`
section of the configuration says how the value of that service is obtained. abox keeps
the values on the host, through `sbx secret`, so they never enter a sandbox.

- `list` shows the services the kits ask for, the kits that ask for them, and whether
  the value is stored on the host.
- `set` asks for the values and stores them. The value goes through the standard input
  of sbx, so it stays out of the process list and out of the shell history.
- `remove` takes the values off the host.

### `abox prune`

Sbx stores every kit it builds as a template image: one image per build plus one per kit
in it, and the sources of the kits under a own repository (sbx-kit-src). They pile up,
because a build replaces them with new ones and sbx never takes the old ones away.

`abox prune` lists the template images (`sbx template ls --json`), groups the images of
the assembled repository by their build id (the tags `«id»`, `«id»-kit-N` and
`«id»-template` of one build share the id), and keeps the newest build together with its
kit sources: an sbx-kit-src image stays as long as its image id is in the kept set. What
is left goes with `sbx template rm`, one image at a time, and sbx asks nothing (--force).

The command never touches sandboxes, secrets, or images of any other repository, and it
never removes an image whose age or id sbx does not report: prune only throws away what
it can prove is stale.

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
secrets:
  - service: github                  # a service sbx knows
    prompt: The GitHub token for GitHub Enterprise Cloud
  - service: elastic                 # a service sbx does not know
    host: api.elastic.co
    env: ELASTIC_API_KEY
    prompt: The API key for Elastic
  - service: registry                # a value that is built from several inputs
    host: registry.example.com
    env: REGISTRY_TOKEN
    inputs:
      - name: username
        prompt: The user name
      - name: password
        prompt: The password
    separator: ":"
    encoding: base64
```

A base chain combines declarations and never overrides them. Across the whole chain
every kit, every MCP server, every parameter, every state directory and every secret is
declared once. A kit is compared by its repository, so two tags of one repository are
one kit.

The parameters are named values. A kit or an MCP server uses one with
`${{ parameters.NAME }}`; a parameter no configuration declares is an error.

The personal `.abox` files of the user live in the configuration directory. They add to
a squad, they never replace it.

The secrets say how a value is obtained:

| Key | Meaning |
|---|---|
| `service` | The name of the secret in the host store. The kits name the same service. |
| `prompt` | What the user is asked for. |
| `inputs` | The parts the value is built from. Without them, one value is asked for. |
| `separator` | How the parts are joined. `:` by default. |
| `encoding` | `plain` (the default) or `base64`. |
| `host`, `env` | The host and the variable of a service sbx does not know. With them the secret is stored with `sbx secret set-custom`. |

### `.workload`

```yaml
template: docker.io/sbx/shell-docker-image   # the image the base is built on
mise:
  - nodejs@24.22.2
  - dotnet@10.0.403                            # the last entry is the default
scripts:
  - enable-fzf.sh                             # run while the image is built
```

The generator writes one install step per tool. The versions of one tool are installed in
one step, so mise keeps them all, and every list is reversed, because mise activates the
first entry of its list: the last entry of the workload is the default of the image.
The GitHub token is passed as a BuildKit secret, so it never lands in a layer. The
generated image has `/home/agent/workspace` as its working directory.

## The code

```
cli/src/Abox/
  Program.cs        runs the command line
  Cli/              the composition root: the service collection and the root command
  Features/
    Build/          abox build: the command and the service that does its work
    Create/         abox create: the command and the service that does its work
    Run/            abox run: the command and the service that does its work
    Secrets/        abox secrets: the command and the service that does its work
    Prune/          abox prune: the command and the service that does its work
    Sandbox/        the commands other programs call: abox exec and abox pipe
    CommandHandling turns an abox error into a message and an exit code
  Configuration/    the directories and the user configuration
  Kits/             the .abox and .workload files, the chain, the kit generators
  Build/            the manifest of a local build and the store that keeps it
  Launchers/        one class per launcher
  Sbx/              every call to sbx
  Health/           the checks every command runs first
  Infrastructure/   the console, the processes, the YAML
  Naming/           the name of a sandbox
```

- The code is arranged by use, not by kind: one folder per command holds the command
  and the service that does its work, and shared, technical services keep one place
  each (`Configuration`, `Kits`, `Build`, `Launchers`, `Sbx`, `Health`,
  `Infrastructure`, `Naming`).
- One wrapper class (`SbxClient`) makes every `sbx` call. The flags and the version
  check live in that one place. The arguments are always a list, never a shell string.
- Each launcher is one class behind `ILauncher`.
- Each command is a class that the container builds and that takes its dependencies
  in its constructor; nothing looks the commands up by hand. The root command holds
  every command the container has, one per slice.
- A slice registers itself: `AddAbox` calls one method per slice and that method adds
  the command and the service of the slice.
- The services take their dependencies in their constructor, so the tests can hand them
  a substitute sbx (NSubstitute in the test project).
- The YAML is read in one place (`YamlReader`), so every file is read the same way and
  an unknown key is an error everywhere.

## The decisions this version took

- **Local images only.** A build stays on the machine and `abox create` uses it. There
  is no registry in this version, so `--local-image` is the default and `--tag` chooses
  the tag of the local build.
- **The kits folder.** The kits and the workloads live in the `kits` folder of the
  project, the `.abox` files in the root of the project.
- **The name of a squad is the name of its file.** `abox build --squad AC` finds
  `ac.abox` and calls the squad `ac`.
- **The workspace is where the runtime puts it.** The runtime mounts the folder of the
  host at the same path in the sandbox and says so with `WORKSPACE_DIR`, so the
  launcher asks for it instead of assuming `/home/agent/workspace`.
- **The state directories of a squad are mounted from the host.** sbx mounts a host
  directory only where a kit asks for it, so abox writes a mixin that asks for every
  state directory and passes one `--mount` per directory.
- **The MCP servers are registered on the host.** A server with an url is registered
  with `sbx mcp add` when it is not registered yet; a server without one has to be
  registered already.
- **The secrets live on the host.** The kits ask for a service, the configuration says
  how its value is obtained, and `sbx secret` keeps it. The value never goes into a
  sandbox and never into the arguments of a process.

## What is not built yet

- `abox publish`. The kit set is not published to a registry, and `abox prune` removes
  only the template images of sbx, not the local builds of the build store.
- The `orca` launcher, and the `hc` helper and the `openssh-server` of the base image.
- The Claude Code gateway to Copilot on GitHub Enterprise (open question 3).
- The `worktrees` of the agents, which are lost when a sandbox is replaced (open
  question 9).
- The agents themselves (GitHub Copilot, OpenCode, pi, Claude Code) are not configured
  by the kits of this repository yet: the kits carry the credentials and the guidance,
  the agent configuration is still to come.

## Where to go next

The open questions of [concept.md](concept.md) are the list of what is left. The ones
that are ready to be picked up:

1. **`abox publish`** — push the kit set of a squad to a registry as one reference, so
   `abox create` can pull it. The build already produces everything a set needs: the
   workload kit, the mixins and the manifest.
2. **The rest of `abox prune`** — the local builds of the build store are not pruned
   yet. The build store knows what is there; the sandboxes say what is in use.
3. **The agent kits** — the kits of `kits/` carry the credentials and the guidance of
   the agents, but not their configuration files yet.
4. **The `orca` launcher** — the SSH key pair, the host entry and the Orca CLI are
   described in the concept and have not been written.
