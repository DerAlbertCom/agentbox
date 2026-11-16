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
| `abox ports [--location <path>] [--offset <n>]` | Discovers .NET and Angular app ports and publishes them on host loopback at port + offset (default 10000). |

Two more entry points exist, and stay out of the help: `abox pipe <sandbox> -- <command>`
and `abox exec <sandbox> -- <command>`.

`create`, `run`, `user-secrets` and `ports` use `--location`, then the user setting `defaultLocation`,
then the current folder. For `create --local`, team discovery starts at that location
and searches its parents, unless `--root <dir>` selects a separate source project.
For `create`, `--root` requires `--local`; remote create rejects it because no local
team sources are used.

### Reaching sandbox apps over HTTP

Start your app inside the existing sandbox with its normal launch profile (including
localhost-only bindings), then run:

```sh
abox ports --location /path/to/project
# app/Properties/launchSettings.json declares http://localhost:8203
# Open http://localhost:18203 on the host after abox exits.
```

The command reads explicit ports beneath the folder from:

- Every profile's semicolon-separated `applicationUrl` values in
  `Properties/launchSettings.json`.
- `angular.json`: every project's `architect.serve` (or `targets.serve`) `options.port`
  and every named `configurations.<name>.port`, whether currently selected or not.
- `package.json`: the exact `scripts.start` entry, for direct `ng serve` / `ng s` / `ng dev`
  commands with one `--port 4200` or `--port=4200`. Literal single/double quoting of
  the value or argument is supported. No scripts or tooling are executed to scan.

Angular ports must be literal integers; no implicit default 4200, environment-variable
expansion, wrapper commands (`npx`, `cross-env`, npm scripts), shell operators or
duplicate `--port` flags are interpreted. A recognized `ng` start script with an
explicit `--port` in an unsupported/dynamic form fails with a fix hint; put a literal
serve port in `angular.json` and remove that dynamic flag instead. Non-`ng` start
commands (including wrappers, Vite, Next and custom Node servers) are ignored even
when they contain `--port`; configure their Angular port in `angular.json` if needed.
Runtime environment overrides such as Angular's `PORT` are not evaluated and can
make the actual listener differ from the static plan. Other scripts/targets are ignored.
All inputs and mapped ranges are validated **before publishing anything**.
It prints each source occurrence, warns about duplicates and publishes each distinct
mapping once. No `abox.json`, team sources or build is needed. It never creates or
replaces a sandbox; a missing sandbox gives a create hint.

sbx publishes **127.0.0.1 only** to a small sandbox-owned TCP relay, because native
publishing cannot reach sandbox loopback on sbx 0.51.0. **Python 3.8+ must already be
available as `python3` inside the sandbox**, running as its non-root application user;
abox checks it and never installs dependencies. No extra host runtime or background
abox process is needed. The relay uses a separate sandbox port, so it does not
reserve or change your application's listener, sources, profiles or startup flags.

