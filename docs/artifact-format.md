# Publishing the v3 artifact format

## The legacy push limitation

Sbx's internal template tags (`sandbox-kit-assembled:<build id>`, the `-kit-N`
and `-template` tags, and `sbx-kit-src:<kit>-<hash>`) are not a publishing interface.
They cannot simply be renamed or exported through `sbx template`.

The original experiment with `sbx kit push <directory> <reference>` on sbx 0.51.0
also failed: its `spec.yaml` packager rejects the v3 descriptor's top-level
`capabilities`, `author`, `provides` and `args` fields. Wrapping them under `spec:`
does not fix the different grammar. Sbx 0.50.1's installed help still describes
that legacy packager. This is an artifact-format mismatch, not a registry
reachability problem.

## The supported v3 publishing path

The [official v3 frontend](https://github.com/docker/sandbox-kit-spec#publishing-a-set-as-one-kit)
supports publication independently of that packager:

1. Build each local kit with its descriptor as the Buildx `--file`. The first line
   `# syntax=docker/sandbox-kit:3` selects the frontend. Push directly to the registry
   with `--push`, rather than round-tripping through a classic Docker image store
   that can discard descriptor annotations.
2. Generate a `kind: set` descriptor whose `kits` list contains published references
   and each kit's create-phase `args`. Local directories cannot appear in that list.
3. Build and push the set with the same frontend. It resolves and pins the component
   digests, validates composition, shares their layers and produces one ordinary
   workload kit. Consumers never see `kind: set`.

`abox publish` uses this path for `rwest.jfrog.io/<publishRepository>`, using the
repository selected in each resolved `.workload` and recorded in its local build
manifest. The included workloads select `mtiae-developer-container-images-local`.
Authentication uses Docker's host credential configuration. It does not depend on
a change to the legacy `sbx kit push` implementation. The full command, naming and credential
expectations are in [implementation.md](implementation.md).

Every squad uses the stable path `agentbox-rwest-<team>/<squad>:<tag>`, with
components below a separate `/components/` segment. Builds must exclude personal
configuration and retain matching source fingerprints. All selected inputs are
validated/snapshotted before the first push. Uploaded artifact digests and every
runnable platform descriptor are inspected remotely; only then is the verified
candidate set promoted to the requested tag. No sbx preflight is needed to publish.

Local verification with Docker Buildx 0.38.1 and frontend 3.0.0-m.11 confirmed that
the generated configuration mixin builds as an OCI image and retains its v3
descriptor in `vnd.docker.sandbox.kit.descriptor`. A declaration-only mixin with a
required create-phase argument also built successfully. Create-phase values are
fixed in the set descriptor, not passed as Buildx arguments; supplied build-phase
arguments are rejected. No JFrog tags were changed during these checks. A live JFrog
push still requires maintainer credentials and an explicitly chosen tag.

Publishing and self-contained pulling in `abox create` are implemented. Remote
creation resolves the selected platform manifest, validates its processed v3
workload descriptor, and reads the one digest-pinned `/components/config` mixin
listed in its retained `kits`. Docker credentials handle these registry reads.
The small mixin is pulled and saved locally; its ordered image layers are read as
tar/gzip streams without extracting files or executing a container. Temporary
archives are deleted; the image stays in Docker's cache. The final set is given to
sbx by immutable platform digest so metadata and declarations cannot drift with
a moving tag. No workload is built and no registry is written.

The bundled `/usr/share/abox/squad.json` contains `SchemaVersion: 1`, `Team`,
`Squad`, `Tag`, `CreatedAt`, `Mcp`, `State`, and informational `Kits`. The original
publisher's identical format without `SchemaVersion` is also accepted as version 1.
Identity, required fields and safe state paths are checked; every mount must have
an exact host-mount declaration in the processed set. Missing or incompatible
metadata requires a maintainer rebuild/republish, never a local-manifest fallback.
All metadata/MCP checks precede sandbox removal. Remote squad discovery is not
implemented; `--squad` is required. `--local` consumes the prebuilt host workload
through a thin v3 source kit instead of pulling the published set; see below.

## Local prebuilds without registry writes

Sbx 0.51.0 does not provide `kit build`. `kit inspect` reads declarations, and
cannot load recipe-based v3 sources without a kit builder. Importing an OCI image
with `sbx template load` does not make its kit reference resolve offline: a probe
of `sbx create <loaded-reference> --pull never` still fetched the manifest from
its registry. Template content alone is not a substitute for kit declarations.

Agentbox therefore uses the supported source-kit host-builder path. `abox build`
builds the original workload once with the v3 frontend into host Docker using
`--load`. It retains the original recipe separately and writes a thin v3 workload
whose entire recipe is `FROM <unique-prebuilt-local-tag>`. `abox create --local`
uses that thin kit with sbx's host builder and the recorded docker-driver context,
preserving all v3 composition and runtime capabilities. No workload apt/tool/script
installation appears in the thin recipe. The recorded Docker content ID and thin
kit fingerprint are checked before replacement; missing artifacts require rebuild.

A local sbx 0.51.0 / Buildx 0.38.1 smoke test ran `abox build --no-personal` and
two `abox create --local` commands for distinct folders using the resulting thin
workload, an argument-bearing v3 mixin, network deny rule and a host state mount.
A build-script timestamp matched the prebuilt image and both sandboxes; the
argument's environment value and the mounted state file were also verified.
The second create reported both the workload and composition cached. Only the
probe sandboxes/images were removed afterward; no registry was written.
Creation still stages/exports/imports content
and composes mixins; it is not a promise of zero BuildKit work. Unlike the v2 POC,
this route needs neither v2 translation nor a template override that could lose
the workload's v3 declarations. Custom recipe-based mixins remain create-time
work, and published sets keep using the existing verified publication pipeline.

Remote consumers need neither sources nor any local build/metadata preparation.
`abox build --metadata-only --no-personal` remains an optional maintainer mode,
not a remote prerequisite. It clears local image selection without deleting prior
artifacts. Normal builds and `--no-verify` still build the workload image. See the
[remote-only workflow](../README.md#remote-only-workflow).
