# User-secret endpoint rewriting

`abox user-secrets` and automatic copying use the same prepared, selected store
snapshots, including [appsettings-derived overrides](user-secrets-appsettings.md).
Original selected host scalar values are checked against the global deny
list **before** sandbox lookup or rewriting; originals that would generate
appsettings overrides are checked during all-store target preparation. `--force` bypasses only that deny
list, not invalid stores, unavailable policy metadata, or destination validation.
Host stores are never changed. Nothing is added to a build input, image, kit, or
published artifact; `abox secrets` is unchanged.

## Runtime policy is the inventory

The selected sandbox's current runtime policy is the **sole source of truth**.
No local kit sources, build records or artifact lookup are used, including for a
published squad:

```sh
sbx policy ls <sandbox> --type network --json
sbx policy check network --sandbox <sandbox> --protocol tcp --json localhost:<port>
```

Two supported rule shapes in that same runtime inventory supply candidates:

- Active, noneditable, sandbox-scoped/provisioned kit network allow rules with
  `net:connect:tcp` and exact `localhost:<integer 1..65535>` resources. Their name,
  sandbox identity, scope/application, origin, layer and provenance must match the
  supported runtime schema. Other resources in a kit rule supply no candidates.
- Active, editable local rules explicitly added to **that sandbox**, with
  `provenance: {created_via: added}`, matching rule ID/name and policy ID in the
  observed sbx v0.51.0 UUID format. Every resource must be an exact
  `localhost:<integer 1..65535>` and the sole action must be `net:connect:tcp`.
  Scope, application and sandbox ID must all match; origin must be `scoped` and
  layer `local`. Only the observed persistent-rule fields are supported: unknown
  fields/provenance, including lifetime or conditional constraints, exclude the
  manual rule rather than guessing their meaning. Inactive/expired rules do not
  declare candidates.

Provisioned-rule provenance identifies metadata already carried by the sandbox;
it is not a lookup of a kit, source file, image or registry. Adding the missing
declarations to an existing sandbox does not require the original kit to exist.

Ports from both sources are deduplicated and checked in numeric order. A valid
effective check must describe the same sandbox, TCP action and target; a deny
excludes that port even when an explicit declaration allows it.
Missing, malformed or unavailable required metadata fails before any store write.
An empty inventory is valid. Global, wildcard, all-port, remote, other-sandbox and
unrecognized rules never supply rewrite intent; effective `allowed: true` alone
is insufficient. These discovery commands do not change policy.

To intentionally map a missing host service without kit sources or sandbox
recreation, the user can add a narrowly scoped declaration:

```sh
sbx policy allow network --sandbox <sandbox> --protocol tcp localhost:1521
```

This command **does** add a network grant for that sandbox/port. abox never runs it
automatically. Rerun the explicit copy operation when ready to refresh overrides;
an ordinary sandbox run still does not copy secrets. No kit install, build or
restart is required. This later user clarification extends the original kit-only
rewrite-intent contract; it does not treat arbitrary network permission as a map.

For supported URL/PostgreSQL/Oracle endpoint fields, `localhost` (case-insensitive),
dotted IPv4 loopback and IPv6 loopback change to `host.docker.internal`, retaining
the **same published host port**, and only when that port is inventoried and currently
authorized. Policy checks do not prove service availability and cannot prevent a
later policy change. No global allow rule is created or broadened.

Examples for a kit declaring host ports 8004 and 8006:

| Original | Copied |
| --- | --- |
| `Server=localhost,8004;Database=dev` | `Server=host.docker.internal,8004;Database=dev` |
| `http://127.0.0.1:8006/path?q=localhost:8004` | `http://host.docker.internal:8006/path?q=localhost:8004` |
| `Host=localhost;Port=8004;Password=unchanged` | `Host=host.docker.internal;Port=8004;Password=unchanged` |
| `https://sql.internal:8004` | unchanged (retains TLS/SNI identity) |
| `Server=host.docker.internal,8004` | unchanged |
| `Server=localhost,5432` | unchanged, even if a host service uses `8004:5432` |

There is **no** container-port translation, service/hostname inference,
first-declared-name selection, experimental `host-port@1` relay, staged kit
descriptor or `/etc/hosts` lookup. `sbx inspect` is not a port inventory and
`sbx ports` describes the opposite direction; neither supplies rewrite intent.

## Supported forms and preservation

Rewriting operates on whole JSON string values, including nested objects/arrays,
not arbitrary substring occurrences:

- Absolute hierarchical URIs with a loopback authority host, for example HTTP(S),
  PostgreSQL or `tcp://`. An explicit decimal port or the URI scheme's standard
  default port must be mapped. Optional
  userinfo is retained. Only the authority host span changes; scheme casing,
  credentials, port spelling, paths, escapes, query strings and fragments do not.
- SQL Server comma-port endpoint fields `Server`, `Data Source`, `Address`, `Addr`
  and `Network Address` (case-insensitive), optionally prefixed with `tcp:`.
