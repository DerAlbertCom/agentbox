# Sandbox endpoint overrides from appsettings

During `abox create`, replacement (including an accepted replacement from `run`),
and explicit `abox user-secrets`, selected stores can receive sandbox-only endpoint
overrides derived from their projects' appsettings. This uses the existing copy,
policy discovery, key filters and sensitive transfer, not a second copy command.
Ordinary `run` does not read appsettings or resynchronize secrets.

For example, with `"userSecrets": ["api-development"]` in the workspace's
`abox.json`, a project declaring a literal `<UserSecretsId>api-development</UserSecretsId>`
and this project-local `appsettings.development.json`:

```json
{
  "Telemetry": { "Endpoint": "http://localhost:4317" },
  "WorkerApi": "http://localhost:5000/api"
}
```

If the sandbox's kit policy declares and currently authorizes host port 4317 but
not 5000, generation adds only:

```json
{ "Telemetry:Endpoint": "http://host.docker.internal:4317" }
```

The worker-to-API address stays in appsettings, unchanged. Only the sandbox
User Secrets file is replaced; host secrets, projects and appsettings are never
modified. Settings need not be under `ConnectionStrings`.

## Bounded discovery, not application startup

- Search recursively only beneath the resolved folder containing the selected
  `abox.json` (`--location`, `defaultLocation`, then the current folder). The team
  sources selected with `--root` are not the workspace for this search.
- Exclude `.git`, `bin`, `obj` and `nCrunchTemp_*.csproj` artifacts. Directory links
  escaping the workspace are not followed. In-workspace links are resolved and
  deduplicated to prevent cycles and duplicate consumers. Project/configuration
  inputs must be ordinary files, not file links/reparse points.
- Associate selected stores case-insensitively with one unconditional literal
  `UserSecretsId` declaration in a project's top-level `PropertyGroup`, using the
  existing portable store-ID rules. Do not execute builds, evaluate MSBuild,
  read imported property values, or guess computed/conditional/multiple/invalid IDs.
  SDK projects without a literal ID also get an unresolved-association warning
  when a `Directory.Build.props` or `Directory.Build.targets` exists between the
  project and workspace root. Only its presence is checked: no import contents or
  ancestors above the workspace are searched/evaluated. Explicit imports and
  computed IDs that cannot be ruled out for the selection still produce value-free
  project warnings. These projects are tracked as possible selected-store consumers,
  not arbitrarily associated or merely warned about: **generation is blocked until
  their associations are resolved**, while authoritative host-only copying remains
  available. This is intentionally conservative when a common workspace import
  exists: even a library without its own ID could inherit one. Import contents are
  not inspected to infer which libraries consume a store. Unconditional literal
  unselected IDs remain unselected; with no import evidence, definitely unselected
  literal IDs and projects without association evidence do not block generation.
  Missing-store errors explain the required literal association and sources.
- Discovery never selects an additional store or authorizes network access.
  Existing store-ID prefixes still expand **only host store directories**, not
  project IDs. Use an exact selected ID for a project-only store that has no host
  directory. No selected stores means no project discovery and no copy.
- Only associated, selected projects' JSON is read. An unmatched selected store
  retains copy-only behavior and requires its readable host `secrets.json`.
- Unreadable/malformed/DTD-bearing or linked project XML cannot establish an
  association. Warn and permit validated host-only copying rather than breaking
  copy-only selections because of an unrelated template. Do not guess its store
  or read its appsettings. Since it could hide a literal shared-store consumer,
  **generation is rejected while any discovered project XML is unreadable** (as
  with possibly selected unresolved IDs above);
  repair that project or supply authoritative selected host keys. This warning
  does not suppress validation of selected host files or associated appsettings,
  even with empty key filters or force. Missing host files still need an actual
  associated appsettings source. Directory traversal failures remain fatal.

This is not a reconstruction of custom/linked/generated configuration providers,
environment or command-line overrides, other environments, launch profiles,
application startup order or values captured before User Secrets are added.
Configure the application to consume the standard User Secrets overrides.

## Precedence and filtering

For each associated project, combine project-local `appsettings.json`, then
**lowercase** `appsettings.development.json`. Development wins for the same
case-insensitive effective .NET key. `appsettings.Development.json` is intentionally
not read, even on a case-insensitive host. Comments/trailing commas are accepted;
every present file must be a readable UTF-8 JSON object with no duplicate effective
keys within it, including nested/colon and array-index collisions. Validation runs
before filtering; force cannot bypass it.

