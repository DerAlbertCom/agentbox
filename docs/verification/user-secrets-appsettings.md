# Issue #13: appsettings endpoint override verification

Verified on Windows, 2026-11-15, against integration baseline `6eaab38`.
Only synthetic temporary workspaces and injected host-store roots were used.
No developer secret stores, live application services, existing sandboxes,
registry publication or global sbx settings were accessed/changed for this work.

## Red/green evidence and seam

`UserSecretsAppSettingsCommandTests` exercises the existing CLI with the existing
`UserSecretsCliHarness` and a substituted `ISbxClient`. No production public test
interface was added. The first tracer failed because a selected project-only
store had no host secrets file; it then passed with the generated JSON override.
Subsequent red/green slices covered development precedence, artifact/link
discovery, unsupported association diagnostics, loopback URL/PostgreSQL formats,
generated-original deny checks, temporary shared-store rejection and unsafe
mapped-local adaptation. Unsafe cases were exercised in both source kinds and
both normal/verbose modes with zero destination writes.

Assertions inspect emitted JSON, unchanged source bytes, value-free diagnostics,
all-store no-write failures and no build process invocation. Synthetic projects
include a build target that would fail if executed, and the command's process
adapter receives no calls in that discovery test. Filesystem link fixtures use
Windows junctions (Unix symlinks on Unix); they cover external escapes, indirect
linked parents, internal aliases and cycles without requiring Windows native
file-symlink privileges.

`UserSecretsLifecycleTests` extends the same command seam to local/published
creation, both explicit replacement kinds and accepted replacement from `run`.
Project-only generation uses current development configuration; invalid project
JSON prevents destructive replacement, and ordinary runs do not discover even
malformed projects. Repeated explicit copying proves stale generated keys vanish.
Existing copy/filter/safety/policy/transport suites remain regression coverage;
historical preservation cases newly required to fail are now at the CLI seam.

## Final checks

| Check | Result |
| --- | --- |
| `dotnet build cli/agentic-box.slnx -c Debug --no-restore --verbosity minimal` | Passed; 0 warnings, 0 errors |
| `dotnet test cli/agentic-box.slnx -c Debug --no-build --verbosity minimal` | 918 passed, 0 failed, 5 platform skips (923 total) |
| `dotnet build cli/agentic-box.slnx -c Release --verbosity minimal` | Passed; 0 warnings, 0 errors |
| `dotnet test cli/agentic-box.slnx -c Release --no-restore --verbosity minimal` | 918 passed, 0 failed, 5 platform skips (923 total) |
| `dotnet csharpier check cli` | Passed |
| `git diff --check` | Passed |

The five existing platform skips are four native-symlink receiver cases lacking
Windows link privileges and one case-distinct host-directory collision case on a
case-insensitive filesystem. Skipped cases are not claimed as passing; new
junction-based discovery cases ran successfully.

This verifies emitted configuration and fail-closed behavior, not service
availability, database authentication, arbitrary application provider ordering or
an independent Unix-host/live sbx run. The existing accepted live-policy matrix
and its [published/nonempty-policy limitation](user-secrets/README.md) are unchanged.
Shared-store generation remains temporarily rejected; Oracle, recognized split
settings and compatible shared-store resolution are separate tickets under #12.

## Review-fix verification against `b3a6672`

The review-fix branch started clean at `b3a6672`, the integration tip. Issue #13's
body and complete comments response were read through `gh api` (no comments were
present). Only #13 behavior and its documentation were changed; no issue/PR writes
or real host-secret/runtime operations were performed.

### Reproductions and decisions

All new behavior tests use the same confirmed CLI/harness seam described above.
Focused commands used `dotnet $sdk10 test cli/agentic-box.slnx --verbosity minimal
--filter FullyQualifiedName~<test-name>` (adding `--no-restore` after restore), with
`$sdk10 = 'C:/Program Files/dotnet/sdk/10.0.404/dotnet.dll'` to select the installed
.NET 10 SDK explicitly without changing repository/global SDK settings.

- **Implicit imports:** `ImplicitWorkspaceImports...` failed both props/targets
  cases because no warning was emitted. Both now pass: SDK projects lacking an ID
  warn about unresolved associations when workspace-local ancestor imports exist,
  without reading their property values or evaluating MSBuild. A separate
  red/green case covers Windows case-variant names using existing host path rules;
  the workspace-parent fixture proves discovery never searches above the root.