- A separate `Port` field with a loopback `Host` (PostgreSQL-style, including bare
  or bracketed IPv6) or `Server` (MySQL-style) field. A PostgreSQL `Host` without
  `Port` uses the standard 5432 default only if that host port is mapped.
- PostgreSQL single `Host=localhost:8004` / `Host=[::1]:8004` inline ports are
  supported without a separate `Port` field. Mixed inline/separate port sources
  for a mapped-local inline endpoint are rejected as ambiguous.
- Connection-string field order, spacing, single/double quoting and doubled quote
  escapes in other fields are preserved. Semicolons inside quoted values do not
  split fields. Credentials and unrelated fields are never recursively rewritten.
- The explicit structured `Database_Config:Host` + `Database_Config:Port` configuration
  pair resolves base/development/selected secrets before eligibility and changes
  only the Host key. This is not generic pairing of neighboring properties. See
  [recognized split settings](user-secrets-split-settings.md).

### Oracle Easy Connect and descriptors (#15)

Oracle endpoints use the same flow from either appsettings or selected host
secrets, not a separate copy mechanism or a restriction to `ConnectionStrings`:

- Easy Connect: `[//]host[:port][/service[:server][/instance]][?options]`, with
  optional `tcp://` or `tcps://`, decimal explicit ports and bracketed IPv6.
  The Oracle default 1521 is eligible only when that same host port is mapped;
  a bare name without a prefix, port or service remains an unresolved TNS alias.
- Easy Connect Plus comma-separated hosts, shared trailing ports
  (`localhost,remote.test:1521/service`), per-address ports, and semicolon-separated
  address lists. Each host's port is resolved within its own group before testing
  locality. A remote host's mapped port cannot implicate an explicitly unmapped
  local host. List order, separators, port spelling, service/server/instance
  identifiers and query options (including quoted scalar values) are retained.
- Structural `(DESCRIPTION=...)`, `(DESCRIPTION_LIST=...)`, `(ADDRESS=...)` and
  `(ADDRESS_LIST=...)` descriptors. Only direct `HOST` fields in TCP/TCPS `ADDRESS`
  entries at an explicitly mapped `PORT` change. Descriptor IPv6 hosts can be bare
  or bracketed. Address/description lists can mix eligible, remote and unmapped
  destinations; only eligible hosts change. Service/SID identifiers, credentials,
  structure, casing, spacing, quotes and all unrelated options remain intact.
  A local `ADDRESS` missing `PORT` is rejected when host port 1521 is mapped:
  supply an explicit decimal `PORT` rather than relying on protocol-dependent
  descriptor defaults. No port is inserted or inferred. Remote addresses and
  missing-port local addresses when 1521 is unmapped remain unchanged.
- Either Oracle form inside an Oracle connection string's `Data Source` field,
  including quoted values with doubled outer quote escapes. Other fields such as
  `User Id`, `Password` and pooling options are untouched. Endpoint-shaped text
  inside credentials or prose is not recursively inspected.

Known mapped-local Oracle endpoints fail safely rather than guessing when syntax
is malformed/ambiguous: duplicate outer fields, competing host/port fields,
unfinished quoting, unbalanced descriptors, duplicate address host/port/protocol,
unsupported address protocols, unbracketed Easy Connect IPv6, or proxy/endpoint
options that make a direct host substitution unsafe. Descriptor parsing is bounded
to 64 nested levels. TNS alias resolution, JDBC/SQL*Plus login syntax and arbitrary
Oracle Net files are not evaluated. Remote/unmapped values are preserved rather
than rejected merely for mentioning localhost. Existing generic TCP URI
userinfo/escaped-path/fragment support remains with the URL dispatcher.
Bare whitespace prose such as `localhost:1521 is the local db` is not an Easy
Connect authority and remains unchanged. Explicit `Data Source=`/Oracle-prefix
syntax and endpoint-shaped service suffixes still receive unsafe-format checks.

Configuration tests require no live Oracle instance and do not establish
authentication/query readiness. See [synthetic Oracle verification](verification/user-secrets-oracle.md).

Recognized mapped-local endpoints that cannot be adapted unambiguously now fail
before **any** destination write: malformed mapped-local URLs, multiple connection
hosts/ports, duplicate fields and recognized endpoints with malformed quoting or
unsupported brace syntax. Diagnostics name the store/key and, for generated
values, project, but never include the value. `--force` does not bypass this.
Named endpoints, undeclared ports and arbitrary strings always stay unchanged;
merely containing `localhost` is not an error. Host aliases, nonliteral/shorthand
loopback spellings, SQL named instances, arbitrary split host/port properties,
embedded prose/JSON and relative URLs are outside the supported shapes.
This is deliberately not a generic connection-string normalizer. JSON whitespace,
comments and escape spelling are not a preservation contract; transfer serializes
the selected JSON tree and excludes skipped comments. Unchanged string contents
remain unchanged.

Every target snapshot and every destination is prepared before the first write.
The originals remain in memory for deny/force accounting; reusing a prepared copy
does not reuse rewritten values or cache authorization. Reports contain only store
IDs, effective key names and copied/rewritten counts, never secret values. Policy
JSON is captured for validation with raw child diagnostics suppressed even under
`--verbose`; payload transport remains the discard-output sensitive stdin path.
Per-store atomic replacement and retry/cancellation limitations are unchanged.

