# What is implemented

This file says how the commands of [concept.md](concept.md) are built today: the shape
of the code, the decisions that were taken where the concept left room, and what is not
built yet. It is the file to read first when you pick this work up.

## The commands

### `abox build --team <name> [--root <dir>] [--tag <tag>] [--no-verify] [--no-personal]`

1. Resolves the team: its folder under `teams`, found in the directory of the command
   or one of its parent directories. `--team` can be given more than once; without it
   the user configuration `defaultTeam` is used.
2. Builds every squad of the team that no other squad of the team names as its `base`
   — the squads named as a base are built with the squad that inherits from them.
3. Reads the `.abox` of a squad inside the team folder and follows its `base` chain,
   the base first. A base is the file of the squad's own team folder first, then the
   team folders of the parent directories; a base that several other teams hold is
   an error.
4. Checks the chain: a cycle, a kit, an MCP server, a parameter, a state directory or a
   secret that is declared twice, overlapping state directories, and a parameter that no
   configuration declares are errors. The error names the duplicate and the files that
   declare it.
5. Writes the workload kit: `workload.yaml` (the descriptor) and `workload.dockerfile`
   (the recipe) in the build directory, with the scripts of the workload copied into it.
6. Writes the state mixin: one `host-mount@1` per state directory.
7. Builds and checks every local kit with `sbx kit inspect`, unless `--no-verify` is
   given.
8. Writes the manifest of the build: `build.json` in the build directory. It records
   whether personal configuration was included and the resolved workload's
   `publishRepository`. Only `--no-personal` builds capture SHA-256 fingerprints of
   configuration files (including the `.workload`) and local source trees (including
   generated workload/state kits), checking them before and after sbx verification.

The build lives in `<state dir>/builds/<squad>/<tag>/`. The tag is `latest` unless the
command or the user configuration says otherwise.

Personal `.abox` files remain included for local builds by default. Maintainers
must use `--no-personal` to exclude them from builds intended for publication.
Default/local builds do not run the publication fingerprint checks and retain
their existing symbolic-link/reparse-point support. `--no-personal` builds are
strict: their inputs must be ordinary files/directories. Even a default build
with no personal files must be rebuilt with `--no-personal` before publishing,
because it has no source fingerprints.

### `abox publish --team <name> [--root <dir>] [--tag <tag>] [--builder <name>]`

1. Resolves the teams like `build` does (`--team` can be repeated, or comes from
   `defaultTeam`). The tag comes from `--tag`, `defaultTag`, or `latest`. Publishing
   checks Docker/Buildx only; it does not call sbx or require its daemon/sign-in.
2. Checks **all selected teams** before the first push: every leaf squad must have
   a matching local build, known provenance excluding personal configuration,
   a valid workload `publishRepository`, existing kit descriptors, and unchanged
   source fingerprints. Legacy manifests
   must be rebuilt. Invalid names and case-insensitive team/squad collisions are
   rejected. All local source trees are copied to private temporary snapshots and
   checked against the build's fingerprints before any registry write. Symbolic
   links/reparse points in build inputs are rejected rather than followed.
3. Builds and pushes the workload, every local mixin and the generated state mixin
   through the v3 frontend, using `docker buildx build --file <descriptor> --push`.
   Kits already named by registry reference are inspected and reused, not copied
   or repushed. Supplied arguments must be declared **create-phase** arguments;
   supplied `buildArg`/`buildSecret` arguments are explicitly unsupported and fail
   before the first push. Arguments are fixed only in the set's `kits[].args`,
   never duplicated as Buildx build arguments.
4. Adds a small configuration mixin containing `/usr/share/abox/squad.json`: the
   resolved team, squad, tag, build timestamp, kit names/arguments, MCP declarations
   and state declarations. Host source paths, raw `.abox` files and secret values
   are not included. Do not put credentials in kit arguments; use host secrets.
5. Publishes a `kind: set` descriptor listing those references and their resolved
   kit arguments. The frontend pins the references by digest and merges their
   layers/declarations into one ordinary workload kit, without flattening the layers.
   Explicit minimal provenance requests retained index annotations. Every pushed
   digest is read from Buildx's metadata file and checked through
   `docker buildx imagetools inspect --raw`. Every runnable platform manifest must
   retain its v3 descriptor and expected kind; an index-only annotation is not
   sufficient. Recognized attestation manifests are skipped.
6. Pushes the set under a unique candidate tag and verifies it before promoting its
   immutable digest with `imagetools create --prefer-index=false --tag <target>`.
   The requested tag is inspected again before success is reported.

