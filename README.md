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
| `abox build --team <name>` | Builds the kit set of a team: its workload and the kits of every squad the team builds on. The result is kept locally. |
| `abox publish --team <name> [--tag <tag>] [--builder <name>]` | Builds and pushes the local kit sets to `rwest.jfrog.io/<publishRepository>`, configured in each workload. Requires Docker Buildx and host registry sign-in. |
| `abox create --team <name> [--squad <name>] [--location <path>] [--local]` | Pulls the published squad kit set to create a sandbox, or uses a local build with `--local`. Without `--squad`, the command asks which one. |
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
configuration. Run project commands from a configured project directory, or use
the command's `--root` or `--location` option where available.

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
`rwest.jfrog.io/mtiae-docker-release/agentbox-rwest-sample/ac:latest` with
`--pull always`. `abox create --team sample --local` uses local kits instead and
never pulls. Creation still needs the team files and a matching local build
manifest for state mounts and MCP registration. Keep that metadata aligned with
the published tag; reading the published set's bundled configuration is not yet
implemented. Local-only/personal kit customizations require `--local`.

### Sandbox prerequisites

The sandbox commands need `sbx` on the PATH: abox calls sbx for every sandbox operation and
never installs anything itself.

Maintainers publishing kits also need Docker with Buildx and a BuildKit builder.
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
