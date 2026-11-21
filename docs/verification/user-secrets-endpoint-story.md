# Story #12 / PR #19: combined review fixes and final verification

Verified on Windows, 2026-11-15, in
`D:/dev/rwe/agentic-box/.worktrees/issue-12-integration`, branch
`integration/issue-12-endpoint-overrides`.

- Integration baseline: `a0901be5e9e594b554a444d137f99868f963f409` (all four
  implementation tickets integrated).
- Behavior fixes: `93e6451` (`fix: distinguish Oracle defaults and endpoint-shaped prose`).
- Verified code/test tip: `59ce8e8e2abad3864e1d104ab6957efcb09c60dc`
  (`refactor: consolidate endpoint override preparation`). The subsequent
  verification/documentation commit changes no production code or tests.
- Read repository instructions, current domain/implementation and user-secret
  documentation, and full #12/#14/#15/#16 issue bodies and empty comments via
  read-only `gh api`. No issue/PR writes were made.

The agreed seam remains the existing CLI with temporary synthetic project/store
files and `UserSecretsCliHarness`'s substituted `ISbxClient`. No real developer
stores, existing sandboxes, databases or other live services were accessed.

## Spec review: reproduced before fixing

All commands below ran from the integration worktree. Builds explicitly use
installed SDK **10.0.404**, rather than ambient **11.0.100**:

```powershell
$sdk10 = 'C:/Program Files/dotnet/sdk/10.0.404/dotnet.dll'
dotnet $sdk10 build abox-cli/agentic-box.slnx -c Debug --no-restore -m:1 --nologo --verbosity minimal
dotnet abox-cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll -method '*UnsafeMappedOracleEndpointsFailBeforeAnyWriteWithoutLeakingValues' -noLogo -maxThreads 2
dotnet abox-cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll -method '*WhitespaceProseIsNotAnOracleEndpoint' -noLogo -maxThreads 2
```

Each regression was added and run separately before its implementation change,
with a rebuild between edits:

1. **Missing descriptor `PORT`:** the new case
   `(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost))(CONNECT_DATA=(SERVICE_NAME=XE)))`
   failed: the command returned **0**, copied the earlier valid store and copied
   the unchanged descriptor, instead of returning **1** with zero writes. The
   filtered suite was **20 passed, 1 failed**. The descriptor eligibility check
   previously ignored addresses without an explicit mapped `PORT`.
   After the fix the same suite was **21 passed, zero failures**.
2. **Whitespace prose:** `localhost:1521 is the local db`, including a variant
   followed by `/synthetic-service`, incorrectly returned **1** with an
   unsupported-endpoint error. The unmapped `localhost:8080 (worker)` control
   passed: **1 passed, 2 failed**. Partial Easy Connect host matching was treating
   a bare prose authority as a recognized malformed endpoint. After tightening
   recognition, the Oracle command suite passed **70 cases**, then **79 cases**
   after adding the surrounding preservation/fail-closed coverage.

### Chosen contracts

- **Fail safely, do not infer descriptor defaults.** A local descriptor `ADDRESS`
  missing `PORT` is rejected when host port **1521 is mapped**. The user must supply
  an explicit decimal `PORT`; abox does not insert a port or guess protocol-specific
  defaults. This is intentionally conservative, including TCPS/unsupported
  protocols, not a claim that every Oracle transport defaults to 1521. Remote
  addresses and missing-port local addresses when 1521 is unmapped remain
  unchanged. Easy Connect's existing mapped-default behavior is unchanged.
- Tests exercise both appsettings-generated and authoritative selected-secret
  sources, quoted/doubled-quote descriptors, compound address lists, TCP/TCPS and
  unsupported protocols, unmapped/remote preservation, source-byte immutability,
  and normal/verbose diagnostics. Unsafe cases include an earlier writable store
  and `--force`, and assert **zero destination writes across all stores**.
- Bare whitespace prose is preserved and produces no generated override. Explicit
  `Data Source=`/Oracle-prefix forms and malformed endpoint service suffixes
  remain fail-closed. Existing generic TCP URI dispatch is retained.
