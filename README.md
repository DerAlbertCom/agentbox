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
| `abox create --team <name> [--squad <name>] [--location <path>]` | Creates the sandbox of a folder from the local build of a squad. Without `--squad`, the command asks which one. |
| `abox run [<launcher>] [--location <path>] [-- <argv>...]` | Starts a launcher for the sandbox of a folder. The default launcher is a shell. |
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

## Building and running the command line

```sh
cd cli
dotnet build
dotnet test
dotnet run --project src/Abox -- build --team sample --root .. --no-verify
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
