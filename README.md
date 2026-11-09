# abox

`abox` gives every folder of your machine its own AI agent sandbox, built on
[Docker Sandboxes](https://docs.docker.com/ai/sandboxes/).

The idea, the roles and the decisions behind the commands are in
[docs/concept.md](docs/concept.md). What is built today, and what is left, is in
[docs/implementation.md](docs/implementation.md) — **start there when you pick this
work up**. How to write the squads, the kits and the workloads of a project is in
[docs/authoring.md](docs/authoring.md).

## The commands

| Command | What it does |
|---|---|
| `abox build --team <name>` | Prebuilds reusable local workload images with Docker Buildx and prepares the v3 kits of every squad. No registry writes. |
| `abox build --team <name> --metadata-only` | Maintainer-only source preparation without image builds. Clears local image selection for this tag; not required for remote create. |
| `abox publish --team <name> [--tag <tag>] [--builder <name>]` | Builds and pushes the local kit sets to `rwest.jfrog.io/<publishRepository>`, configured in each workload. Requires Docker Buildx and host registry sign-in. |
| `abox create --team <name> --squad <name> [--location <path>] [--local]` | Pulls a self-contained published squad set, or uses a local prebuild with `--local`. Only local create can ask for an omitted squad. |
| `abox run [<launcher>] [--location <path>] [-- <argv>...]` | Starts a launcher for the sandbox of a folder. The default launcher is a shell. |
| `abox prune [--full] [--sbx]` | Removes the template images of sbx that stopped being used. `--full` reaches for everything sbx holds, `--sbx` stops the running sandboxes first. |
| `abox secrets list\|set\|remove` | The host secrets the kits of a squad ask for. |

Two more entry points exist, and stay out of the help: `abox pipe <sandbox> -- <command>`
and `abox exec <sandbox> -- <command>`.

## The layout of a project

```
.
├── teams/
│   └── sample/
│       ├── ac.abox            the squad: its kits, its parameters, its state, its secrets
│       ├── cape.abox          the base of the squads of the team
│       └── sample.workload    the workload: the template, the tools, the scripts
├── kits/
│   ├── pi-config/             a kit
│   │   └── pi-config.yaml
│   └── opencode-config/
│       └── opencode-config.yaml
└── cli/
    ├── agentbox.slnx
    ├── src/Abox/              the command line
    └── test/Abox.Tests/       the tests
```

The `.abox` files and the workloads live in the team folders under `teams`, the kits in
the `kits` folder of the project. A kit named in a `.abox` file without a slash or a
colon is a directory in the `kits` folder; a name with a slash or a colon is a
reference sbx resolves.

## Installing the command line

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) first;
it includes the .NET 10 runtime that abox needs. Once the package has been published
to a configured NuGet source, install it globally:

```sh
dotnet tool install --global rwest-agentbox
abox --help
```

The NuGet package is named `rwest-agentbox`; the installed command is **`abox`**.
Make sure the global tools directory is on PATH: `$HOME/.dotnet/tools` on Linux and
macOS, or `%USERPROFILE%\.dotnet\tools` on Windows. The installer reports any PATH
setup that is needed.

To update or remove the tool:

```sh
dotnet tool update --global rwest-agentbox
dotnet tool uninstall --global rwest-agentbox
```

Installing the tool does not install sbx or the project's `teams/` and `kits/`
configuration. Remote create works from any directory without those sources;
build, publish and `create --local` require the configured project directory.

## Building and running the command line

```sh
cd cli
dotnet build
dotnet test
dotnet run --project src/Abox -- build --team sample --root .. --no-verify
```

### Checking the tool package locally

From the repository root, pack and install into a fresh isolated directory without
changing your global tools or publishing anything:

```sh
dotnet pack cli/src/Abox/Abox.csproj --configuration Release --output cli/src/Abox/bin/packages
dotnet tool install rwest-agentbox --source ./cli/src/Abox/bin/packages --tool-path ./cli/src/Abox/bin/tool-test
./cli/src/Abox/bin/tool-test/abox --help
dotnet tool uninstall rwest-agentbox --tool-path ./cli/src/Abox/bin/tool-test
```

`--source` restricts installation to the local package directory. The package and
test installation are under ignored `bin/` output. To check installation-directory
independence, also run the installed command by its absolute path with `--help`
from outside the repository. No sbx daemon is needed for help.

### User settings

Sandbox creation pulls from **`rwest.jfrog.io/mtiae-docker-release`** by default.
Override the repository in abox's user `config.yaml`, alongside the existing defaults:

```yaml
defaultTeam: sample
defaultTag: latest
pullRepository: mtiae-docker-release
```

The file is at `$XDG_CONFIG_HOME/abox/config.yaml` when `XDG_CONFIG_HOME` is an
absolute path; otherwise it is `~/.config/abox/config.yaml` on Linux/macOS or
`%USERPROFILE%\.abox\config\config.yaml` on Windows. `pullRepository` is one
lowercase JFrog repository name, not a URL, host, path or tag. Omitted or blank
values use the default. It is independent of each workload's `publishRepository`.