- **PostgreSQL URL ambiguity:** five new `KnownMappedButUnsupportedEndpoints...`
  cases reproduced silent preservation of explicit-mapped local host lists and
  query-host URLs. All now fail actionably, without values, in copied/generated
  sources and normal/verbose modes, before any store is written. Thirteen preservation
  fixtures cover unmapped/remote URLs, arbitrary text, local hosts paired with
  unmapped ports, query options overriding authority hosts/ports, and hostless URLs
  without explicit ports. Additional red/green slices check that an overridden
  authority is never adapted or mistaken for a mapped-local effective endpoint,
  and that an explicit mapped query port is not missed. In particular,
  `postgresql:///db?host=localhost` does **not** infer port 5432, even when mapped.
- **Unreadable project XML:** malformed, DTD-bearing and exclusively locked
  unrelated projects reproduced the copy-only regression. The six paired
  `IncompleteProjectDiscovery...` cases now allow validated host-only copies with
  a warning, but reject generation while consumers are unknown. This deliberately
  conservative restriction prevents an unreadable project from hiding another
  literal shared-store consumer. No partial XML/string matching guesses an ID.
  Separate empty-filter/force cases prove selected host JSON and associated
  appsettings are still validated and fail with zero writes. Missing host stores
  still require a real, successfully associated appsettings source. Directory
  traversal failures remain fatal. Two file-link fixtures are explicit platform
  skips here because Windows cannot create native file symlinks; they are not
  claimed as passing.
- **Warning scope:** three `UnsupportedButDefinitelyUnselectedLiteralIds...`
  fixtures reproduced unnecessary warnings for conditional/multiple/invalid
  unselected literals. They now pass without warnings. Computed or imported IDs
  whose relationship to the selection is unknowable still warn intentionally:
  suppressing those would contradict the unresolved-association requirement.
- **Alleged generated-key collision — rejected with provider evidence:**
  `GeneratedParentScalarsAndCopiedChildKeys...` passed against the existing emitted
  shape using the actual `Microsoft.Extensions.Configuration.Json` 10.0.14
  provider (test-only dependency). Host `{"Api":{"Key":"synthetic-only"}}` plus
  generated `Api` emits
  `{"Api":{"Key":"synthetic-only"},"Api":"http://host.docker.internal:5000"}`.
  The provider loads exactly two distinct effective keys: `Api:Key` retains the
  copied value and `Api` contains the adapted endpoint. A duplicate property name
  at different effective-key paths is not a .NET key collision. No new rejection
  or precedence change was introduced; host bytes remain unchanged.

### Proportional standards cleanup

Project discovery now delegates association and appsettings merging to coherent
private helpers. Project metadata lives on the prepared operation, not on generic
JSON snapshots: this removes the redundant snapshot clone and the
`UserSecretSnapshot`/`ProjectSettings` mutual dependency. `EffectiveLeaves`,
`HasAppSettingsSource` and `fullyParsedAndUnique` state what their data means. The
shared ambiguity predicate and value-free setting description remove repeated
logic/diagnostic suffixes. The internal prepared constructor requires explicit
force state. The public optional console argument is intentionally preserved for
existing non-CLI callers; CLI composition still supplies diagnostics. No
speculative setting-context, port or path wrapper types were added. The code map
now includes bounded project/appsettings discovery and generation, and affected
list-continuation indentation is consistent.

### Initial review-fix checks (`02580e4`)

| Check | Result |
| --- | --- |
| `dotnet $sdk10 build cli/agentic-box.slnx -c Debug --no-restore --verbosity minimal` | Passed; 0 warnings, 0 errors |
| `dotnet $sdk10 test cli/agentic-box.slnx -c Debug --no-build --verbosity minimal` | 953 passed, 0 failed, 7 platform skips (960 total) |
| `dotnet $sdk10 build cli/agentic-box.slnx -c Release --no-restore --verbosity minimal` | Passed; 0 warnings, 0 errors |
| `dotnet $sdk10 test cli/agentic-box.slnx -c Release --no-build --verbosity minimal` | 953 passed, 0 failed, 7 platform skips (960 total) |
| `dotnet csharpier check cli` | Passed |
| `git diff --check` | Passed |

The seven skips comprise the five existing platform skips described above plus
the two new native file-symlink discovery cases. Malformed/DTD/locked-file,
workspace-directory-junction and cycle cases ran. The service-readiness,
independent Unix/live sbx, shared-store and sibling-feature limitations above
remain unchanged.

## Residual independent-review fixes against `02580e4`