Subsequent runs reuse matching relays/mappings and preserve unrelated mappings.
An existing direct native mapping at the requested host port is a conflict **at every
offset, including 10000**: its matching numbers do not prove abox ownership. abox never
automatically removes or replaces it. Inspect the full binding and, only if you intend
to replace it, explicitly remove that exact binding before retrying; see the
[manual recovery examples](docs/application-port-offsets.md#persistent-mapping-and-ownership-contract).
Other targets or non-loopback bindings are also conflicts, not overwritten mappings.
A stopped sandbox is started first and its relays are restored, but **apps are not
started** by this command. Ports occupied by another service fail with a diagnostic;
fix the conflict and retry. Failure/cancellation can leave mappings and sandbox
relays in place; there is no rollback. The table is a plan, not proof the app is running.

This supports .NET/Angular discovery and HTTP reachability with configurable offsets.
HTTPS ports are discovered too, but certificates/trust are **not** configured.
See [HTTPS status, trust boundaries and remaining gates](docs/https-development-certificates.md).
Automatic create/recreate publishing is not included yet.
See [shared discovery/runtime rules](docs/verification/application-ports.md) and
[Angular scope, supported forms and verification](docs/verification/angular-application-ports.md).

#### Folder and one-run port offsets

An optional `abox.json` in the sandbox folder can select a default without changing
`squad` or `userSecrets` behavior:

```json
{ "portOffset": 20000 }
```

For sandbox port 8203, `abox ports` now requests host 28203. `abox ports --offset 30000`
requests 38203 instead. Precedence is **`--offset`, folder `portOffset`, 10000**;
there is no global setting, no file is required, and abox never writes configuration.
Offsets are decimal integers **-65534..65534**; zero keeps the same port number and
negative offsets subtract. Every resulting host port must still be **1..65535**.
Fractions, exponent notation, overflow and out-of-range mappings fail before publishing.

**An override does not undo persistent forwarding on exit.** Repeating the same
offset is safe. Changing it while old abox relay bindings exist fails without
removing them or adding new bindings. First explicitly remove only the exact old
bindings, then rerun with the new offset; never clear relay state or unrelated ports.
After a failure or cancellation, retry with the **same offset** to complete missing
bindings. See [offset-change and recovery examples](docs/application-port-offsets.md).

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

- `defaultLocation` is unset by default: `create`, `run`, `user-secrets` and `ports` use the current folder unless `--location` is given.
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

### Reusing cape-agent-sandbox environments

`run`, `user-secrets`, `ports` and create/replacement checks find the sandbox for the
effective folder (`--location`, then `defaultLocation`, then the current folder).
They first check the current `<folder>-abox-<hash>` name. Only when it is absent
do they check the exact historical `<folder>-ai-sbx-<hash>` generated name.
Runtime/discovery errors stop the command; they are not treated as absence.

A legacy sandbox is reused **in place**, including when sbx exec needs to start
it. Probes, launchers and secret copying use its actual name. If both names exist,
the agentbox name wins and the legacy sandbox is left alone. There is no automatic
rename, recreation, cleanup, adoption record or persistent-state/Herdr migration.
Arbitrary custom names and workspace-metadata matches are not discovered.

`run` and `user-secrets` still never create a missing sandbox. Normal `create`
uses current naming when neither candidate exists. Replacing an existing legacy
sandbox still requires the usual confirmation (or explicit `--yes`), lists the
same losses, and retains the selected name rather than converting it to agentbox
naming. An accepted newer-build replacement from `run` does the same.

On-demand `user-secrets` does **not verify the sandbox's squad** or require an
agentbox record, even when `abox.json` declares a squad. It copies into the resolved
existing sandbox without creating or changing any record. Create/run replacement
squad checks remain unchanged. Review the [user-secret safety contract](#copying-net-user-secrets)
before copying. Historical path spelling and link rules determine the legacy hash;
this is not a general alias search across all spellings of a folder.

### Copying .NET user secrets

Declare only the intended stores/keys and optionally the creation squad in **the
sandbox folder's `abox.json`** (not a squad `.abox` file or the source project
selected by `--root`):

```json
{
  "squad": "ac",
  "userSecrets": [
    "5b3e0f6a-1c2d-4e5f-a6b7-c8d9e0f1a2b3",
    "Cape.*",
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
the **whole store**. An ID ending in one `*`, such as `Cape.*`, selects every local
store whose ID starts with the literal prefix `Cape.` (ordinal, case-insensitive).
Only a safe nonempty literal prefix plus one terminal `*` is supported: no bare
`*`, paths, recursive lookup or other glob syntax. A prefix with no matches selects
no stores; an exact ID still selects a store when no host directory exists.
Copy-only stores require a readable `secrets.json`; missing files may instead use
matching project appsettings sources as described below. An incomplete matched
directory is not an unmatched prefix. Unreadable/invalid present files fail, even
with `--force`. Matches use actual host ID spelling and
are sorted; overlapping exact/prefix selections copy each store once, unioning
their key selections (a whole-store selection wins). Repeated identical selection
strings are still rejected case-insensitively. Matching unsafe host directory names
or case-colliding IDs fail before any copy, rather than being silently skipped.

A `keys` list selects effective .NET keys, such as
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
existing command selection applies. Accepted `run` replacement requires a known
matching sandbox record when a folder squad is present; it never silently switches
an existing sandbox's squad. On-demand `user-secrets` ignores squad identity when
selecting the existing sandbox: missing, matching or mismatched records do not
block copying. Folder configuration is still validated. Ordinary `run` (including
declining replacement) can still open the existing sandbox with a different or
unknown recorded squad: it copies no secrets and changes no squad.

#### Automatic copying and policy

Local/published creation, explicit replacement and an accepted newer-local-build
replacement from `run` use the **same copy flow** as `abox user-secrets`. Folder
configuration, project appsettings and all selected host originals are validated **before removal** of
an existing sandbox. After successful creation, abox saves the truthful new sandbox
record, then copies secrets **before reporting full success or launching**.
Target-only discovery/transfer failures can therefore occur after creation.

A folder without `abox.json` retains its existing create/run behavior, without
user-secret store reads, target discovery or writes. Ordinary `run`, including
declining a newer-build replacement, does **not** resync secrets. Failed creation,
declined replacement and cancellation do not start a subsequent copy. Missing or
invalid `abox.json` is an on-demand error. See the
[lifecycle and recovery details](docs/user-secrets-lifecycle.md).

All selected host originals are snapshotted in memory, parsed and checked against the
global deny list **before rewriting or any destination write**. Only selected
scalar values are checked: strings after JSON unescaping, plus number/boolean text;
null/empty containers have no text. A blocked unmatched value does not prevent a
safe filtered copy. Appsettings originals that would generate overrides receive
the same deny check during all-store target preparation, before any destination
write. A block names the store/key/project, never the value.

`--force` is **on-demand only** and bypasses only matched deny-list policy, not
invalid configuration/JSON, missing files, policy metadata/destination
checks, cancellation or transport errors. A warning is reported
after a successful copy **only if selected original values actually matched**;
passing `--force` with no match is not an override.
Automatic copying always enforces the list: `--yes` and replacement consent are
not force. If validation blocks creation, correct the values/selection. For an
intentional override, explicitly deselect the blocked stores, create, restore the
selection, then run `abox user-secrets --force`; restoring selection alone does
not trigger a copy.

#### Appsettings-derived sandbox overrides

Selected stores can also receive sandbox-only endpoint overrides from associated
projects' appsettings, **without editing project files or requiring a host secrets
file**. Discovery searches only beneath the selected `abox.json` workspace and
associates literal `UserSecretsId` declarations without running/evaluating MSBuild.
Only explicit case-insensitive matches to selected IDs participate. Unrelated,
computed or import-only IDs do not warn or block generation; their appsettings
are not read. Explicit matching but conditional, multiple or malformed declarations
fail safely; see [the association rules](docs/user-secrets-appsettings.md#bounded-discovery-not-application-startup).
It excludes `.git`, `bin`, `obj`, nCrunch artifacts, escaping directory links and
cycles. Discovery never selects additional stores; store prefixes still expand
only existing host directories.

Project-local `appsettings.json` is combined with lowercase
`appsettings.development.json` (development wins, keys are case-insensitive).
Selected host keys always win, but still receive eligible endpoint adaptation.
Existing exact/wildcard/empty key filters apply to copied **and generated** keys.
Only eligible URL/PostgreSQL/Oracle values needing a host change are generated, not the
whole appsettings file. A store with no matching project remains copy-only.
The explicit `Database_Config:Host` + `Database_Config:Port` pair is also supported:
resolve both keys first and emit only the necessary host override. Unselected
appsettings port metadata can establish eligibility; filtered-out secrets cannot.
See [recognized split settings](docs/user-secrets-split-settings.md).

Each create/replacement/explicit copy rebuilds the complete destination, removing
stale generated entries. Ordinary `run` still performs no copy. Projects sharing a
store merge compatible generated overrides. A generated key must agree with every
explicitly associated consumer's required effective value, including remote/unmapped values
that need no rewrite; conflicts stop all selected stores before any write, with
value-free store/key/project diagnostics. Supply an authoritative selected host
key, exclude the key, or use separate stores to resolve a conflict. Unreadable XML
and unresolved IDs establish no association and do not block generation. Split hosts depending on project port metadata
also require compatible consumer contexts; an authoritative selected host/port pair
retains copy-only behavior.
This bounded discovery does not simulate
custom providers, environment/CLI overrides or service readiness. See
[precedence, discovery, refresh and safety details](docs/user-secrets-appsettings.md).

#### Paths, transfer and endpoint rewriting

The host store is `<APPDATA>/Microsoft/UserSecrets/<id>/secrets.json` when
`APPDATA` is set, **including on Unix**, otherwise
`<HOME>/.microsoft/usersecrets/<id>/secrets.json`. Empty/relative environment roots
fail; abox does not guess another user's profile. Prefixes enumerate only direct
store directories in that root, not project files or subdirectories. Only selected
files are read; host files are never changed. The host user-secrets root itself,
selected store directories and secret files must not be symbolic links/reparse
points. Linked ancestors above that trusted root (for example a linked user profile)
are allowed. The sandbox destination is independently resolved from
the effective, non-root **default exec application user** and its `APPDATA`/`HOME`,
using the same precedence. An application launched with a different environment
must use matching roots. No host paths, local kits or published-source checkout
are needed inside the sandbox. A stopped sandbox may be started by sbx exec;
a missing sandbox is never created/replaced by this command.

Copying is runtime-only: payloads and integrity frames travel over UTF-8/no-BOM
stdin, never process arguments, images, build inputs or published artifacts. This
does not change the separate proxy/host-secret command `abox secrets`.

**Endpoint rewriting (#8, implemented):** abox discovers
exact TCP `localhost:<port>` declarations from the sandbox's current runtime
network policy, whether provisioned at creation or explicitly added later. It
checks each port's **effective** authorization. No local kit files, build records
or artifact lookup are involved. Only supported URI
authorities and database connection-string endpoint fields change loopback to
`host.docker.internal`, keeping the **same published host port**:

```text
Server=localhost,8004      -> Server=host.docker.internal,8004
http://127.0.0.1:8006/path -> http://host.docker.internal:8006/path
```

Named hosts (including `host.docker.internal`) retain their names/ports for
TLS/SNI. URL/PostgreSQL/Oracle rewriting includes IPv4/IPv6 loopback. Oracle Easy
Connect and structural descriptors work standalone or inside `Data Source`
connection-string values, preserving credentials, service/SID identifiers and
options; only eligible hosts within compound destinations change. Undeclared ports and
unrelated credentials, fields, paths and queries remain unchanged. Recognized
mapped-local endpoints with unsafe/ambiguous syntax fail before any destination
write, rather than silently retaining known broken configuration. Arbitrary text
and unmapped localhost references are not errors. A service published as `8004:5432`
does **not** imply a `5432 -> 8004` rewrite. Wildcard/global/all-port authorization
is not rewrite intent; a valid empty inventory means no rewrites. Explicit manual
declarations must match the supported persistent sandbox-scoped, TCP-only schema;
unknown lifetime/constraint fields are not inferred. Unavailable/malformed
required metadata fails before writes, not by guessing. There is no host kit-source,
`/etc/hosts`, experimental `host-port@1` or `sbx ports` lookup and no policy change.
Authorization is not proof of service availability or protection against later
policy changes. The accepted live evidence is **local/nonempty + published/empty
kit policy**; published/nonempty rewriting remains unverified. See the
[supported forms and rewrite verification](docs/user-secrets-rewriting.md) and
[verification record](docs/verification/user-secrets/README.md).
An explicit `sbx policy allow network --sandbox <sandbox> --protocol tcp localhost:1521`
can declare a missing host port without kit files or recreation. This is a user
policy change, never an automatic abox grant; see the
[scoped-policy contract and QA evidence](docs/verification/user-secrets-scoped-policy.md).

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

`--metadata-only` **replaces the manifest for the selected team/squad/tag** and clears
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

See the [coding standards](CODING_STANDARDS.md) for architecture conventions and
[formatting and hook setup](CODING_STANDARDS.md#formatting-and-hooks).
