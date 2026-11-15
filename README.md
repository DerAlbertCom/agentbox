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
| `abox build --team <name> [--root <dir>] [--tag <tag>] [--no-verify] [--no-personal]` | Prebuilds reusable local workload images with Docker Buildx and prepares the v3 kits of every squad. No registry writes. |
| `abox build --team <name> --metadata-only` | Maintainer-only source preparation without image builds. Clears local image selection for this tag; not required for remote create. |
| `abox publish --team <name> [--root <dir>] [--tag <tag>] [--builder <name>]` | Builds and pushes the local kit sets to `rwest.jfrog.io/<publishRepository>`, configured in each workload. Requires Docker Buildx and host registry sign-in. |
| `abox create --team <name> [--squad <name>] [--location <path>] [--local] [--root <dir>]` | Pulls a self-contained published squad set, or uses a local prebuild with `--local`. The folder's `abox.json` can select the squad and opt into user-secret copying. Only local create can ask for an otherwise omitted squad; `--root` locates its team sources separately from the sandbox location. |
| `abox run [<launcher>] [--location <path>] [-- <argv>...]` | Starts a launcher for the sandbox of a folder. The default launcher is a shell. |
| `abox prune [--full] [--sbx]` | Removes the template images of sbx that stopped being used. `--full` reaches for everything sbx holds, `--sbx` stops the running sandboxes first. |
| `abox secrets list\|set\|remove` | The host secrets the kits of a squad ask for. |
| `abox user-secrets [--location <path>] [--force]` | Copies explicitly selected local .NET user-secret stores into the folder's existing sandbox. |

Two more entry points exist, and stay out of the help: `abox pipe <sandbox> -- <command>`
and `abox exec <sandbox> -- <command>`.

`create`, `run` and `user-secrets` use `--location`, then the user setting `defaultLocation`,
then the current folder. For `create --local`, team discovery starts at that location
and searches its parents, unless `--root <dir>` selects a separate source project.
For `create`, `--root` requires `--local`; remote create rejects it because no local
team sources are used.

### Seeing tool output

Add the global `--verbose` flag to see the complete captured stdout and stderr of
external tools such as sbx and Docker, on both success and failure:

```powershell
./run-abox.ps1 build --team mti-ca --verbose
# Installed tool:
abox --verbose build --team mti-ca
```

The flag works before or after a command (including `secrets` subcommands), but
must precede `--` when passing arguments to a launcher or sandbox command. Output
is shown after each tool exits, grouped by stdout and stderr with its exit code,
and goes to **abox's stderr** so it does not contaminate stdout. There is no
truncation; default output remains concise. Interactive tools already inherit the
terminal and are not captured or printed twice. Diagnostics omit arguments,
environment settings and stdin, and redact known credential values if a tool
echoes them. Tool output can still contain other sensitive data; review it before
sharing.

`user-secrets` transfers are an exception: their child stdout, stderr and raw
errors are discarded, **even with `--verbose`**, including scalar fragments.
Only store IDs, key names/counts and safe recovery messages are reported.

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
Use `create --local --root <project>` when the sandbox location is outside that project.

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
# defaultLocation: /path/to/project
teamsDirectory: teams
kitsDirectory: kits
defaultTag: latest
pullRepository: mtiae-docker-release
userSecretsDenyList:
  - staging.example.internal
  - production.example.internal