## Verification

The accepted live matrix is **local/nonempty kit inventory + published/empty kit
inventory**. The accessible already-published squad predates host-access policy.
Published/nonempty runtime rewriting has **not** been demonstrated and is an
explicitly accepted limitation, not a claim of passing coverage. Do not publish a
new artifact or change global policy merely to fill that gap. See the
[verification evidence and sanitized fixtures](verification/user-secrets/README.md).
The later [sandbox-scoped policy verification](verification/user-secrets-scoped-policy.md)
also exercises production discovery of explicit manual declarations on an existing
sandbox, without copying secrets or claiming live service readiness.

Automated tests exercise the production shared-copy and sbx/process seams with
disposable synthetic host files. For the integrated #12 story's Debug/Release
results and runner caveats, see the [combined verification record](verification/user-secrets-endpoint-story.md).
On the current Windows host, select SDK 10 explicitly for the build and invoke
the xUnit executable directly:

```powershell
$sdk10 = 'C:/Program Files/dotnet/sdk/10.0.404/dotnet.dll'
dotnet $sdk10 build cli/agentic-box.slnx -c Debug --no-restore -m:1 --nologo --verbosity minimal
dotnet cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll -class 'Abox.Tests.UserSecrets*' -class Abox.Tests.SplitSettingsCommandTests -noLogo -maxThreads 2
```

The synthetic-only `RewriteProbe` below can exercise production discovery/copy in
a fresh disposable sandbox. It rejects names outside its own `abox-issue8-<8 hex>`
namespace, creates its own host files, and checks copied values inside the sandbox
without printing them. Never pass an existing user sandbox or a real secret store.

```powershell
$name = 'abox-issue8-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$saved = @{}
foreach ($key in 'SBX_KIT_BUILDER', 'BUILDX_BUILDER', 'DOCKER_CONTEXT') {
    $saved[$key] = [Environment]::GetEnvironmentVariable($key)
}
$created = $false
try {
    $env:SBX_KIT_BUILDER = 'host'
    $env:BUILDX_BUILDER = 'desktop-linux'
    $env:DOCKER_CONTEXT = 'desktop-linux'
    sbx --no-input create ./docs/verification/user-secrets/workload --kit ./kits/host-access-cape --name $name --cpus 2 --memory 2g --pull never --static-mcp none --no-progress
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic sandbox creation failed' }
    $created = $true
    dotnet run --project docs/verification/user-secrets/RewriteProbe -c Release -- $name nonempty
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic rewrite smoke failed' }
} finally {
    if ($created) { sbx rm $name --force }
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
}
```

For published/empty verification choose another fresh name, replace only the
create source with the existing digest-pinned reference below, **omit `--kit`**,
use `--pull always`, and pass `empty` to `RewriteProbe`. No host kit sources,
publication, registry writes, runtime upgrades or persistent settings are required.

```text
rwest.jfrog.io/mtiae-docker-release/agentic-box-rwest-mti-ca/ac@sha256:ae9c3211420760e6581ff3fdbcd3357cdd59761d1ad2d352ab0bba3adf81d954
```

Verified 2026-11-15 with sbx v0.51.0 in task-owned
`abox-issue8-8648cd68` / `abox-issue8-f965ae54` (local/nonempty) and
`abox-issue8-b8d658ce` / `abox-issue8-b405fad7` (digest-pinned published/empty).
The second pair included the integrated filtering/lifecycle/security fixes and
checked rewritten-key reporting. The production `RewriteProbe` passed exact
inventory/effective-authorization checks, original-value denial, expected copied
bytes, SQL/URI rewriting or empty-inventory preservation, named/unmapped endpoint
preservation, unchanged host bytes and application-user 0700/0600 modes in both.
Verbose was enabled; no values or raw child diagnostics were printed. Both
sandboxes were removed in `finally`. No actual host secret stores, existing user
sandboxes, publications or global settings were touched. This proves metadata and
copy behavior, not service connectivity or an untested published/nonempty policy.

Earlier #8 verification used **Debug tests only**, plus a Release solution build
and Debug probe build. The focused rewrite/policy/CLI suite passed **101 tests**
with no skips. Those builds had zero warnings/errors; CSharpier and
`git diff --check` passed. No Release test run was performed for #8.

The earlier #8 post-review integrated run, `dotnet test cli/agentic-box.slnx -c Debug`,
passed **756 tests**, with **4 explicit native-symlink privilege skips** (760 total),
zero failures. The focused user-secrets/create/run regression set passed **366
tests**, with the same **4 skips** (370 total). The Debug solution build had zero
warnings/errors; `dotnet csharpier check cli` and `git diff --check` passed. No
Release build or test run, live sandbox changes, publication or global setting
changes were performed for this final-review change. Native Windows symlink
regression tests explicitly skip when this host lacks link-creation privileges;
they are not reported as passing.