Selected host secrets take precedence over appsettings. An existing selected
effective key blocks generation regardless of its value, including null, remote
addresses and arbitrary strings. Eligible selected secret values are still
adapted. Generation adds **only values that need endpoint adaptation**, not the
whole appsettings file. Generated keys are flat effective keys (including array
indices); unfiltered copied host values keep their JSON structure.

The same exact/wildcard/empty key filters govern both sources. Excluded host keys
cannot reappear through generation. An empty resulting selection replaces the
destination with `{}`. Missing host secrets are allowed only when an associated
project has at least one standard appsettings source. If neither source exists,
preparation fails actionably rather than silently clearing a missing store.

## Endpoints and the temporary shared-store restriction

Supported new shapes are absolute endpoint URLs and PostgreSQL connection strings
with one host and one port, including `localhost`, dotted IPv4 loopback and IPv6
loopback. Explicit ports retain their spelling. A URL's standard scheme default
port and PostgreSQL's standard 5432 default are eligible only if that **same host
port** is declared and authorized. Existing SQL comma-port/Server-plus-Port support
remains. See [the full rewrite contract](user-secrets-rewriting.md).

Replace only the host with `host.docker.internal`; preserve credentials, ports,
database names, paths, query strings, fragments and all other connection options.
Remote hosts and unmapped localhost endpoints are untouched and are not generated.
There is no container-to-host port translation: publishing `8004:5432` does not
make `localhost:5432` eligible when only port 8004 is declared.

Known mapped-local URLs with unsafe syntax and recognized ambiguous/malformed
connection strings (such as multiple hosts/ports or duplicate fields) fail with
store/key/project recovery information, without values. Arbitrary strings and
unmapped localhost references are not errors merely for containing `localhost`.
This includes PostgreSQL URL host lists and query-supplied `host`, `hostaddr` and
`port` options when an explicit mapped-local port is known. These query endpoint
forms are recognized for fail-closed validation, not rewritten. Query options take
precedence over the authority; a nonempty `hostaddr` supplies the connection address
even when `host` names a remote TLS/authentication identity. Empty `hostaddr`
entries fall back to their corresponding hosts. Positional host/port lists are
matched before deciding locality, so a remote host's mapped port does not implicate
a local host at an unmapped port. Remote/unmapped overrides are preserved rather
than adapting the overridden authority. Hostless forms such as
`postgresql:///db?host=localhost` or `postgresql:///db?hostaddr=127.0.0.1` without an
explicit port remain unchanged: no default port is inferred for them.
The explicit `Database_Config:Host` + `Database_Config:Port` pair is also supported:
resolve both keys before eligibility and generate only the host override. Unselected
appsettings port metadata can establish eligibility; filtered-out secrets never
participate. See [recognized split settings](user-secrets-split-settings.md) for the
exact supported shape, filters and safe-failure rules. Oracle Easy Connect/descriptors
remain a separate follow-up.

**Temporary fail-closed restriction:** if a store has multiple discovered project
consumers, any generated override is rejected, even when both projects would
generate the same value or another consumer needs no rewrite. Enumeration order
never chooses a winner. Copy-only authoritative-secret behavior is still allowed:
provide the selected host key explicitly, filter out the generated key, or use
separate stores. This restriction does not implement compatible shared-store
merging or conflict resolution.

## Refresh, safety and failures

Each copy rebuilds the **complete selected destination** from current selected host
secrets plus current eligible generated overrides. Removed/remote/unmapped keys
stop being generated, clearing stale entries. Manual destination-only edits are
not preserved. An ordinary run never performs this refresh.

All selected inputs are parsed before copying. Policy authorization, adaptation,
applicable deny-list checks, temporary shared-store checks and destination checks
for **all stores** finish before the first destination replacement. Thus a
configuration/adaptation failure writes zero destination stores. Unexpected
write-time failures can still leave earlier files replaced; no cross-file rollback
is promised. Retry with `abox user-secrets` after correcting the problem.

The deny list checks selected copied originals and original values that would
produce generated overrides, not excluded or otherwise un-emitted appsettings
values. `--force` bypasses only matched deny policy, with a warning only after a
successful actual override. Automatic copying never forces. Host-secret checks and
project parsing occur before destructive replacement; generation needs the new
sandbox's runtime policy, so generation/policy/destination failures can occur
**after creation**. The truthful sandbox record is retained and launch is stopped;
use the printed retry command, not another recreation.

Normal and verbose diagnostics identify stores, escaped effective key names and
project paths/reasons, never endpoint values, credentials or sensitive URI parts.
Configuration tests use only temporary synthetic sources and a substituted
`ISbxClient` through the existing CLI command seam. They prove configuration
output and safety, not database authentication, service readiness or an independent
live sandbox run. Existing [policy/transport verification limits](verification/user-secrets/README.md)
are unchanged.