For example, `abox create --team sample --squad ac --tag latest` pulls
`rwest.jfrog.io/mtiae-docker-release/agentbox-rwest-sample/ac:latest`, validates its
bundled metadata and creates from its immutable platform digest with `--pull always`.
`abox create --team sample --local` uses a prebuilt local workload
image plus its thin v3 runtime kit and local mixins, with `--pull never`. It does
not rerun the workload's apt, mise or script installation. Only `--local` needs
team files and a matching local build manifest. Remote state mounts and MCP
registration come exclusively from the published set's bundled `squad.json`.
Local-only/personal kit customizations require `--local`.

Local build and creation require host Docker with Buildx and a Docker Engine
context's **docker-driver** builder. `build` prints a unique image reference and
its immutable content ID, and records the Docker context. `create --local` checks
that image and the generated thin recipe before replacing any existing sandbox;
old manifests, missing/replaced images, and changed runtime recipes require a new
`abox build`, never a silent workload rebuild. Keep the recorded Docker context
available. `--no-verify` only skips additional kit inspection, not the image build.
The explicit `--metadata-only` preparation mode below is the exception; it does
not produce an image usable by `create --local`.

Local create still runs sbx's host builder for the thin `FROM <prebuilt-image>`
v3 wrapper, loads/reuses layers in sbx's separate image store, and composes mixins
and runtime declarations. This can take time on the first create. It may resolve
the frontend or registry kit dependencies; it is not an offline guarantee.
Custom mixins with their own recipes can still build during creation. The kits
in this repository are declaration-only. Original workload sources remain separate
for the existing fingerprint-checked publication pipeline.

Remote create consumes the **already built and processed published set**, including
its workload, mixins, arguments and v3 declarations. It does not reapply the team's
local kit sources or use the local workload image. User-specific overlays on a
published set are future work, not part of this lifecycle.

### Remote-only workflow

From any directory, with no `teams/`, `kits/`, local build or abox user config:

```sh
abox create --team sample --squad ac --tag latest --location /path/to/project
# From D:\dev\rwe using this checkout's launcher:
.\agentbox\run-abox.ps1 create --team mti-ca --squad ac
```

Remote create needs a healthy, signed-in sbx runtime and host Docker with Buildx
and a running engine. Sign in with `docker login rwest.jfrog.io`, using read access
to the set and its referenced configuration component. It only inspects/pulls
published artifacts: no builds, source preparation, publishing or registry writes.
The small digest-pinned config mixin is read from a temporary Docker-save archive;
no container is executed. Its image remains in Docker's normal cache. The processed
set's layers and security declarations are passed unchanged to sbx. No local
workload/mixin recipes are run or overlaid.

Give `--squad` explicitly: remote squad discovery is not currently supported.
Missing/invalid bundled metadata, identity/schema mismatches, unsafe state paths,
missing host-mount declarations and unavailable MCP registrations fail before
an existing sandbox is removed, even with `--yes`. Older artifacts without the
required metadata must be rebuilt and republished by a maintainer; local metadata
is never substituted. `abox run` does not replace pulled sandboxes with local builds.

The optional `build --metadata-only --no-personal` mode remains for maintainers;
it is not part of this workflow. It performs no Docker/Buildx or sbx calls.

`--metadata-only` **replaces the manifest for the selected squad/tag** and clears
its previous local image selection, even if inputs are unchanged. Existing
Docker images, thin runtime kits and sandboxes are not deleted. Use a separate
tag if you need to keep a local build selected; otherwise a normal `abox build`
without `--metadata-only` is required before the next `create --local`.
`abox run` continues using existing sandboxes rather than offering metadata-only
preparation as a newer local image build.
`--no-verify` remains independent: without `--metadata-only`, it still builds the
real workload image.

### Sandbox prerequisites

The sandbox commands need `sbx` on the PATH: abox calls sbx for every sandbox operation and
never installs anything itself.

Normal image builds, all creation and maintainers publishing kits need Docker
with Buildx; metadata-only maintainer preparation does not.
Local builds use the current Docker context's docker-driver builder, regardless
of a separately selected `docker-container` or remote publishing builder.
Set `publishRepository` in each `.workload` file (the included workloads use
`mtiae-developer-container-images-local`). Sign in on the host with
`docker login rwest.jfrog.io` using a JFrog identity with read/write access to the
selected repository, then run:

```sh
abox build --team sample --no-personal
abox publish --team sample --tag latest
```

Publishing updates the chosen tag (`latest` by default) after remote descriptor
verification. Every squad has a stable reference, for example
`rwest.jfrog.io/mtiae-developer-container-images-local/agentbox-rwest-sample/ac:latest`.
Builds containing personal `.abox` configuration, default local builds without
fingerprints, older builds without provenance, and builds whose local sources
have changed must be rebuilt with `--no-personal` before publication. The build
records the repository of its resolved workload; changing it also requires a rebuild.
Link-free source enforcement and fingerprinting apply only to `--no-personal` builds;
ordinary local builds retain their previous linked-input support.
`publish` needs Docker/Buildx, but does not need sbx running or signed in.
Registry credentials stay in Docker's host credential store; `abox secrets` does not
configure publishing credentials. See [the publishing details](docs/implementation.md#abox-publish---team-name---root-dir---tag-tag---builder-name).

## The formatting and the hooks

The code is formatted with [CSharpier](https://csharpier.com), which comes with the
repository as a local tool:

```sh
dotnet csharpier format cli     # format the code
dotnet csharpier check cli      # check the formatting
```

The hook in `.githooks/pre-commit` checks the formatting before every commit. Enable
the hooks of the repository once:

```sh
git config core.hooksPath .githooks
```