```

- `defaultLocation` is unset by default: `create`, `run` and `user-secrets` use the current folder unless `--location` is given.
- `teamsDirectory` names the project's teams directory; the default is `teams`.
- `kitsDirectory` names the kits directory relative to the project root; the default is `kits`.
- `userSecretsDenyList` is a list of nonempty **literal substrings**, matched
  case-insensitively against selected original values before rewriting or writes.
  It defaults to `[]`: there is no built-in staging/production detection. Add your
  environment markers here; do not put secret values in configuration. This is a
  policy safeguard, not a detector for arbitrary production credentials.

The file is at `$XDG_CONFIG_HOME/abox/config.yaml` when `XDG_CONFIG_HOME` is an
absolute path; otherwise it is `~/.config/abox/config.yaml` on Linux/macOS or
`%USERPROFILE%\.abox\config\config.yaml` on Windows. `pullRepository` is one
lowercase JFrog repository name, not a URL, host, path or tag. Omitted or blank
values use the default. It is independent of each workload's `publishRepository`.

### Copying .NET user secrets

Declare the squad and only the intended stores/keys in **the sandbox folder's
`abox.json`** (not a squad `.abox` file or the source project selected by `--root`):

```json
{
  "squad": "ac",
  "userSecrets": [
    "5b3e0f6a-1c2d-4e5f-a6b7-c8d9e0f1a2b3",
    { "id": "my-dev-store", "keys": ["ConnectionStrings:*", "Feature:Enabled"] }
  ]
}
```

**Trust boundary:** the workspace, including `abox.json`, is writable by the
sandbox agent. Changes to this file control which host stores/keys the **next**
create, replacement or on-demand copy reads. Review and trust its current squad,
IDs and patterns before those operations, including after agent edits. There is
no separate host-side approval/authorization mechanism. The global deny list does
not make an untrusted folder safe or guarantee that production secrets cannot enter.

```sh
# Initial creation also copies the configured secrets:
abox create --team sample --location /path/to/project
# Resync an existing local or published sandbox without recreating it:
abox user-secrets --location /path/to/project
# Explicit safety override, only when you have checked the blocked key:
abox user-secrets --location /path/to/project --force
```

#### Selection and squad identity

Plain IDs and objects can be mixed. A plain ID or `{"id":"my-dev-store"}` selects
the **whole store**. A `keys` list selects effective .NET keys, such as
`ConnectionStrings:Db` for a nested JSON property:

- Matching is full-key, ordinal and case-insensitive. An exact pattern selects
  only that key; `ConnectionStrings` does not select its descendants.
- `*` matches zero or more characters, **including `:`**, anywhere in a pattern.
  `ConnectionStrings:*` includes deeper descendants. Other characters are literal,
  not regex/glob operators. Overlapping patterns select each key once.
- `"keys": []` or patterns with no matches select zero keys and **replace that
  store with `{}`**. They never broaden into a whole-store copy or leave old keys.
- An absent/empty `userSecrets` array selects no stores and writes nothing.
  Filtered output uses flat effective keys, preserving array indices and selected
  values; unfiltered output retains the JSON structure.

IDs must be 1–128 ASCII letters/digits/dots/underscores/hyphens, beginning with a
letter/digit, without trailing dots or Windows device names. Duplicate IDs and
duplicate effective .NET keys are rejected case-insensitively, including
nested/colon-key and array-index collisions, **before filtering**. Invalid patterns
fail, not widen selection. Folder JSON is strict; store JSON must be a UTF-8 object
and accepts .NET-style comments/trailing commas. See the
[full filtering contract](docs/user-secrets-filtering.md) for pattern validation,
scalar types and empty-container handling.

The optional `squad` selects local or published creation when `--squad` is omitted.
An explicit option must match case-insensitively; conflicts fail, not override the
folder. The team still comes from `--team` or `defaultTeam`. With no folder squad,
existing command selection applies. `run` and on-demand `user-secrets` require a
known matching sandbox record when a folder squad is present; they never silently
switch an existing sandbox's squad.

#### Automatic copying and policy

Local/published creation, explicit replacement and an accepted newer-local-build
replacement from `run` use the **same copy flow** as `abox user-secrets`. Folder
configuration and all selected host originals are validated **before removal** of
an existing sandbox. After successful creation, abox saves the truthful new sandbox
record, then copies secrets **before reporting full success or launching**.
Target-only discovery/transfer failures can therefore occur after creation.

A folder without `abox.json` retains its existing create/run behavior, without
user-secret store reads, target discovery or writes. Ordinary `run`, including
declining a newer-build replacement, does **not** resync secrets. Failed creation,
declined replacement and cancellation do not start a subsequent copy. Missing or
invalid `abox.json` is an on-demand error. See the
[lifecycle and recovery details](docs/user-secrets-lifecycle.md).

All selected originals are snapshotted in memory, parsed and checked against the
global deny list **before rewriting or any destination write**. Only selected
scalar values are checked: strings after JSON unescaping, plus number/boolean text;
null/empty containers have no text. A blocked unmatched value does not prevent a
safe filtered copy. A block names the store/key, never the value.

`--force` is **on-demand only** and bypasses only matched deny-list policy, not
invalid configuration/JSON, missing files, squad constraints, metadata/destination
checks, cancellation or transport errors. A warning is reported
after a successful copy **only if selected original values actually matched**;
passing `--force` with no match is not an override.
Automatic copying always enforces the list: `--yes` and replacement consent are
not force. If validation blocks creation, correct the values/selection. For an
intentional override, explicitly deselect the blocked stores, create, restore the
selection, then run `abox user-secrets --force`; restoring selection alone does
not trigger a copy.

#### Paths, transfer and endpoint rewriting

The host store is `<APPDATA>/Microsoft/UserSecrets/<id>/secrets.json` when
`APPDATA` is set, **including on Unix**, otherwise
`<HOME>/.microsoft/usersecrets/<id>/secrets.json`. Empty/relative environment roots
fail; abox does not guess another user's profile. Only named files are read; host
files are never changed. The sandbox destination is independently resolved from
the effective, non-root **default exec application user** and its `APPDATA`/`HOME`,
using the same precedence. An application launched with a different environment
must use matching roots. No host paths, local kits or published-source checkout
are needed inside the sandbox. A stopped sandbox may be started by sbx exec;
a missing sandbox is never created/replaced by this command.

Copying is runtime-only: payloads and integrity frames travel over UTF-8/no-BOM
stdin, never process arguments, images, build inputs or published artifacts. This
does not change the separate proxy/host-secret command `abox secrets`.

**Endpoint rewriting (#8, pending integration):** the approved contract discovers
exact kit-declared TCP `localhost:<port>` resources using supported sandbox policy
metadata and checks each port's **effective** authorization. Only supported URI
authorities and database connection-string endpoint fields change loopback to
`host.docker.internal`, keeping the **same published host port**:

```text
Server=localhost,8004      -> Server=host.docker.internal,8004
http://127.0.0.1:8006/path -> http://host.docker.internal:8006/path
```

Named hosts (including `host.docker.internal`) retain their names/ports for
TLS/SNI. Undeclared ports, unsupported/ambiguous forms and unrelated credentials,
fields, paths and queries remain unchanged. A service published as `8004:5432`
does **not** imply a `5432 -> 8004` rewrite. Wildcard/global/manual rules are not
kit intent; a valid empty inventory means no rewrites. Unavailable/malformed
required metadata fails before writes, not by guessing. There is no host kit-source,
`/etc/hosts`, experimental `host-port@1` or `sbx ports` lookup and no policy change.
Authorization is not proof of service availability or protection against later
policy changes. The accepted live evidence is **local/nonempty + published/empty
kit policy**; published/nonempty rewriting remains unverified. See the
[verification record](docs/verification/user-secrets/README.md).

Transfer serializes selected JSON data, not raw source text: destination JSON
contains no skipped comments or trailing commas. JSON formatting/escape spelling
is not preserved; unchanged string contents stay unchanged. Destination checks
close an empty redirected stdin and cannot forward host stdin.

Rerunning replaces the **entire** destination store, removing old keys. The fixed
receiver checks the byte count and SHA-256 frame before a same-directory atomic
rename; short/corrupt/interrupted input cannot commit a truncated file. Secret
root/store directories are `0700`, files `0600`; symlinks in destination paths,
nonregular files, multiply-linked files and wrong ownership are rejected. The
sandbox needs `sh` and GNU-compatible coreutils (`stat`, `mktemp`, `sha256sum`,
`mv -T`, etc.). No TTY or root exec is used.

#### Recovery and limitations

Validation is all-store, but transfer is atomic **per file**, not a
cross-store transaction. A later transport failure/cancellation can leave earlier
stores replaced; even a completed rename can be unconfirmed if its acknowledgement
is lost. No rollback is claimed. Fix the reported problem and rerun
`abox user-secrets` for the same folder (and explicitly repeat `--force` if needed).
Errors exit 1, cancellation exits 130. Cancellation before a complete verified
frame preserves the previous file; a full frame already committed may remain.
A forcibly killed receiver may leave a private `.secrets.*` temporary file; it is
not read by .NET. Sandbox removal/replacement removes these runtime files too.
Atomic replacement is not crash-durable storage or protection from malicious
concurrent filesystem changes by the application user/root; sandbox contents and
memory remain accessible to that user, not encrypted.

If creation succeeded but required copying failed, the sandbox and its new record
remain; the command fails without a full-success/open message, and `run` does not
launch. Fix the problem and retry the folder-qualified on-demand command without
recreating. Live copy verification used Windows sbx v0.51.0; automated Unix paths
and representative Unix transfer are not independent Unix-host sbx verification.

### Creating local and published sandboxes

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

Before registry preparation, remote create automatically adds the configured
`rwest.jfrog.io/<pullRepository>/` prefix to sbx's `kit.allowedSources` when it is
not already covered. This is a persistent, **repository-scoped trust addition**;
existing entries (including an existing `*`) are preserved, and no setting is
written when the repository is already allowed. It never adds a wildcard or trusts
the whole JFrog host. `--local` does not read or change source trust. Invalid settings,
administrator restrictions or an environment-controlled allowlist that excludes
the repository stop creation before registry preparation or sandbox replacement.
Signature requirements and other sbx security policies remain unchanged.
Adding to the default list creates an override, freezing those copied defaults
across future sbx upgrades. `sbx settings unset kit.allowedSources` restores the
current defaults and discards the entire list override, including the added
repository trust; a later remote create may add its configured repository again.

Give `--squad` explicitly or set `squad` in the folder's `abox.json`: remote squad
discovery is not currently supported.
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
