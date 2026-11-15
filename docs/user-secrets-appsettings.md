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

If the sandbox's runtime policy explicitly declares and currently authorizes host
port 4317 but not 5000, generation adds only (using either supported kit declarations
or [explicit sandbox-scoped local rules](user-secrets-rewriting.md#runtime-policy-is-the-inventory)):

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
- Exclude directories named `.git`, `bin`, `obj` or `node_modules`
  (case-insensitive) anywhere **beneath** the selected workspace root, plus
  `nCrunchTemp_*.csproj` artifacts. Projects beneath `node_modules` are not
  consumers for store association or shared-store conflict validation, even if
  they explicitly name a selected store or contain invalid configuration. Move
  intended consumers outside dependency trees to include them. Similarly named
  ordinary directories such as `node_modules-backup` remain included. An explicitly
  selected root named `node_modules`, or below one, retains its meaning; only
  excluded descendants relative to that root are skipped.
- Directory links escaping the workspace are not followed. Both directory names
  encountered during traversal and resolved targets respect the exclusions, so
  aliases cannot bypass dependency boundaries. In-workspace links are resolved
  and deduplicated to prevent cycles and duplicate consumers. Project/configuration
  inputs must be ordinary files, not file links/reparse points.
- Associate selected stores case-insensitively with one unconditional literal
  `UserSecretsId` declaration in a project's top-level `PropertyGroup`, using the
  existing portable store-ID rules. Project XML is inspected only to find explicit
  matches; only matching projects' appsettings are read. Do not execute builds,
  evaluate MSBuild, read imports (including `Directory.Build.props`/`.targets`),
  or infer IDs from expressions, conditions or imports.
- Projects without an explicit matching ID are ignored **without warnings or
  blocking generation**. This includes computed/import-only/absent IDs and
  conditional, multiple or invalid declarations whose values do not match a
  selected ID. An import's presence does not invalidate an explicit matching
  literal declaration, and its values are never used to override that declaration.
- If readable XML explicitly names a selected ID but its declaration is conditional,
  multiple or malformed (for example nested elements, attributes or incorrect
  placement), fail before copying any stores. Declare exactly one unconditional
  literal ID in a top-level `PropertyGroup`. Empty filters, authoritative host
  values and `--force` do not bypass this selected-project validation.
- Discovery never selects an additional store or authorizes network access.
  Existing store-ID prefixes still expand **only host store directories**, not
  project IDs. Use an exact selected ID for a project-only store that has no host
  directory. No selected stores means no project discovery and no copy.
- Only associated, selected projects' JSON is read. An unmatched selected store
  retains copy-only behavior and requires its readable host `secrets.json`.
- Unreadable/malformed/DTD-bearing or linked project XML cannot establish an
  association and is ignored without warnings, even if its raw text mentions a
  selected ID. Do not guess its store, follow file links or read its appsettings.
  This explicit-association policy intentionally does not protect consumers hidden
  in unreadable XML or supplied only by MSBuild evaluation; put their literal IDs
  directly in readable ordinary project files to include them. Selected host files
  and associated appsettings are still validated, even with empty key filters or
  force. Missing host files still need an actual associated appsettings source.
  Directory traversal failures remain fatal.

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

## Endpoints

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
Oracle Easy Connect and structural descriptors are also supported, both as whole
values and inside Oracle connection strings' `Data Source` fields. Compound
destinations adapt only eligible hosts; ports, credentials, service/SID identifiers,
descriptor structure and unrelated options retain their exact string spelling.
The Oracle Easy Connect default 1521 is eligible only when mapped. Descriptors
require an explicit `PORT` for adaptation; a local address missing it fails safely
when 1521 is mapped, rather than silently retaining a potentially broken default.
Remote/unmapped addresses remain unchanged. Unsupported or
ambiguous known mapped-local Oracle forms fail before any store is written, with
the same value-free diagnostics. See [Oracle forms and limits](user-secrets-rewriting.md#oracle-easy-connect-and-descriptors-15).

The explicit `Database_Config:Host` + `Database_Config:Port` pair is also supported:
resolve both keys before eligibility and generate only the host override. Unselected
appsettings port metadata can establish eligibility; filtered-out secrets never
participate. See [recognized split settings](user-secrets-split-settings.md) for the
exact supported shape, filters and safe-failure rules.

## Shared stores

All explicitly associated consumers of a selected store participate in compatibility checks.
Compatible generated overrides are merged once per case-insensitive effective
key. Compare complete desired configuration values, not just endpoint hosts:
different ports, credentials, paths or connection options cannot silently win by
project/filesystem discovery order. Key spelling and generated output order are
also deterministic.

Only keys actually needing generated overrides are checked. For each such key,
every consumer that defines it must require the same value after eligible
adaptation. A consumer with an already-adapted `host.docker.internal` value can
agree without generating its own override. A consumer without that key contributes
no competing value, allowing compatible disjoint overrides to merge. Unrelated
appsettings differences are not conflicts.

In particular, adapting `http://localhost:8004/path` must not replace another
consumer's remote endpoint or its unmapped `http://localhost:5000/api` through
the shared store. Such unchanged values still conflict with the generated value.
Null values for the targeted key also cannot be silently replaced.

Selected host keys remain authoritative **before** generation or compatibility
checks, regardless of value, and still receive eligible adaptation. Lower-priority
appsettings differences for those keys are irrelevant. Existing key filters apply
before generation and compatibility; they do not authorize any extra keys.
Unrelated/unresolved projects do not participate or block generation. These checks
cover all explicit matching consumers within the bounded discovery model above,
not consumers inferred through MSBuild, hidden in unreadable project XML or
beneath excluded directories (including `node_modules`). A conflict outside
exclusions still blocks copying **every** selected store before any write.

Conflicts identify the store, escaped effective key and affected project paths,
without values. Resolve them by supplying an intentional authoritative selected
host-secret key, excluding the key, or assigning separate stores; abox never
chooses a winner or splits stores automatically.

## Refresh, safety and failures

Each copy rebuilds the **complete selected destination** from current selected host
secrets plus current eligible generated overrides. Removed/remote/unmapped keys
stop being generated, clearing stale entries. Manual destination-only edits are
not preserved. An ordinary run never performs this refresh.

All selected inputs are parsed before copying. Policy authorization, adaptation,
applicable deny-list checks, shared-store compatibility and destination checks
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