The destination is **`rwest.jfrog.io/<publishRepository>`**, using the repository
recorded from each squad's resolved `.workload` when it was built. The included
workloads select `mtiae-developer-container-images-local`; there is no hardcoded
repository fallback. Missing/invalid repositories fail before any Docker calls.
Changing a workload (including its repository) requires a new `--no-personal`
build, and older manifests without this setting must be rebuilt. A
team always publishes **`agentbox-rwest-<team>/<squad>:<tag>`**, regardless of its
squad count. Distinct path segments keep team, squad and component identities
separate; adding another squad does not rename existing images. Names are
lowercased, and case collisions are rejected. The current implementation moves
the chosen tag, including `latest`; use a dedicated `--tag` to test without
replacing it. Whether a different approval policy is wanted remains a product
question. The local manifest's unqualified team `Reference` is an ownership marker,
not the remote publication name.

Each component uses a unique `publish-<id>` tag under
`agentbox-rwest-<team>/<squad>/components/{workload,kit-N,state,config}`. The final
set candidate uses `agentbox-rwest-<team>/<squad>:publish-<id>`. A failed component
push or failed candidate inspection cannot move the requested tag. This is not
a transaction across squads/teams; failures after promotion cannot automatically
roll back a tag. Temporary snapshots/descriptors are removed even on failure;
cleanup failures produce warnings without hiding the original error. Images
already pushed remain in JFrog. Registry retention is separate; `abox prune` does
not remove components or candidates.

**Maintainer prerequisites and authentication:** Docker with the Buildx plugin and
a working BuildKit builder. A `docker-container` builder is recommended to preserve
OCI metadata and avoid the classic image store round trip:

```sh
docker buildx create --name abox-publisher --driver docker-container --bootstrap
docker login rwest.jfrog.io
abox build --team sample --no-personal
abox publish --team sample --builder abox-publisher
```

`--builder` selects an existing builder; otherwise Buildx uses its selected builder
(including `BUILDX_BUILDER`). Nothing is installed or provisioned automatically.
The builder's default platform is published. The JFrog username and access token
must have read/write access to the target repository; Buildx uses Docker's host
credential configuration/credential helper, not `abox secrets` or sbx's sign-in.
For automation, feed the token to `docker login --username <user> --password-stdin
rwest.jfrog.io`. Credentials are never passed by abox as process arguments or
included in image layers. If `GITHUB_TOKEN` is set in the host environment, the
workload receives it through the existing `github_token` BuildKit secret mount.

Publication builds from fingerprint-checked snapshots of the generated workload
and local kits, not from sbx's internal template store. Source changes require a
new build. This protects local inputs, not changing registry dependencies or
network downloads in recipes: pin external images/digests and tool versions for
reproducibility. `sbx kit push` is deliberately not used: its
legacy `spec.yaml` packager does not understand these v3 descriptors; see
[artifact-format.md](artifact-format.md).

### `abox create --team <name> [--squad <name>] [--location <path>] [--tag <tag>]
[--local] [--yes]`

1. Resolves the team like `abox build` does, from the current directory and its parents. The
   team comes from `--team` or the user configuration `defaultTeam`. The squad comes
   from `--squad`; without it the command asks which squad of the team to use, and
   uses the only one without asking.
2. Reads the manifest of the local build for the selected tag, for state mounts and MCP
   registration. Without one, the command says which build to make. Pulling also checks
   that the metadata belongs to the selected team.
3. Names the sandbox after the folder: `<folder>-abox-<hash>`, where the hash is the
   first five characters of the SHA-256 of the absolute path.
4. Asks before it replaces a sandbox that is already there, and lists what is lost.
   The state directories on the host are kept: the name of the sandbox does not change.
5. Creates the state directories on the host under `<state dir>/<sandbox>/` and passes
   one `--mount` per directory.
6. Registers the MCP servers that have an url and are not registered yet, and passes
   the names of all of them with `--static-mcp`.
7. Creates the sandbox from
   `rwest.jfrog.io/<pullRepository>/agentbox-rwest-<team>/<squad>:<tag>`, with
   `--pull always`. The set already contains its mixins and arguments. With `--local`,
   uses the workload kit directory, `--kit <mixin>... --kit-arg <name>=<value>...` and
   `--pull never` instead. Both pass `--mount <host>:<path>... --static-mcp <names>`.
8. Keeps a record of the sandbox in `<state dir>/sandboxes/<sandbox>.json`: the team
   and the squad it comes from, the build metadata and the pulled reference (if any).

The user setting `pullRepository` defaults to **`mtiae-docker-release`**. It accepts
one lowercase JFrog repository path segment; an omitted or blank setting uses the
default. It is independent of the workload's `publishRepository`. See
[the settings file location and example](../README.md#user-settings).
`--tag` chooses the published set and local build metadata tag, `latest` by default.
`--local` opts into local kits and includes their local/personal customizations.
Pull failures are reported without a silent local fallback.

**Current limitation:** creation still requires team files and a local build manifest
for state/MCP metadata. Keep it aligned with the published tag. The published set's
bundled squad configuration is not read yet, and local kit arguments/mixins are not
reapplied over the set. A pulled sandbox is not offered a local-build replacement by
`abox run`; recreate it explicitly to refresh the published tag.

### `abox run [<launcher>] [--location <path>] [-- <argv>...]`

1. Checks the programs the launcher needs on the host.
2. Checks that the folder has a sandbox. Without one, the command prints the
   `abox create` command to run.
3. Asks the runtime where the workspace of the sandbox is (`WORKSPACE_DIR`).
4. For a locally created sandbox, offers to replace it when the build of its squad is newer than the one the
   sandbox was made of. abox knows the squad of a sandbox because it keeps a record of
   the sandboxes it created, in `<state dir>/sandboxes/`. Replacement stays local.
5. Checks the programs the launcher needs in the sandbox. A missing one means the image
   predates the launcher: the command says to create the sandbox again.
6. Starts the launcher.

The launchers of this version are `shell` and `herdr-app`. `shell` runs `bash -l` in
the workspace with a terminal, or the command after `--`.

### `abox secrets list|set|remove --team <name> --squad <name> [--service <name>]`

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

With `--full`, the prune reaches for every sbx kit image of a known age, not only the
stale sets: it removes everything, and what a sandbox uses, sbx refuses and keeps. The
prune reports what sbx keeps and is happy about it. With `--sbx`, it stops the running
sandboxes first (`sbx stop`, one per sandbox, the kit builder included).

The command never touches secrets, or images of any other repository, and it never
removes an image whose age or id sbx does not report: prune only throws away what it
can prove is stale.

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
publishRepository: mtiae-developer-container-images-local
apt:
  - git
mise:
  - nodejs@24.22.2
  - dotnet@10.0.403                            # the last entry is the default
scripts:
  - enable-fzf.sh                             # run while the image is built
```

