# The artifact format blocks pushing a kit

A set of sbx template images carries no names a user can rely on. The build
writes it into the store as `sandbox-kit-assembled:<build id>` (with
`<build id>-kit-N` and `<build id>-template` tags) and the sources of the kits
as `sbx-kit-src:<kit>-<hash>`. Those names are inside the kit build pipeline
and abox does not reach them: no setting names them, and retreated from
`sbx template ls` there is no export or rename.

The name a build is meant to be published under, `agentbox-rwest-<team>:<tag>`
with `latest` as the default tag, lives in the manifest of the build. To put
that name on an artifact, sbx offers `sbx kit push <directory> <reference>`.
With sbx 0.51.0, that command rejects every shape we tried, including the
examples of the [kit spec](https://github.com/docker/sandbox-kit-spec):

| Tried `spec.yaml` | Error |
|---|---|
| our generated workload descriptor (`capabilities:` at the top) | `field capabilities not found in type spec.SpecFile` |
| the schema's fields wrapped in a `spec:` root | `field spec not found in type spec.SpecFile` |
| the spec repo's own `examples/hello/hello.yaml`, unchanged | `field author not found …` `provides …` `args …` `capabilities …` |

The `spec.SpecFile` the push validates against therefore speaks another format
than the v3 descriptor schema of the kit spec
(`schema/kit.schema.json`, where `capabilities`, `args` and friends are
top-level). The spec repo carries an `assemble` flow that may publish a set
under one reference; it is not wired into the CLI of 0.51.0.

## What this means

- `abox publish` cannot be built until sbx can carry a kit set under the
  reference `agentbox-rwest-<team>:<tag>`: either a newer sbx gains it, or abox
  has to generate the layout `kit push` accepts.
- Until then, `abox create` stays local (`--local`): the templates in the sbx
  store and the manifest keep the flow whole; only the registry leg is missing.
- The experiment was run against the daemon's own kit registry facade
  (`127.0.0.1:5411`), which is reachable; the failure is in the artifact
  format, not in reachability.

## Where to take it next

With the live registry: push one of the spec repo's examples, and if it also
test fails there, raise it with the kit spec maintainers; the schema and the
packaging code disagree. If it works with a newer sbx CLI, the fix is to
upgrade and wire `abox publish` against it.