The same clean review-fix worktree was continued after confirming and merging the
integration tip at `02580e4` (already up to date). Issue #13 and its still-empty
comments response were reread through `gh api`. The existing confirmed CLI seam,
synthetic roots and SDK 10.0.404 commands above were reused. No issue/PR writes,
developer stores, live sandbox operations or MSBuild evaluation were involved.

### Red/green evidence and safety decisions

- `PotentialSelectedConsumersBlockGenerationButPermitAuthoritativeCopies` first
  produced **6 failures and 6 passes**. Each generated case improperly succeeded
  after warning about a possibly selected Worker consumer: implicit props,
  implicit targets, computed ID, explicit import, selected conditional ID and
  multiple IDs. The authoritative-copy cases already passed. All twelve now pass
  in both normal and verbose modes, with force unable to bypass the generation
  restriction. Unresolved associations join unreadable projects in the prepared
  operation's unknown-consumer inventory. Their appsettings are never guessed or
  read; generated candidates fail with zero writes for every selected store.
  Diagnostics distinguish unreadable XML from unresolved associations and omit
  imported/computed values, endpoint paths, credentials and query tokens. Sources
  are unchanged and the substituted process runner receives no calls.
- Import handling deliberately remains **presence-only and workspace-bounded**.
  A no-ID SDK project beneath a workspace import may inherit a selected ID, even
  if it appears to be a library. Without reading/evaluating imported property
  values, it is unsafe to declare it a non-consumer. Such projects conservatively
  block generation, not authoritative host-only copying. Separate passing fixtures
  prove an unconditional unselected literal consumer still permits generation
  despite the same implicit props, and that definitely unselected literals or
  projects with no association evidence neither warn nor prevent generation.
  The existing above-workspace and no-build tests remain passing coverage.
- Adding `hostaddr` URI fixtures to the two endpoint theories first reproduced
  **8 failures**: six missed mapped-loopback addresses and two cases incorrectly
  adapting/rejecting a local host name whose actual connection address was remote.
  [PostgreSQL's connection URI and `hostaddr` documentation](https://www.postgresql.org/docs/current/libpq-connect.html#LIBPQ-CONNECT-HOSTADDR)
  was checked directly: named URI options accept `hostaddr`; it determines the
  network address while `host` can remain the TLS/authentication name. IPv4,
  expanded/percent-encoded IPv6, empty-address fallback and unrelated SSL/options
  parameters are now covered. Known explicitly mapped local addresses fail
  actionably in copied/generated sources, normal/verbose modes, before any write.
  Remote addresses, unmapped explicit ports and hostless addresses without a
  known explicit port are preserved; no port default or auth-name rewrite is
  inferred. All source files, including host stores, are compared byte-for-byte.
- Two additional positional-list fixtures went red when a mapped remote port was
  incorrectly paired with a local address at an unmapped port. They now pass:
  matching host/address and port lists are paired by index, and a single explicit
  port applies to every host. The converse local-mapped pair still fails safely.
  The endpoint theories pass **61 cases** after these additions. They exercise
  existing preservation as well as `hostaddr`; they do not claim live libpq
  connections or database availability.

`ResolveSelectedStore`, the named `cannotAffectSelection` guard, and
`IsMultiHostOrQueryPostgresUrl` make the private logic's role explicit without new
wrapper types. The implementation map now correctly states that
`PreparedUserSecrets`, not individual JSON snapshots, owns project originals and
association metadata. Discovery/lifecycle/endpoint docs describe the stronger
unknown-consumer restriction and `hostaddr` validation semantics.

### Final checks after residual fixes

| Check | Result |
| --- | --- |
| Focused appsettings/lifecycle CLI tests | 213 passed, 0 failed, 2 explicit file-symlink skips (215 total) |
| `dotnet $sdk10 build cli/agentic-box.slnx -c Debug --no-restore --verbosity minimal` | Passed; 0 warnings, 0 errors |
| `dotnet $sdk10 test cli/agentic-box.slnx -c Debug --no-build --verbosity minimal` | 988 passed, 0 failed, 7 platform skips (995 total) |
| `dotnet $sdk10 build cli/agentic-box.slnx -c Release --no-restore --verbosity minimal` | Passed; 0 warnings, 0 errors |
| `dotnet $sdk10 test cli/agentic-box.slnx -c Release --no-build --verbosity minimal` | 988 passed, 0 failed, 7 platform skips (995 total) |
| `dotnet csharpier check cli` | Passed |
| `git diff --check` | Passed |

The same seven explicit platform skips and live-service/Unix/shared-store/sibling
limitations above remain; this continuation introduces no additional skips.