`publishRepository` is optional for local builds but required for publication.
It names one lowercase registry path segment, not a host, URL, path or tag: letters
and digits separated by `.`, `_`, `__` or one or more `-`, with no whitespace.
The root `.abox` selects the workload; children inherit its publish repository
along with its image settings, including when that workload is shared in `kits/`.

The packages of `apt` are installed in one step before the tools, as root and with
`--no-install-recommends`, and the package names are checked; the image switches back
to the agent user after them. The generator writes one
install step per tool. The versions of one tool are installed in one step, so mise
keeps them all, and every list is reversed, because mise activates the first entry of
its list: the last entry of the workload is the default of the image.
The GitHub token is passed as a BuildKit secret, so it never lands in a layer. The
generated image has `/home/agent/workspace` as its working directory.

## The code

```
cli/src/Abox/
  Program.cs        runs the command line
  Cli/              the composition root: the service collection and the root command
  Features/
    Build/          abox build: the command and the service that does its work
    Publish/        abox publish: v3 frontend builds, JFrog pushes and set assembly
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

- **Published creation, explicit local builds.** `abox create` pulls a published set
  from the user's `pullRepository`; `--local` uses the local build instead.
  `abox publish` pushes v3 kit sets to the workload's `publishRepository` through
  Buildx. Creation still needs local state/MCP metadata. `--tag` chooses the tag of
  the local build and the published set, `latest` by default.
- **The teams folder.** A team is a folder under `teams`. Its `.abox` files (the squads
  of the team), its `.workload` and the scripts of the workload live in it. The kits
  stay in the `kits` folder of the project, so every team can use them; the workload of
  a team lives in the team folder, a shared one can stay in the `kits` folder.
- **The name of a squad is the name of its file.** `abox create --squad AC` finds
  `ac.abox` in a team folder and calls the squad `ac`.
- **A build is of a team.** `--team` names the team folder, and the build makes every
  squad of it that no other squad names as its base. When `abox create` is not asked
  for a squad and the team has more than one, it asks which one.
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

- Reading the published set's bundled squad configuration in `abox create`, so creation
  no longer needs team sources and local build metadata.
- Pruning local builds in the build store and registry components. `abox prune`
  currently removes only sbx template images.
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

1. **Self-contained remote `abox create`** — consume the published kit set's bundled squad
   configuration, without requiring the source repository and a local build.
2. **The rest of `abox prune`** — the local builds of the build store are not pruned
   yet. The build store knows what is there; the sandboxes say what is in use.
3. **The agent kits** — the kits of `kits/` carry the credentials and the guidance of
   the agents, but not their configuration files yet.
4. **The `orca` launcher** — the SSH key pair, the host entry and the Orca CLI are
   described in the concept and have not been written.
