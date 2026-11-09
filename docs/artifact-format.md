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

Publishing and pulling the published set in `abox create` are implemented. Creation
still uses a local manifest for state and MCP metadata; reading the bundled squad
configuration remains separate work. `--local` uses local kits instead of pulling.
