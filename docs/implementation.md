# What is implemented

This file says how the commands of [concept.md](concept.md) are built today: the shape
of the code, the decisions that were taken where the concept left room, and what is not
built yet. It is the file to read first when you pick this work up.

## The commands

All commands accept the global `--verbose` option before or after the command name
(and before the `--` that separates child arguments). The shared `ProcessRunner`
reports complete captured stdout and stderr after each external tool exits,
including successful calls, to abox's stderr. It labels the streams and exit code
without changing captured results, JSON parsing, or exit status. It never prints
raw arguments, environment settings or stdin, and redacts known credential values
echoed in captured diagnostics. Attached-console processes keep their existing
interactive output without duplicate diagnostics. See [the usage example](../README.md#seeing-tool-output).

An opt-in `ProcessOutputPolicy.Sensitive` instead discards child output (including
raw errors and scalar fragments), prohibits attached streams and writes explicit
UTF-8/no-BOM stdin. It is used by user-secrets transfers even with `--verbose`;
ordinary process diagnostics and `abox secrets` keep their existing behavior.

### `abox build --team <name> [--root <dir>] [--tag <tag>] [--no-verify] [--no-personal] [--metadata-only]`

Normal builds run the following full image-building path. The explicit
`--metadata-only` exception is described after the steps; `--no-verify` alone
does not skip the workload image build.

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
   (the recipe) in the build directory, with the scripts of the workload and the
   bundled CA certificates copied into it.
6. Writes the state mixin: one `host-mount@1` per state directory.
7. Optionally checks additional local kit declarations with `sbx kit inspect`,
   passing their resolved arguments, unless `--no-verify` is given. Inspection
   **does not build images**. sbx 0.51.0 cannot inspect recipe-based local kits;
   those require `--no-verify` and are processed by sbx at creation.
8. Builds the generated workload through the v3 frontend with host
   `docker buildx build --file workload.yaml --load --provenance=false`, using the
   current Docker context's docker-driver builder. Workload apt, mise, scripts
   and certificate setup run here, not at every sandbox create. BuildKit reuses
   unchanged instruction/layer caches. No images are pushed. A unique local tag
   `docker.io/library/abox-workload:<id>` and its actual immutable Docker image
   content ID are reported; source changes during building fail the build.
9. Writes a separate versioned thin runtime kit: the same v3 workload descriptor
   and a companion Dockerfile containing only `FROM <unique-local-image>`.
   The original generated sources remain unchanged for publication. The versioned
   runtime directory keeps an unsuccessful rebuild from overwriting the previous
   build's runtime kit.
10. Writes the manifest of the build: `build.json` in the build directory. It records
   whether personal configuration was included and the resolved workload's
   `publishRepository`. Only `--no-personal` builds capture SHA-256 fingerprints of
   configuration files (including the `.workload`) and local source trees (including
   generated workload/state kits), checking them before and after verification and
   image building. `LocalWorkload` records the unique tag, image content ID,
   Docker context, runtime kit directory and its fingerprint on image builds.

The build lives in `<state dir>/builds/<team>/<squad>/<tag>/`, with lowercase team
and squad path segments. The tag is `latest` unless the command or the user
configuration says otherwise. Teams sharing a squad/tag keep separate generated
sources, manifests, state mixins and local image selections, including in one
multi-team build. Separate source projects deliberately using the same team identity
still share that team's local storage; project identity is not a storage dimension.

Build-store reads check the manifest's squad, tag and explicit team ownership marker
(`Reference`). If no qualified manifest exists, an old
`<state dir>/builds/<squad>/<tag>/build.json` is usable only with matching, unambiguous
ownership. Missing/conflicting ownership, duplicate JSON properties or unreadable
metadata require rebuilding for the requested team; no directory/source-path
inference substitutes for provenance. A present but invalid qualified manifest
never falls back to an older one. Builds write only the new layout: old sources,
manifests, images and runtime directories are not moved, deleted or relabelled.
See [team-isolation verification](verification/team-qualified-builds.md) for the
synthetic coverage and legacy compatibility checks.

Normal and metadata-only builds, local/remote creation and reusable build-store
access use the same registry-tag grammar: 1–128 ASCII letters, digits, underscores,
periods or hyphens, starting with a letter, digit or underscore. The selected
`--tag` or configured `defaultTag` is rejected before tag-derived filesystem access,
generated script/certificate cleanup or external-tool/runtime operations. Paths,
separators, whitespace and empty/overlong tags are invalid. An omitted `defaultTag`
still means `latest`; an explicitly blank default is invalid when selected, not a
request to fall back to `latest`. Valid explicit tags can override invalid defaults.
Published reference naming and tag promotion are unchanged.

**Metadata-only preparation:** `--metadata-only` resolves configurations, writes
generated workload/state sources and saves the manifest, but skips sbx preflight,
all kit inspection and all Docker/Buildx/image operations. `LocalWorkload` is
always null. No sbx or Docker installation/daemon/authentication is required for
this maintainer preparation. Remote `create` does not use it: it reads published
metadata with Docker/Buildx and creates the sandbox with sbx.
The command reports that no image was built/selected and prints the remote create
command. `--no-personal` still excludes personal declarations and captures source
fingerprints; all existing publication provenance checks remain in force.

This mode explicitly replaces the selected team/squad/tag manifest and **invalidates
any previous local image selection, even with identical inputs**. It does not
delete existing images, runtime kit directories or sandboxes, and does not retain
an image whose validity could require Docker checks. To retain another local
manifest, use a distinct tag. To use `create --local` after metadata-only
preparation, run a normal `abox build` without `--metadata-only`; local create
fails with that instruction instead of rebuilding implicitly. `--no-verify` is
independent and still builds the workload image on a normal build.
Existing local sandboxes remain runnable; `abox run` does not offer a metadata-only
manifest as a newer local image build.

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

### Shared existing-sandbox resolution (#10)

`Sandboxes/SandboxResolver.FindAsync(location, token)` is the single location-to-existing-
sandbox rule for run, on-demand user secrets and create/replacement checks. It returns
the actual `SandboxInfo` or null: current `SandboxName.For` first, then the exact
cape-agent-sandbox generated name only after a genuine null/not-found. Discovery
exceptions and cancellation propagate, never enabling fallback or creation.
Current naming and effective-location precedence are unchanged. No fuzzy/prefix or
workspace-metadata matching is used; both-present inventories select the current
candidate without touching the legacy one.

`Naming/LegacySandboxName` keeps sanitization and hashing pure; `SandboxResolver`
privately resolves the historical real path before calling it. The contract is
verified against
cape-agent-sandbox `1512ff71c31c6bfa7e923a43683c6d80624c40d0`:

- Sanitize the original leaf before case conversion: non-ASCII-letter/digit/underscore/
  hyphen characters become hyphens; lowercase invariantly, trim surrounding hyphens,
  and use `root` when empty. Append `-ai-sbx-` and three SHA-256 bytes of the UTF-8
  historical real path as six lowercase hex characters.
- Windows uses `Path.GetFullPath` and a final directory link target when available,
  falling back to the full path on missing/ordinary/inaccessible link resolution.
  Preserve case, native separators and retained trailing separators in the hash.
- Non-Windows uses the historical `bash -c 'cd "$1" 2>/dev/null && pwd -P' bash <path>`;
  take its last output line on success, otherwise the original effective location.
  Never hash the Windows-lowercased/slash-normalized current naming input instead.

Callers carry the selected actual name into local-record reads, probes, launcher
context, policy discovery, transfer and removal. Accepted run replacement passes
its already-selected `SandboxInfo` in `SandboxCreationRequest.ExistingSandbox` so
a second candidate appearing after consent cannot redirect destructive work.
Create's read-only MCP validation, host-secret preparation, confirmation, cancellation
checks, state preparation and removal safeguards remain in place. Explicitly
authorized replacement creates and records the replacement under that same name;
ordinary compatibility lookup writes no record or state/alias mapping and migrates
neither legacy state layout nor Herdr registrations.

On-demand user-secret copying does not verify squad identity or read/require a local
sandbox record, even when `abox.json` declares `squad`. Missing, matching, unknown or
mismatched records do not block copying. Host input and deny-list validation precede
runtime access. Discovery writes no provenance; create/run replacement squad checks,
destination/ownership, effective policy authorization, value-free reporting and
sensitive transport checks are unchanged. This supersedes the earlier on-demand
record prerequisite in #2/#10, per the user's subsequent policy correction.

Explicit-name `exec`/`pipe`, inventory-wide prune and host proxy-secret commands do
not use location fallback. Location-based ports (#43) also uses this resolver and
retains the actual selected identity. Public stop/delete commands are not included.
See [verification and red evidence](verification/legacy-sandbox-compatibility.md).

### `abox create --team <name> [--squad <name>] [--location <path>] [--root <dir>] [--tag <tag>]
[--local] [--yes]`

1. Takes the team from `--team` or `defaultTeam`. Reads runtime-only `abox.json`
   from the sandbox location: its optional `squad` fills an omitted `--squad`,
   while a case-insensitive conflict fails before preparation/replacement. Remote
   create requires a squad from one of those sources and never resolves local
   team folders; it works without a source tree, local manifest or build. Only
   `--local` resolves the team like `build` and asks for an otherwise omitted squad
   (or uses its only squad).
   The sandbox location is `--location`, then `defaultLocation`, then the current
   folder. Local team discovery starts there and searches parent folders; `--root`
    overrides that starting directory when the sources live in another project.
    `--root` requires `--local`; remote create rejects it rather than ignoring it.
2. Prepares all selected host user secrets and checks original selected values
   against the global deny list before any destructive replacement, without
   sandbox destination/policy access. Missing folder configuration skips this
   work. Then, before registry preparation, remote create reads
   `sbx settings get kit.allowedSources --json` and automatically appends the
   validated `rwest.jfrog.io/<pullRepository>/` prefix using `sbx settings set`
   only when no existing prefix (or existing `*`) covers it. It preserves all
   entries and never adds a wildcard or whole-host trust. The JSON array is one
   process argument, with no shell quoting. abox serializes its own settings
   updates and checks the evaluated value after writing; sbx provides no atomic
   append, so independent manual/settings clients must not edit the list concurrently.
   Invalid settings, administrator denials and excluding environment overrides
   fail before registry work or sandbox replacement. This persistent trust addition
   does not change signature requirements or other policies. `--local` never reads
   or changes this setting. Copying defaults into an override freezes that list
   across sbx upgrades; `sbx settings unset kit.allowedSources` restores current
   defaults and discards the entire overridden list, including the added trust.
   Remote create then resolves the published tag with Docker Buildx, selects the host's
   Linux platform manifest, validates its processed v3 workload descriptor, then
   pulls its digest-pinned configuration mixin from the retained `kits` list.
   Docker's host credentials need read access to both repositories. It reads
   `squad.json` from a temporary `docker image save` archive, without file extraction,
   builds or container execution. The cached config image is retained. The metadata
   schema/identity, state paths and matching host-mount capabilities are validated;
   missing/invalid metadata requires maintainer republication, never local fallback.
   Only `--local` reads the local build manifest for state and MCP metadata.
3. Resolves an existing current or legacy sandbox as above and retains its actual
   name for an authorized replacement. For a new sandbox, names it after the
   folder: `<folder>-abox-<hash>`, where the hash is the
   first five hex characters, uppercase, of SHA-256 over the normalized absolute
   path: forward slashes, lowercased on Windows.
4. Validates MCP registrations read-only, then asks before replacing an existing
   sandbox and lists what is lost. After consent, registers missing MCP servers
   with URLs and prepares host state directories before deletion. A refusal
   registers no MCP servers and creates no state directories.
   The state directories on the host are kept: the name of the sandbox does not change.
5. Passes one `--mount` per state directory under `<state dir>/<sandbox>/` on the host.
6. Passes the validated MCP server names with `--static-mcp`. A server with no URL
   must already be registered; a missing registration fails before replacement.
   Local kit arguments with the same key must have equal values; conflicting values
   are rejected with the key and both kit names before sandbox replacement.
7. Creates the sandbox from
   the immutable platform digest resolved from
   `rwest.jfrog.io/<pullRepository>/agentbox-rwest-<team>/<squad>:<tag>`, with
   `--pull always`, preventing moving-tag metadata drift. The set already contains
   its mixins and arguments. With `--local`,
    uses the **thin prebuilt-workload runtime kit**, `--kit <mixin>...`
    `--kit-arg <name>=<value>...` and `--pull never` instead. Both pass
    `--mount <host>:<path>... --static-mcp <names>`.
8. Keeps a record of the sandbox in `<state dir>/sandboxes/<sandbox>.json`: the team
   and the squad it comes from, the build metadata and the pulled reference (if any).
9. After successful creation and record save, copies prepared user secrets through
   the shared on-demand flow, before full-success/open output or returning to a
   launcher. A post-create copy failure retains the actual sandbox/new record,
   returns failure (1, or 130 for cancellation) and gives a folder-qualified
   `abox user-secrets --location "<folder>"` retry, without recreating. Creation
   failure, declined replacement or cancellation never starts a subsequent copy.

Automatic copying never forces deny-list matches: neither `--yes` nor replacement
consent is a safety override. Missing `abox.json` retains the old create/run behavior
with no user-secret store reads, target discovery or writes. See the
[lifecycle contract](user-secrets-lifecycle.md) for squad checks and intentional
on-demand override/recovery.

The user setting `pullRepository` defaults to **`mtiae-docker-release`**. It accepts
one lowercase JFrog repository path segment; an omitted or blank setting uses the
default. It is independent of the workload's `publishRepository`. See
[the settings file location and example](../README.md#user-settings).

The same user configuration also accepts:

- `defaultLocation`: the folder for `create`, `run` and `user-secrets` when `--location` is omitted;
  unset by default, which means the current folder.
- `teamsDirectory`: the project's teams directory, `teams` by default.
- `kitsDirectory`: the kits directory relative to the project root, `kits` by default.

Source discovery searches the start directory and its parents. `build`, `publish`
and `secrets` start at the current directory; local `create` starts at its resolved
location. An explicit `--root` overrides that start (local create only), with
relative paths made absolute before parent search. The discovered team carries
its owning project root into local kit/shared-workload lookup and build metadata;
nested invocation directories never become accidental kit roots. Configured nested
teams/kits directories, own-team-first base lookup, cross-team ambiguity checks
and personal additions retain the same resolution rules. Team/default selection
is shared in `AboxConfig`, preserving repeated teams for build/publish and single
teams for create/secrets. Settings are still loaded only by command actions, not
for help or internal exec/pipe commands.

`--tag` chooses the published set or local build tag, `latest` by default.
`--local` opts into local kits and includes their local/personal customizations.
Pull failures are reported without a silent local fallback.

For `--local`, before replacement or state/MCP changes, creation verifies the
runtime kit fingerprint and checks the unique image tag still has the recorded
Docker content ID in its recorded context. A metadata-only or legacy manifest
without `LocalWorkload`, missing image/context, replaced image or edited runtime kit fails
with an explicit rebuild instruction. `create` never regenerates the runtime
recipe or falls back to the original workload sources. Changing workload sources
only takes effect after a successful new `build`; previous images keep their
own unique tags and Docker's layer/build caches remain reusable.

Sbx receives `SBX_KIT_BUILDER=host`, `BUILDX_BUILDER=<recorded-context>` and
`DOCKER_CONTEXT=<recorded-context>` for local creation only. The context's
docker-driver builder shares the prebuilt host image store; a selected remote or
docker-container builder is not substituted. The thin v3 frontend build stages
declarations and exports/imports layers into sbx's separate runtime store, then
sbx composes mixins and applies mount, credential, policy and other v3 requests.
No `--template` override or v2 conversion is used. Workload installation is absent
from this path, but the first thin build/layer transfer can still take time.
Frontend resolution, registry-kit metadata and custom mixin recipes can still
need network/build work; `--local` does not promise fully offline creation or
prebuild arbitrary mixin recipes. The repository's mixins are declaration-only.

`Kits/StateEntry` owns the shared portable state-path and overlap rules for `.abox`
resolution, consumed local manifests and bundled published metadata. Creation
validates the entire stored state list before MCP lookup/registration, state
directory preparation or sandbox replacement, then mounts only those validated
entries. Stored paths must be canonical and retain their original derived host
mapping; they are not repaired on consumption. Sandbox overlap and portable
host-mapping overlap (including case and Unicode-normalization aliases) are both
rejected. Valid home-relative, absolute and whole-home mappings retain their
existing persistence layout. See [the state input rules and mapping examples](authoring.md#state).

Remote create instead consumes the published set's already built/processed
workload and mixin layers and their merged v3 declarations. It does not pass
team-local kit sources, local arguments or a host-builder override. No team
workload installations are repeated. User-specific local overlays on published
sets remain future work; none are applied implicitly.

Remote create needs host Docker/Buildx with a running engine and sbx, not team
sources or preparation. The bundled metadata format and compatibility rules are
in [artifact-format.md](artifact-format.md). Remote squad discovery is not supported;
give `--squad` or set the folder's `squad`. A pulled sandbox is not offered a
local-build replacement by `abox run`; recreate it explicitly to refresh the
published tag.

### `abox run [<launcher>] [--location <path>] [-- <argv>...]`

1. Resolves `--location`, then `defaultLocation`, then the current folder, just like
   `create`. Checks the programs the launcher needs on the host, then loads and
   validates folder configuration.
2. Checks that the folder has a sandbox. Without one, the command prints the
   `abox create` command to run, including `--team`, `--squad` and `--tag` from its
   sandbox record, build metadata or configured default team when available. Local
   records retain `--local` and their build's `--root`; unknown team/squad names are marked `<name>` placeholders
   to replace. The same complete hint is used when launcher programs are missing.
3. Ordinary run can open the existing sandbox even with a different or unknown
   recorded squad; it copies no secrets and changes no squad. When replacement is
   accepted and the folder config has `squad`, requires a known matching sandbox
   record before preparation/replacement. A conflict never silently switches squads.
4. For a locally created sandbox, offers to replace it when the build of its recorded
   team/squad/tag is newer than the one the
   sandbox was made of. abox knows the squad of a sandbox because it keeps a record of
   the sandboxes it created, in `<state dir>/sandboxes/`. Replacement stays local,
   resolving team sources from the newer build's recorded root (or the sandbox
   location for legacy metadata), so a separate `create --root` project stays usable.
   Accepted replacement uses `SandboxCreator`, including shared user-secret
   preparation/copying. Failed required copying prevents launch. Ordinary run or
    declining the newer build never reads selected host stores or resyncs secrets.
    Another team's build is never offered or borrowed for a create hint. Unproven or
    invalid local metadata reports a rebuild instruction without interrupting an
    existing sandbox's ordinary launch. A teamless legacy record can recover a hint
    only from an explicitly owned legacy manifest; it does not search other teams'
    qualified stores or use source paths to guess identity.
5. Checks the programs the launcher needs in the sandbox. A missing one means the image
   predates the launcher: the command says to create the sandbox again.
6. Asks the runtime where the workspace is (`WORKSPACE_DIR`) and starts the launcher.

The launchers of this version are `shell` and `herdr-app`. `shell` runs `bash -l` in
the workspace with a terminal, or the command after `--`.

### `abox secrets list|set|remove [--team <name>] --squad <name> [--service <name>]`

The kits of a squad ask for a service with the `credential@1` capability. The `secrets`
section of the configuration says how the value of that service is obtained. abox keeps
the values on the host, through `sbx secret`, so they never enter a sandbox.
The team defaults to the user setting `defaultTeam` when `--team` is omitted.

- `list` shows the services the kits ask for, the kits that ask for them, and whether
  the value is stored on the host.
- `set` asks for the values and stores them. The value goes through the standard input
  of sbx as explicit UTF-8 without a BOM or added newline, preserving non-ASCII service
  and custom secrets independently of the console code page. It stays out of process
  arguments, environment settings and shell history. Normal redacted diagnostics remain
  available; this encoding opt-in does not change sensitive-transfer suppression,
  attached-console streams or raw pipes.
- `remove` takes the values off the host. Custom secrets are matched by `--host`,
  or by sbx's `--env-name` selector when only `env` was declared; service names
  are used only for service secrets. `list` uses the same custom-secret identity.
  sbx 0.51 requires destination hosts for credential substitution; environment-only
  legacy mappings remain removable, but need a `host` to supply credentials.

### `abox user-secrets [--location <path>] [--force]`

The on-demand path copies explicitly selected .NET stores/selected keys into an
existing local or published sandbox, with no kit-source or build dependency. It
never creates or replaces a sandbox. See the
[configuration, safety and recovery contract](../README.md#copying-net-user-secrets).
The global YAML `userSecretsDenyList` matches case-insensitive literal substrings
in selected original scalar values; it defaults to empty. It is a policy safeguard,
not an arbitrary staging/production-secret detector. Mixed plain IDs and objects
with optional `keys` use [the same key-selection contract](user-secrets-filtering.md)
on demand and during [creation/replacement](user-secrets-lifecycle.md).

**Trust boundary:** workspace `abox.json` is writable by the sandbox agent and
selects host stores/keys for the next copy. Review/trust its current contents,
including squad, IDs and patterns, before invoking create/replacement/user-secrets.
This feature adds no separate host-side authorization/approval mechanism; neither
an explicit store name nor the deny list makes an untrusted workspace safe.

Shared orchestration is in `UserSecrets/`, outside command slices:

- `FolderConfig.LoadAsync(location, token)` reads runtime-only `abox.json`,
  returning null for absence. A missing file is an on-demand error but a lifecycle
  no-op. Its optional `squad` selects creation and rejects conflicting explicit
  selections; accepted run replacement requires a matching known sandbox record
  if that squad is present. On-demand copying never checks the sandbox's squad or
  reads its record. Ordinary run does not impose the replacement constraint.
  The team still comes from command/user settings.
- `SandboxCreationRequest` carries the folder configuration snapshot loaded at
  command entry, including null for absence. Creation never reloads it, including
  after deletion; host selections are then snapshotted before destructive work.
  The command's early squad-conflict check and shared lifecycle's pure revalidation
  intentionally protect both public entry paths without duplicate file reads.
- `UserSecretsCopy.PrepareAsync(folderConfig, force, token)` validates all entries,
  expands safe literal ID prefixes ending in one `*` against direct host store
  directories, then snapshots every selected host file and its nested JSON tree. ID
  patterns are validated separately from strict actual IDs. Expansion is ordinal
  case-insensitive, deterministic within each entry, and deduplicated across
  overlapping exact/prefix entries (key unions, whole store wins). No-match prefixes
  select no stores; missing exact copy-only IDs fail. Matching unsafe/case-colliding host IDs
  and symlinks/reparse points in the host user-secret root itself/stores/files fail
  before sandbox access; linked profile ancestors above that trusted root are allowed.
  A matched directory without a readable `secrets.json` is not skipped as an
  unmatched prefix; missing files require matching project appsettings sources. See
  [store-prefix semantics and verification](verification/user-secrets-store-prefixes.md).
  Preparation rejects duplicate
  effective case-insensitive .NET keys before filtering, then checks all selected
  **original** values before rewriting/writes. Plain IDs or objects with omitted
  `keys` select the whole store. Full-key case-insensitive patterns support only
  `*` (including across `:`); a union selects each key once. Empty/no-match lists
  produce `{}`, not a skipped file or a whole-store fallback. Filtered snapshots
  contain flat effective keys so array indices never shift. All input must still
  be valid, even with no matches or force. This phase makes no sandbox calls/writes.
  `PreparedUserSecrets` is opaque, in-memory and has a value-free `ToString`.
  `SafetyOverride` on the prepared snapshot/report means at least one selected
  original value actually matched the deny list and was allowed by `--force`, not
  merely that the flag was supplied. The on-demand warning uses the successful
  copy report; lifecycle callers retain those semantics.
- `ProjectSettings` statically discovers selected stores' literal `UserSecretsId`
  consumers beneath the `FolderConfig` location, excluding artifacts, escaping
  directory links and cycles. It never evaluates MSBuild. Selected project-local
  base/lowercase-development JSON is validated and merged case-insensitively;
  only selected keys are eligible for emission. Unfiltered appsettings metadata
  is retained for recognized split-setting resolution; only selected secrets can
  override it. `PreparedUserSecrets` owns project originals
  and association metadata in memory, separately from host snapshots, allowing
  project-only generation without a host secrets file.
  Unmatched stores remain copy-only. Only explicit case-insensitive selected-ID
  matches participate. Unrelated/computed/import-only IDs and unreadable/linked
  project XML are ignored without warnings or blocking generation; imports are
  neither detected nor evaluated. Readable XML explicitly naming a selected ID
  with conditional, multiple or malformed declarations fails preparation before
  any writes, even with empty filters, authoritative host values or force.
  See [bounded appsettings discovery](user-secrets-appsettings.md).
- `CopyAsync(prepared, sandbox, token)` finishes target preparation for all stores
  before writing. `UserSecretsCopy.Target.cs` discovers authorized host ports and
  prepares targets, retaining originals for deny/force accounting. Pure internal
  `EndpointAdaptation` handles URL, SQL Server, PostgreSQL, Oracle and recognized
  split-setting parsing/rewriting using only those ports, resolved effective settings
  and value-free diagnostic context; it performs no discovery, selection or copying.
  Selected host keys block generated replacements. Eligible appsettings-only
  values produce flat effective-key overrides subject to the same deny/filter
  checks. `UserSecretsCopy.SharedStores.cs` merges generic generated-key candidates
  against every consumer's desired effective values, including unchanged consumers.
  Only generated keys can conflict; host precedence and key filters are resolved
  first. Identical required values merge once with deterministic key spelling/order;
  incompatible values fail without a discovery-order winner. All explicit matching
  consumers participate; unrelated unresolved projects do not block generation. Parsing,
  adaptation, deny, policy and destination checks complete for all stores before
  the first write. Each new copy recalculates and removes stale generated entries.
  Each selected store replaces its entire destination, clearing old unmatched keys,
  even for a zero-key `{}` result. Reports contain IDs, key names/counts, never values.
  Only parsed JSON data is serialized for transfer:
  skipped comments and trailing commas must not survive as unscanned payload text.
  No hook framework or alternate pipeline is needed.
- `UserSecretsReportWriter` formats the value-free store summaries once for both
  automatic and on-demand output, JSON-escaping rewritten key names.
- `SbxClient.UserSecrets.cs` owns captured default-user exec, environment/path checks
  and the fixed receiver. All ancestor symlinks, nonregular/multiply-linked files
  and wrong-owned secret directories/files fail. It verifies stdin byte count and
  digest before same-directory atomic replacement. GNU-compatible coreutils are
  required. Secret root/store directories are 0700, files 0600. It does not assume
  `/home/agent` or run as root. The host adapter and receiver both prefer APPDATA
  over HOME, even on Unix, and reject unusable environment roots rather than guess.
  Destination checks omit `exec -i` and explicitly close an empty redirected stdin;
  they cannot consume or forward host stdin.

The host store is `<APPDATA>/Microsoft/UserSecrets/<id>/secrets.json` when APPDATA
is set (also on Unix), otherwise `<HOME>/.microsoft/usersecrets/<id>/secrets.json`.
The target roots are resolved independently for the non-root default exec
application user, not copied from the host. Alternate application environments
must use matching roots. Supported sandboxes need `sh` and GNU-compatible
`stat`, `mktemp`, `sha256sum`, `mv -T` and the other receiver utilities. A stopped
sandbox may start through exec. Destination validation is not a host-secret
authorization boundary against the sandbox application user.

Validation atomicity is all-store; transport atomicity is per-file. Failures report
completed counts and safe retry, never promise rollback or confirmation after lost
acknowledgements. Cancellation remains exit 130 and the on-demand command explains
partial-copy/retry behavior. Host files never change; no payload becomes a build
input, image layer or published artifact. Concurrent malicious filesystem edits by
the sandbox user/root and crash durability are outside this contract.

`--force` is available only on demand and bypasses only matched deny policy, not
input, policy metadata, destination or transport checks. The reporting contract is
to warn after successful copying only when at least one selected original value
actually matched, never merely because the flag was supplied. Automatic copying
always prepares with `force: false`; replacement consent/`--yes` is not force.

#### Supported rewrite contract (#8)

The implemented rewrite flow follows the approved clarification on #2/#8,
superseding experimental relay metadata, host/container port translation and
first-name selection. See [supported forms, preservation and verification](user-secrets-rewriting.md).
The later user clarification permits explicit sandbox-scoped local policy
declarations as an additional source of rewrite intent, without kit files or
recreation; the original kit-only restriction is superseded, not its safety checks.
The sandbox is the sole mapping source: no kit files, local build records, image
contents or registry lookups participate in runtime port discovery. Provisioning
provenance below describes existing runtime metadata, not a kit lookup.

- Discover exact declared TCP resources through
  `sbx policy ls <sandbox> --type network --json`. Keep active,
  sandbox-scoped/provisioned kit network allow rules with `net:connect:tcp` and
  literal `localhost:<integer 1..65535>` resources. Also recognize the observed
  sbx v0.51.0 persistent local-rule schema: active/editable, exact matching sandbox
  scope/application/identity, scoped/local origin/layer, `created_via: added`, UUID
  rule ID/name and policy ID, TCP-only action and only exact localhost-port resources.
  Unknown manual fields/provenance (including expiry/constraints) are excluded.
  Deduplicate/sort ports numerically. Wildcard/global/broad authorization is never
  inferred rewrite intent. An empty inventory is valid.
- Check current effective authorization with
  `sbx policy check network --sandbox <sandbox> --protocol tcp --json localhost:<port>`;
  a higher-priority deny excludes the port. Missing/malformed required metadata
  fails safely before writes. Do not invent mappings or change policy.
- Rewrite only supported loopback endpoint fields at an
  inventoried, currently authorized port to `host.docker.internal`, preserving the
  **same published host port**. URL/PostgreSQL/Oracle forms include localhost, dotted IPv4
  loopback and IPv6 loopback. Supported forms include absolute URI
  authorities, SQL Server comma-port fields (optional `tcp:`), and separate
  Host/Server + Port database fields. Standard URL scheme defaults and the
  PostgreSQL Host default 5432 are eligible only when that same host port is mapped.
  The internal `EndpointAdaptation.Oracle.cs` implementation handles Easy
  Connect/Plus and structural descriptors, including quoted `Data Source` values.
  It maps parsed host spans back to the original text, preserving credentials, service/SID names,
  options, formatting and compound remote/unmapped destinations. Easy Connect's
  default 1521 is eligible only if mapped; descriptors require explicit ports.
- Preserve named endpoints, including `host.docker.internal`, and their ports
  for TLS/SNI. Preserve undeclared ports and unrelated
  credentials/text/fields/paths/queries. A service published as `8004:5432` does not
  justify rewriting `localhost:5432` to port 8004.
  Recognized mapped-local endpoints with unsafe/ambiguous supported syntax now
  fail actionably before writes, without values. Arbitrary/unmapped localhost
  strings do not fail merely for mentioning localhost. The explicit
  `Database_Config:Host` + `Database_Config:Port` pair uses resolved effective
  metadata and changes only the host; see [split settings](user-secrets-split-settings.md).
- No host kit sources, staged descriptors, `/etc/hosts`, `host-port@1` or `sbx ports`
  are used. `sbx inspect` is insufficient for this inventory. Authorization is not
  proof of service availability or protection against later policy changes.

#### User-secrets integration status

Safe on-demand copying (#6), filtering (#7), authorized same-port endpoint
rewriting (#8), creation/replacement integration (#9) and URL/PostgreSQL
appsettings-derived overrides (#13), and compatible shared-store merging with
all-consumer conflict protection (#14), Oracle Easy Connect/descriptor support
(#15), and recognized `Database_Config` split-setting support (#16) are implemented
through the shared copy flow. Oracle and split settings feed the same
format-independent generated-candidate/effective-value safeguards. Selected keys
govern output; unfiltered appsettings-only metadata supplies split-port eligibility.
See [shared-store verification](verification/user-secrets-shared-stores.md).
See [Oracle command-level verification](verification/user-secrets-oracle.md).
See [split-setting command-level verification](verification/user-secrets-split-settings.md).
Final #12 review fixes and full integrated direct-executable verification are in
[the combined story record](verification/user-secrets-endpoint-story.md): Debug
and Release each passed **1168 tests**, with **7 platform skips**, zero failures,
and zero-warning/error serial SDK 10 builds. Missing-port local Oracle descriptors
now fail safely when 1521 is mapped; bare whitespace prose remains unchanged.
The subsequent requested explicit-selected-ID discovery simplification removes
unrelated unresolved-project warnings/blocking while retaining matching-project
validation and shared-store conflicts. Debug and Release each passed **1173 tests**,
with **7 platform skips** and zero failures; see
[the regression and verification record](verification/user-secrets-selected-projects.md).

The safety review fixes are integrated: normalized transfer excludes skipped
comments/trailing commas, no-payload checks cannot consume host stdin, and force
warnings report actual overridden matches only after successful copying. JSON
formatting/escape spelling is not a preservation contract; unchanged string
contents stay unchanged.

Earlier pre-#13 integrated Debug verification: **756 passed, zero failures, 4 explicit
native Windows symlink-privilege skips** (760 total). The skipped receiver cases
are not claimed as passing; see [rewrite verification](user-secrets-rewriting.md).
The focused user-secrets/create/run regression set passed **366 tests**, with the
same **4 skips** (370 total). The Debug solution build had zero warnings/errors;
CSharpier and `git diff --check` passed. No Release build or tests were run for
that final-review change.

Issue #13 review-fix verification: **988 passed, zero failures, 7 platform skips**
(995 total) in both Debug and Release. The skips include two new native file-link
discovery fixtures that this Windows host cannot create. Both solution builds had
zero warnings/errors;
CSharpier and `git diff --check` passed. New command-level coverage includes bounded
project discovery, generation, precedence/filtering, URL/PostgreSQL adaptation,
all-store validation, safe diagnostics, temporary shared-store rejection and
create/replacement/refresh behavior. See [the synthetic verification record](verification/user-secrets-appsettings.md).

Lifecycle regression coverage now explicitly permits non-replacing runs with
mismatched, missing or unknown squad records. On-demand copying now also permits
these records; creation/accepted replacement still reject unintended squads.
It also checks that entry-time folder snapshots, including absence, survive later
file edits. A small composed CLI harness shares
synthetic host/store and fake-tool setup across command, filtering, safety and
lifecycle suites; behavior-specific helpers stay local. The null-deny-list guard
remains intentional for directly constructed public settings, independently of
the YAML loader; force cannot bypass that invalid configuration.

The accepted [verification matrix](verification/user-secrets/README.md) is
**local/nonempty kit policy + published/empty kit policy**. An already-published
squad with nonempty host-access policy remains untested by explicit acceptance;
ambient global reachability is not kit rewrite intent. Windows sbx v0.51.0 live
transport evidence and representative Unix/automated path tests do not establish
an independent Unix-host sbx run. No host store mutation, production-secret reads
or new publication is needed or claimed by these synthetic checks.
The later [scoped-policy QA record](verification/user-secrets-scoped-policy.md)
documents explicitly authorized local declarations on the existing `D:\dev\rwe`
sandbox and read-only production port discovery; no real secret copy, connectivity
or authentication check was performed for that change.

### `abox ports [--location <path>] [--offset <n>]` (#43, #44, #45, #46)

Resolves the existing sandbox with the shared current-first/legacy resolver and
passes its actual identity to `ApplicationPorts/AppPorts`. Location precedence is
explicit, `defaultLocation`, current folder. No team sources, build metadata or
folder configuration is required, and no create/replacement occurs.

The shared module statically discovers all explicit .NET launch-profile URL ports
and Angular serve/start-script ports,
validates the complete plan, prints source occurrences and duplicate warnings, then
asks `SbxClient.EnsureApplicationPortsAsync` to ensure distinct offset mappings.
Discovery/reporting/application orchestration stays outside the command slice so
the HTTPS and lifecycle slices can reuse it without importing a command.
`AppPortDiscovery` owns one bounded filesystem walk/read policy for both formats and
static explicit Web SDK project detection;
`DotnetLaunchSettings`, `AngularConfiguration` and `AngularStartScript` own format
parsing. No extension framework or project-tool execution is involved. Angular #44
includes options and all named serve configurations, and a conservative literal
direct-CLI start-script grammar. See [scope/tests/live evidence](verification/angular-application-ports.md).

The command loads `FolderConfig` once before runtime access. Configuration owns
`portOffset` parsing/validation and `FolderConfig.SelectPortOffset(snapshot, option)`:
option, snapshot setting, then 10000. The same immutable snapshot still contains
squad and user-secret selections for creation/replacement callers; absence stays null.
Future lifecycle setup can select from that entry-time snapshot without rereading
the workspace. `AppPorts.PublishAsync(location, sandbox, offset, token)` applies the
selected offset to every discovered occurrence before calling the adapter. An explicit
offset is required: there is no overload that silently substitutes 10000. Lifecycle
callers (#47) must select from their entry-time folder snapshot, not reread configuration.
This does not add automatic create/recreate setup.

Offsets are integers -65534..65534, including zero and negatives, with complete-plan
widened arithmetic and resulting-port validation in 1..65535 before runtime mutation.
All adapter mappings must have one common offset. Neither global settings nor
configuration writes are introduced. See [offset semantics and evidence](application-port-offsets.md).

The sbx adapter completes a fixed noninteractive `exec <sandbox> true` before reading
`ports <sandbox> --json`: this waits for startup and restores existing bindings,
avoiding a verified sbx 0.51.0 stopped-publishing race. Native publishing does not
reach sandbox loopback, so `Sbx/application_ports.py` is an embedded receiver sent
through the same argv/stdin adapter. It requires existing sandbox Python 3.8+, uses
only its standard library, and never installs tools. Detached, non-root sandbox
relays listen on separate high ports and connect to application IPv4/IPv6 loopback
or the ingress sandbox IPv4 address. They never reserve the app port or mutate app
startup settings. Private owned state/control sockets under sandbox `~/.abox-ports-v1`
provide bounded readiness, serialized setup, repeat reuse and same-port restoration.
No host worker, shell-lifetime assumption or PID-based adoption/killing is needed.

The receiver gets the selected offset and validated TCP-binding snapshot. Under its
setup lock it reads all saved relay ports/control sockets, including no-longer-discovered
apps and running relays whose save acknowledgement was lost. Any existing binding
to a known relay with a different offset
refuses the whole change before starting new relays or changing bindings. Operators
must remove exact old bindings explicitly to change offsets; no ownership guessing,
automatic stale-binding removal or concurrent runtime-writer transaction is claimed.
Direct bindings to a requested app at another host port are also refused conservatively,
with an unproven-direct-mapping diagnostic, not a claim of a previous abox offset.

The adapter reuses exact `127.0.0.1:<host>:<relay>/tcp4` mappings, refuses conflicting
or malformed inventory, only appends missing bindings and verifies them after
publication. Every unproven direct native binding at a requested host port is a conflict,
even an exact loopback/host/app/protocol match at offset 10000. Matching numbers are not
ownership provenance. abox never unpublishes bindings automatically; diagnostics identify
the full conflicting spec and require deliberate operator inspection and exact removal
if replacement is intended. Different targets, wildcard binds and non-exact protocols
also remain conflicts. The historical #43 automatic native upgrade is retired; existing
proven relay mappings remain reusable, including the already-converted Deals mapping.
Application publishing is deliberately separate from user-secrets' opposite-direction
authorized host-access policy inventory.

Failures and cancellation explain possible partial state and retry, without claiming
success or rollback; relays may also remain after interruption. Publishing does not
run apps. Relays end when the sandbox stops; run
`abox ports` after starting it again to restore them (the command itself handles a
stopped sandbox). See [discovery bounds, runtime evidence and limitations](verification/application-ports.md).

#### Shared development HTTPS setup (#46)

After validated forwarding (or without ports when a Web SDK project is explicit),
`AppPorts` invokes shared `Https/DevelopmentHttps`. Non-Web/non-.NET folders perform
no host certificate operation. The host exporter locates an already-installed
dev-certs DLL with an eligible certificate-format requirement, privately checks
machine-readable existing identity/trust metadata, exports with a generated
password into owner-only staging, validates actual ephemeral PFX/key/SAN/dates and
fresh exact trust metadata, then clears/removes temporary private material. No
native-store export fallback, automatic alternative identity or host trust change
is allowed. CLI pre/post checks cannot make concurrent store renewal/deletion
atomic; see [the explicit CLI race contract](https-development-certificates.md).

`SbxClient` activates unchanged encrypted PFX bytes at an owner-only runtime path
outside workspace/HOME/build mounts. Protected `config.json` fixes the canonical
Kestrel path/password for subsequent launches. Stable password/path make renewal
one atomic PFX replacement rather than mismatched independent file updates.
Shared `ExecAsync` bootstrap covers shell/direct argv/exec/pipe and newly initialized
agent/core children after abox exits; it preserves streams, argv, workdir, TTY flags
and actual child exit status. It is not a shell-profile-only mechanism.

Existing recognizable hc/OpenCode/Copilot/pi/Claude parents with missing/mismatched
settings block full activation and launch; nothing is automatically restarted.
Herdr checks readiness before desktop opening. Explicit fresh-parent recovery and
running-app renewal/restart requirements are documented in the
[HTTPS contract](https-development-certificates.md). New processes cannot mutate an
arbitrary old Unix shell's environment, and raw sbx launches bypass this contract.

Production export/activation and synthetic exact-leaf .NET 10/real-local-tool
verification are present, but #46 acceptance remains incomplete; see
[the scoped production-path record](verification/https/README.md). Separately,
[manual Windows verification](agents/story-3-host-certificate-verification.md)
completed exact host CLI export/copy and explicit Deals activation with default
host TLS (Swagger HEAD 200) and sandbox TLS (HEAD 404), without changing host trust.
That does not establish a production real-host `abox ports` run, browser reload,
Copilot's actual shell tool, the host Herdr desktop or independent Linux-host
evidence. No #47 automatic create/replace/lifecycle integration is implemented.

### `abox prune [--full] [--sbx]`

Sbx stores every kit it builds as a template image: one image per build plus one per kit
in it, and the sources of the kits under a own repository (sbx-kit-src). They pile up,
because a build replaces them with new ones and sbx never takes the old ones away.

`abox prune` lists the template images (`sbx template ls --json`), groups the images of
the assembled repository by their build id (the tags `«id»`, `«id»-kit-N` and
`«id»-template` of one build share the id), and keeps the newest build together with its
kit sources: an sbx-kit-src image stays as long as its image id is in the kept set. What
is left goes with `sbx template rm`, one image at a time, and sbx asks nothing (--force).

With `--full`, the prune reaches for every eligible sbx kit image of a known age and
id, not only the stale sets, subject to the conservative checks below. What a sandbox
uses, sbx refuses and keeps; full prune reports those refusals without failing. With
`--sbx`, it stops the running sandboxes first (`sbx stop`, one per sandbox, the kit
builder included).

Both modes recognize only the exact repository names `sandbox-kit-assembled` and
`sbx-kit-src`, either bare or as the final `/`-delimited component of a qualified
repository (for example `docker.io/library/sbx-kit-src`). Arbitrary suffix matches
such as `other-sbx-kit-src` do not qualify and cannot influence newest-set selection.
An image without a reported age or nonblank image id stays in both modes. If any
assembled image has uncertain metadata, its whole build group (including kit and
template aliases) stays, and all kit sources stay: the inventory cannot prove which
sources that retained set may need. Other complete assembled groups can still be
removed when eligible; normal mode also keeps every group tied for newest.

The command never calls broad `sbx prune` or `sbx reset`, touches secrets or unrelated
repositories, or removes host-Docker local builds or registry components. All removal
requests remain individual `sbx template rm` calls, subject to runtime in-use refusals.

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
generated image has `/home/agent/workspace` as its default working directory, not
a guaranteed runtime workspace mount path.
Generated Dockerfiles and workload/state descriptors, and copied workload scripts,
use LF line endings on every host OS. Scripts are copied by file name; two source
paths with the same file name (case-insensitive for cross-platform builds) are
rejected instead of overwriting one another.

When tools or scripts are requested, the generator installs mise as `agent` if it
is missing, then checks that it runs. The installer is downloaded before execution,
so a failed download stops that build step instead of being hidden by a shell
pipeline and causing `mise: not found` in the next step.

Every generated workload (including one without tools) installs the approved
`cli/src/Abox/Assets/rwe_inspection_certificate_chain.pem` bundle before any apt,
mise or script downloads. This is the supplied RWE inspection CA bundle: public
certificates and CA metadata comments, not private keys. It is embedded in the
CLI assembly, so global tool installations do not need the repository or
`C:/Localdata` at runtime. The generated `certificates/` build-context directory
contains the exact bundle and one `.crt` per certificate; as root, the Dockerfile
copies these into `/usr/local/share/ca-certificates/abox/` and runs the distro's
`update-ca-certificates` before returning to `agent`. Templates must provide
`update-ca-certificates` (the `ca-certificates` package) and `curl`; trust setup
does not download anything to bootstrap itself.

The system trust store and certificate files remain in the runtime image.
`MISE_EXTRA_CA_CERTS` and `NODE_EXTRA_CA_CERTS` add this same bundle to mise's and
Node's roots for subsequent tool/package downloads without replacing default
roots or disabling TLS verification. The host certificate store is never imported.
Generated assets are covered by the workload source fingerprint and publication
snapshot like the Dockerfile and scripts; no manifest contract change is needed.
After updating the approved repository bundle, rebuild the CLI/tool and workload.

## The code

```
cli/src/Abox/
  Program.cs        runs the command line
  Cli/              the composition root: the service collection and the root command
  Features/
    Build/          abox build: the command and the service that does its work
    Publish/        abox publish: v3 frontend builds, JFrog pushes and set assembly
    Create/         abox create: the command and its slice registration
    Run/            abox run: the command and the service that does its work
    Secrets/        abox secrets: the command and the service that does its work
    UserSecrets/    abox user-secrets: on-demand command only
    Ports/          abox ports: on-demand command only
    Prune/          abox prune: the command and the service that does its work
    Sandbox/        the commands other programs call: abox exec and abox pipe
    CommandHandling.cs  turns an abox error into a message and an exit code
  Configuration/    the directories and the user configuration
    FolderConfig.cs runtime-only abox.json (not squad/build configuration)
  UserSecrets/      shared selection/policy, bounded project/appsettings discovery,
                    endpoint override generation and runtime copy flow
  ApplicationPorts/ shared static discovery, occurrence reporting and publishing flow
  Kits/             the .abox and .workload files, the chain, the kit generators
  LocalState/       local on-disk state shared by the command slices
    BuildManifest.cs
    BuildSources.cs
    BuildStore.cs
    LocalWorkloadImages.cs
    PersonalConfigurations.cs
    SandboxRecord.cs
    SandboxRecordStore.cs
  Launchers/        one class per launcher
  Sandboxes/        application-level sandbox orchestration shared by create and run
    SandboxCreator.cs       the create operation shared by create and run
    SandboxResolver.cs      current-first, exact legacy fallback for existing sandboxes
    PublishedSquadSource.cs reads the published set's bundled squad metadata
    SquadSources.cs         local and remote ISquadSource adapters
  Sbx/              every call to sbx: CLI arguments, settings and response models
  Health/           the checks every command runs first
  Infrastructure/   the console, the processes, the YAML
    OciRegistry.cs          raw Docker calls and OCI inspection primitives
    TemporaryDirectory.cs   temporary working directories and their cleanup
  Naming/           sandbox and image names
    ImageName.cs            the unqualified team reference of a local build
```

For architecture and coding conventions, see the
[coding standards](../CODING_STANDARDS.md#architecture).

## The decisions this version took

- **Published creation, explicit local builds.** `abox create` pulls a published set
  from the user's `pullRepository`; `--local` uses the local build instead.
  `abox publish` pushes v3 kit sets to the workload's `publishRepository` through
  Buildx. Remote state/MCP metadata comes exclusively from the pinned published set.
  `--tag` chooses the tag of
  the local build and the published set, `latest` by default.
- **Expensive local workload build before create.** `build` produces host-Docker
  content using the v3 frontend. Local create only wraps that image in a thin v3
  workload and composes kits. It checks recorded content identity and never
  silently reruns the original workload recipe. Publication still builds from
  independently fingerprint-checked original source snapshots.
- **The teams folder.** A team is a folder under `teams`. Its `.abox` files (the squads
  of the team), its `.workload` and the scripts of the workload live in it. The kits
  stay in the `kits` folder of the project, so every team can use them; the workload of
  a team lives in the team folder, a shared one can stay in the `kits` folder.
- **The name of a local squad is the name of its file.** `abox create --local --squad AC` finds
  `ac.abox` in a team folder and calls the squad `ac`.
- **A build is of a team.** `--team` names the team folder, and the build makes every
  squad of it that no other squad names as its base. When `abox create --local` is not asked
  for a squad and the team has more than one, it asks which one.
- **The workspace is where the runtime puts it.** The runtime mounts the folder of the
  host at the same path in the sandbox and says so with `WORKSPACE_DIR`, so the
  launcher asks for it instead of assuming `/home/agent/workspace`. The shipped
  OpenCode and pi guidance also directs agents to enter `WORKSPACE_DIR`, retaining
  their agent-specific conventions. Both distinguish host proxy credentials from
  explicitly copied .NET User Secrets in sensitive runtime application files.
- **The state directories of a squad are mounted from the host.** sbx mounts a host
  directory only where a kit asks for it, so abox writes a mixin that asks for every
  state directory and passes one `--mount` per directory.
- **The MCP servers are registered on the host.** A server with an url is registered
  with `sbx mcp add` when it is not registered yet; a server without one has to be
  registered already.
- **The kit/proxy secrets live on the host.** The kits ask for a service, the configuration says
  how its value is obtained, and `sbx secret` keeps it. The value never goes into a
  sandbox and never into the arguments of a process. The separate opt-in .NET
  `user-secrets` flow intentionally copies selected host values into runtime files
  readable by the sandbox application user; those values are not proxy secrets.

## What is not built yet

- Discovering published squads remotely when `--squad` is omitted.
- Pruning local builds, versioned runtime kits, host-Docker `abox-workload` images
  and registry components. `abox prune` currently removes only sbx template images.
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

1. **The rest of `abox prune`** — the local builds of the build store are not pruned
   yet. The build store knows what is there; the sandboxes say what is in use.
2. **The agent kits** — the kits of `kits/` carry the credentials and the guidance of
   the agents, but not their configuration files yet.
3. **The `orca` launcher** — the SSH key pair, the host entry and the Orca CLI are
   described in the concept and have not been written.