- **Missing-key shared consumer: no policy change.** A consumer without the key
  contributes no competing effective value; it can receive the merged shared
  override. `CompatibleDisjointKeysAndAlreadyAdaptedConsumersMergeWithoutUnrelatedConflicts`
  includes a no-appsettings consumer and disjoint keys, asserts the exact shared
  payload, and passed in **both discovery orders**. The current
  [user contract](../user-secrets-appsettings.md#shared-stores) already explains
  this explicitly. Null/remote/unmapped values are present values and remain
  competing consumers, not missing keys.

## Standards review decisions

| Finding | Resolution |
| --- | --- |
| A: stale lifecycle wording | Replaced temporary shared-store checks/limitations with current all-consumer compatibility/conflict rules. Historical #13 verification remains unchanged. |
| B: duplicate project-override loops | Both selected split-host consumers and generated appsettings candidates use `BuildProjectOverrides` for effective resolution, adaptation and generated maps. Selected host originals are already deny-checked in preparation; every generated appsettings original is still checked with its project and force accounting before merging, even when targets agree. |
| C: split-setting knowledge/threading | The explicit pair remains local to the split-settings adapter. Selected targets are prepared once; recursive JSON writing receives final targets, not ports, store identity or split-setting metadata. No generic pair registry or inference of arbitrary sibling properties was added: the spec supports one explicit pair, so that extra abstraction would be speculative. |
| D: Oracle parameter/result clump | `CollectOracleHosts`, `CollectOracleSourceHosts` and `CollectOracleEasyConnectHosts` return a small `OracleHostResult` naming recognition, validity and original host spans. This removes mutable replacement-list/out-valid threading and makes the old boolean recognition contract explicit. No general parser framework was introduced. |
| E: repeated parsing/projections | One strict invariant decimal port parser (1..65535) is shared by Oracle, split settings and existing URL/database handlers. Descriptor protocol text is computed once; pending Easy Connect hosts contain only local spans, eliminating repeated filtering/projection. |
| F: double parse and ambiguous names | JSON validation/flattening is separate from key selection. Merged appsettings is parsed once and filtered from that validated snapshot; `SelectedSettings` versus `AllSettings` distinguishes emitted candidates from metadata. Duplicate-key, malformed-file, case-insensitive, array and filter coverage remains in the command suites. |

## Final integrated verification

The required matrix used the direct xUnit executable, **not `dotnet test`**.
Earlier verification records document MTP runner/handshake failures; no
`dotnet test` success is inferred from or claimed by these direct-executable
results. Runner configuration was not changed. Exact accepted commands:

```powershell
$sdk10 = 'C:/Program Files/dotnet/sdk/10.0.404/dotnet.dll'

# Focused Debug verification after consolidation.
dotnet $sdk10 build abox-cli/agentic-box.slnx -c Debug --no-restore -m:1 --nologo --verbosity minimal
dotnet abox-cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll -class 'Abox.Tests.UserSecrets*' -class Abox.Tests.SplitSettingsCommandTests -class Abox.Tests.CreateCommandTests -class Abox.Tests.RunCommandTests -noLogo -maxThreads 2
dotnet abox-cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll -method '*CompatibleDisjointKeysAndAlreadyAdaptedConsumersMergeWithoutUnrelatedConflicts' -noLogo -maxThreads 2

# Full integrated Debug, then Release: serial builds and full direct runs.
dotnet $sdk10 build abox-cli/agentic-box.slnx -c Debug --no-restore -m:1 --nologo --verbosity minimal
dotnet abox-cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll -noLogo -maxThreads 2
dotnet $sdk10 build abox-cli/agentic-box.slnx -c Release --no-restore -m:1 --nologo --verbosity minimal
dotnet abox-cli/test/Abox.Tests/bin/Release/net10.0/Abox.Tests.dll -noLogo -maxThreads 2
dotnet csharpier check abox-cli
git diff --check
```

| Check | Result |
| --- | --- |
| Oracle command suite after behavior fixes | 79 passed, 0 failed, 0 skipped |
| Focused Debug user-secrets/split/create/run suite after consolidation | 685 passed, 0 failed, 7 skipped (692 total) |
| Explicit shared missing-key/disjoint contract check | 2 passed, 0 failed, 0 skipped |
| Final serial Debug solution build | 0 warnings, 0 errors |
| Full Debug executable | **1168 passed, 0 failed, 7 skipped (1175 total)** |
| Final serial Release solution build | 0 warnings, 0 errors |
| Full Release executable | **1168 passed, 0 failed, 7 skipped (1175 total)** |
| CSharpier | Passed, 168 files checked |
| `git diff --check` | Passed |

The seven skips in each configuration are **not passes**: four native-symlink
receiver cases, two native-file-symlink discovery cases (this host lacks the
required creation privileges), and one case-distinct host-directory collision
case unsupported by this Windows filesystem.

The main worktree's pre-existing modified `run-abox.ps1` was not edited; its SHA256
remained `F303DBA9D64E8B51D6BE9CC7F4F4ECCE59C0D1DD97A4CE71E292F71557E0D967`.
No live-readiness, authentication/query, independent Unix-host sbx, publication
or new published/nonempty-policy claim follows from these synthetic tests.
Existing [live policy/transport limits](user-secrets/README.md) remain unchanged.
