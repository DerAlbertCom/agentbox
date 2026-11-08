# abox

`abox` gives every folder of your machine its own AI agent sandbox, built on
[Docker Sandboxes](https://docs.docker.com/ai/sandboxes/).

The idea, the roles and the decisions behind the commands are in
[docs/concept.md](docs/concept.md). This file describes what is built today.

## The commands

| Command | What it does |
|---|---|
| `abox build --squad <name>` | Builds the kit set of a squad: its workload, its kits and the mixin that keeps the state. The result is kept locally. |
| `abox create --squad <name> [--location <path>]` | Creates the sandbox of a folder from the local build of a squad. |
| `abox run [<launcher>] [--location <path>] [-- <argv>...]` | Starts a launcher for the sandbox of a folder. The default launcher is a shell. |

Two more entry points exist, and stay out of the help: `abox pipe <sandbox> -- <command>`
and `abox exec <sandbox> -- <command>`.

## The layout of a project

```
.
├── ac.abox                    the squad: its kits, its parameters, its state
├── cape.abox                  the base of the squad: the workload
├── kits/
│   ├── mti-ca-dotnet.workload the workload: the template, the tools, the scripts
│   ├── enable-fzf.sh          a script of the workload
│   ├── pi-config/             a kit
│   │   └── pi-config.yaml
│   └── opencode-config/
│       └── opencode-config.yaml
└── cli/
    ├── agentbox.slnx
    ├── src/Abox/              the command line
    └── test/Abox.Tests/       the tests
```

The `.abox` files live in the root of the project, the kits and the workloads in the
`kits` folder. A kit named in a `.abox` file without a slash or a colon is a directory
in the `kits` folder; a name with a slash or a colon is a reference sbx resolves.

## Building and running the command line

```sh
cd cli
dotnet build
dotnet test
dotnet run --project src/Abox -- build --squad ac --root .. --no-verify
```

The command line needs `sbx` on the PATH: it calls sbx for every sandbox operation and
never installs anything itself.

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

## What is not built yet

- `abox publish`, `abox prune` and `abox secrets`.
- The kit set is not published to a registry: `abox build` keeps it on the machine and
  `abox create` uses that build. `--local-image` says so explicitly; `--tag` chooses
  the tag of the local build.
- The `orca` launcher. `shell` and `herdr-app` are the launchers of this version.
- The `hc` helper in the image and the `openssh-server` of the base image.
