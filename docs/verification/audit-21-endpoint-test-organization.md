# Issue #35: endpoint-generation test organization

## Scope and compatibility

This is the endpoint-generation organization/phase-marker batch of #21, based on
`integration/audit-21` at `3f18ac16f7d9b28b53ba55247ff19682b3853be2`, including
the endpoint-adaptation extraction (#23) and command organization (#32).
Both #35 and #21 bodies and their empty comment lists were read through `gh api`.

- Command-level rewrite/reporting, appsettings discovery/generation, Oracle,
  split-setting and shared-store suites now live in `Features/UserSecrets/`.
  The existing endpoint command regression moves there too, without content changes.
- The shared-copy rewrite suite lives in `UserSecrets/`, beside its production
  shared-feature ownership. Its specialized private fixture stays with the suite.
- Existing `Abox.Tests` namespaces, classes, method names and theory inputs remain
  unchanged. The shared `UserSecretsCliHarness` remains compatible with its other
  users; its organization and selection/lifecycle tests belong to sibling #34.
- Explicit Arrange/Act/Assert markers separate setup, calls and checks, including
  repeated copy/refresh phases. Named CLI exit results only separate invocation from
  the existing exit assertions. No assertions, helpers, skip reasons, supported
  endpoint forms, authorization or production behavior changed.
- Existing composed create/replacement/on-demand coverage remains in place. No
  parser/private-helper seam or test-only production API was added.

## Discovery comparison

Captured the direct executable's `-list full/json -preEnumerateTheories` output
before editing and after rebuilding. The complete JSON is identical: **1,564
discovered cases before and after**, with the same IDs, classes, methods, display
names/theory arguments and explicit flags; no additions, removals or duplicates.
The seven affected classes account for 362 cases across 79 test methods.

For a reproducible fingerprint independent of the assembly's worktree path, remove
the `Assembly` property from each discovery entry, serialize the resulting list
with Python `json.dumps(..., sort_keys=True, separators=(',', ':'), ensure_ascii=True)`
and hash its UTF-8 bytes with SHA-256. Before and after both yield:

```text
9e0633956bc373a3703f9335a3a5daf2ab7103fbbc636b0d5c93f05aca1e40a2
```

An additional source comparison inlined the new named CLI results and removed
phase comments/whitespace: all six edited suites match their originals, including
assertions, theory data and fixtures. The existing endpoint regression is an exact
content-preserving move.

## Original implementation verification

Used the existing executable xUnit runner, not the incompatible `dotnet test`
handshake on this host. Commands run from the ticket worktree:

```powershell
$sdk10 = 'C:/Program Files/dotnet/sdk/10.0.404/dotnet.dll'
$tests = 'abox-cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll'
dotnet $sdk10 build abox-cli/agentic-box.slnx -c Debug --no-restore -m:1 --nologo --verbosity minimal
dotnet $tests -list full/json -preEnumerateTheories
dotnet $tests -class Abox.Tests.UserSecretsRewriteTests -class Abox.Tests.UserSecretsRewriteCommandTests -class Abox.Tests.UserSecretsAppSettingsCommandTests -class Abox.Tests.UserSecretsOracleCommandTests -class Abox.Tests.SplitSettingsCommandTests -class Abox.Tests.UserSecretsSharedStoreCommandTests -class Abox.Tests.UserSecretsEndpointCommandTests -noLogo -maxThreads 2
dotnet $tests -noLogo -maxThreads 2
dotnet csharpier check abox-cli
git diff --check
```

| Check | Result |
| --- | --- |
| Debug solution build | Passed; zero warnings/errors |
| Focused endpoint/generation suites, before and after | 360 passed, 2 skipped, zero failures (362 total) |
| Full Debug executable | 1,557 passed, 7 skipped, zero failures (1,564 total) |
| Full pre-enumerated discovery comparison | Exact equality, including identities/theory rows |
| CSharpier and whitespace check | Passed |

The focused skips are the existing two file-symlink discovery cases requiring
native link privileges. The full suite additionally skips four receiver-symlink
cases for unavailable host link privileges and one case-distinct host-directory
collision fixture unsupported by this filesystem. None is claimed as passing.

All verification uses synthetic files and fake tool adapters. No live sandbox,
real host-secret store, registry operation, issue/PR mutation or user launcher
change was performed. No Release test run is claimed for this bounded batch.

## Integrated verification rerun

Resumed from clean ticket HEAD `1019e63e2ceb00a6c1b20b7914ba7064b3dfe9a1`
and reread both issue bodies and their empty comment lists with `gh issue view`.
Merged `integration/audit-21` at `293c687cf64b89c3eef8f832712fa199d37af5b6`
into this ticket branch, producing `0522a7a46fa6416e7b1a1cad319bf118de6bb3cc`.
The merge was conflict-free and did not change any #35 suite. No sibling ticket
branch or integration worktree was edited. No leftover test process belonged to
this worktree; existing unrelated processes were left alone.

The earlier post-`dca4904` full run timed out without a completed result and is
not counted as a pass. The following completed run supersedes that incomplete
attempt. The full executable was allowed a 30-minute timeout and completed in
59.876 seconds; focused execution completed in 9.196 seconds.

```powershell
$sdk10 = 'C:/Program Files/dotnet/sdk/10.0.404/dotnet.dll'
$tests = './abox-cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.exe'
$artifacts = 'C:/Users/UI520628/AppData/Local/Temp/11/opencode'
dotnet $sdk10 build abox-cli/agentic-box.slnx -c Debug --no-restore -m:1 --nologo --verbosity minimal
# Capture discovery stdout as raw bytes, avoiding shell code-page decoding.
python -c "import subprocess; subprocess.run(['$tests', '-list', 'full/json', '-preEnumerateTheories', '-noColor'], stdout=open('$artifacts/ticket-35-integrated-discovery.json', 'wb'), check=True)"
& $tests -class Abox.Tests.UserSecretsRewriteTests -class Abox.Tests.UserSecretsRewriteCommandTests -class Abox.Tests.UserSecretsAppSettingsCommandTests -class Abox.Tests.UserSecretsOracleCommandTests -class Abox.Tests.SplitSettingsCommandTests -class Abox.Tests.UserSecretsSharedStoreCommandTests -class Abox.Tests.UserSecretsEndpointCommandTests -noLogo -noColor -maxThreads 2 -result-xml "$artifacts/ticket-35-integrated-focused.xml"
& $tests -noLogo -noColor -maxThreads 2 -longRunning 120 -result-xml "$artifacts/ticket-35-integrated-full-debug.xml"
dotnet csharpier check abox-cli
git diff --check
git diff 293c687 --check
```

| Check | Integrated result |
| --- | --- |
| Debug solution build | Passed; zero warnings/errors |
| Focused endpoint/generation suites | 360 passed, 2 skipped, zero failures/errors/not-run (362 total) |
| Full Debug executable | 1,646 passed, 7 skipped, zero failures/errors/not-run (1,653 total) |
| Original discovery entries | All 1,564 entries preserved exactly, excluding only the assembly path; no removed/changed entries or duplicate IDs |
| Focused discovery | Same 362 cases across 79 methods; IDs, classes, methods, theory display names and explicit flags unchanged |
| Integrated runtime comparison | All 1,653 name/type/method/result rows match the integration baseline; all seven skip names/reasons match |
| Source preservation | All seven suites match `293c687` after removing phase comments/whitespace and inlining named CLI results; endpoint regression remains an exact content-preserving move |
| CSharpier and both whitespace checks | Passed; 205 C# files checked |

The 89 additional discovered cases come exclusively from integration's team-build
isolation (#26, 47 cases) and MCP validation (#30, 42 cases), not this organization
batch. The integrated runtime comparison used `audit21-pre33-full-debug.xml`
from the integration baseline at `293c687`, comparing multisets so duplicate
truncated theory display names remain represented. Focused runtime rows and both
focused skip reasons also match that baseline.

Using the same normalized JSON fingerprint procedure as above:

```text
Integrated full discovery (1,653): 20664bab74679df6d9ec2644b8d54254f2d05f8b942089772038a4f3ab5f5087
Focused before/after (362):       626c50f4d266ee78fd0cf65be82c8b36787a9440a24812afb10288e7dcf08e65
```

The platform limitations and synthetic-only scope described above remain
unchanged. No Release run, live runtime/registry/secret-store operation, issue/PR
edit, or modification to the original user-changed `run-abox.ps1` is claimed.

## Integration verification after #34

On 2026-11-15, read #35 and #21 and their empty comment lists through `gh api`.
Both worktrees were clean at their expected commits. Merged ticket commit
`e11ba266e801e5fbd0850d545c19e440777b3d3f` into integration baseline
`acf6977eb310a5195e753260165792a5ffe94722` with `--no-ff --no-commit`, inspected
the staged result, then committed the conflict-free merge as
`37cc2d696ba54f5db24d762220281f8fb083b6c8`.

- All seven incoming C# files match the ticket byte-for-byte. The other 89 test
  project files remain byte-for-byte identical to the integration baseline.
  This preserves #32's command-only partial/namespaces/assertions, #33's moved
  sandbox/local-state suites and creator-team partial, and #34's lifecycle partial,
  shared harness and `ShellSandbox` compatibility without adaptations.
- All seven moved suites match their pre-merge source after removing only phase
  comments/whitespace and inlining the named CLI exit results. Assertions, helpers,
  theory data, skip branches and command-level seams remain intact; the endpoint
  command regression remains an exact content-preserving move.
- Fresh pre-enumerated executable discovery before and after the merge is exactly
  equal across every JSON field: **1,653 unique cases**, with no additions,
  removals, changed metadata or duplicate IDs. This includes the same **362 endpoint
  cases across 79 methods** and all **41 `SandboxCreatorTests` cases**.
- Full-run XML matches the completed post-#34 baseline
  (`audit21-merge34/after-full.xml`) as a multiset of name/type/method/result rows.
  All seven skipped identities and nonempty reasons match exactly; focused results
  and their two skips match the corresponding baseline subset too.

| Check | Post-merge result |
| --- | --- |
| Debug solution builds before/after merge | Passed; zero warnings/errors |
| Focused endpoint/generation executable | 360 passed, 2 skipped, zero failures/errors/not-run; 7.206 seconds |
| Full Debug executable | 1,646 passed, 7 skipped, zero failures/errors/not-run; 57.553 seconds |
| CSharpier | Passed; 207 C# files checked |
| Working/staged/baseline diff whitespace checks | Passed |

Build, discovery and test commands use the same SDK and executable as the rerun
above. Tests ran with `-noLogo -noColor -maxThreads 2 -longRunning 120`, XML
results and a 30-minute timeout, not `dotnet test`. Raw discovery, logs, XML and
comparison results are under
`C:/Users/UI520628/AppData/Local/Temp/11/opencode/audit21-merge35/`.
The before/after fingerprints using the normalization procedure above are:

```text
Integration full discovery (1,653): 04e49791d1b7105878535a7fa24ba3d797a9d094ffa919e10e575e9e9424c135
Integration endpoint cases (362):  89635ec4c1bf74731d967f2e466f87b68d9dd63aeb17186def85689f36fd6605
```

These are same-worktree comparisons: discovery IDs depend on the assembly path,
so these hashes are not expected to match the ticket-worktree hashes above.
The existing two file-link, four receiver-link and one case-distinct-directory
skips remain platform limitations, not passing coverage. No production files,
other ticket worktrees, live runtime/registry/secrets, issue/PR state or original
user-modified launcher were changed. At this checkpoint, Release verification and
final reviews were deferred until #36 was integrated; the follow-up below records
the completed verification and remaining endpoint-organization fix.

## Final integrated endpoint-lifecycle follow-up

On 2026-11-15, resumed the clean `integration/audit-21` branch at
`dea89d82ccbff7a77d67c8055dd7c3e6e57d8c0f`, with all 15 tickets integrated, and
reread the complete #21/#35 bodies and empty comment lists through `gh issue view`.
This focused follow-up resolves the same S1/S2 gap reported by both final reviews:
the two endpoint-generation theories still at the test-project root.

- Moved the root `UserSecretsLifecycleTests.cs` to
  `abox-cli/test/Abox.Tests/UserSecrets/UserSecretsLifecycleTests.EndpointGeneration.cs`.
  These cross-command create/replacement cases protect the shared user-secret flow,
  beside the existing selection/lifecycle partial and its private fixture.
- Added explicit Arrange/Act/Assert markers to both methods. In the malformed-input
  test only, a named exit result separates the existing command action and assertion.
  Namespace, partial class, method names, attributes, theory inputs, assertions and
  private-helper access remain unchanged. No production files or test-only public
  APIs changed; no new tests or trivial configuration assertions were introduced.
- The five generation rows remain `published-create`, `local-create`, `published`,
  `local` and `run`; all still verify the exact current-development override payload
  and successful creation/replacement without a host file. The three malformed-input
  rows remain `published`, `local` and `run`; all still verify failure, absence of
  replacement/copy and value-free project/appsettings diagnostics.
- Reversing only the phase comments and named exit local makes the moved source
  identical to the original, ignoring blank lines. SHA-256 checks confirm the
  existing `UserSecrets/UserSecretsLifecycleTests.cs`, transport `ShellSandbox`,
  receiver safety suite and both worktree/original launcher scripts are unchanged.

### Discovery, runtime metadata and skips

Fresh Debug discovery before editing and after rebuilding is exactly equal across
all JSON fields and entry order: **1,653 unique cases**, with zero additions,
removals, duplicate IDs or changed metadata. This includes all **eight moved rows**:
their IDs, class/method names, display names/theory arguments, explicit flags and
any emitted skip metadata are unchanged. The normalized SHA-256 procedure described
above yields these identical before/after fingerprints:

```text
Full discovery (1,653): 04e49791d1b7105878535a7fa24ba3d797a9d094ffa919e10e575e9e9424c135
Moved theory rows (8): 20b92209cf2124ad46dfeb7f126fb7e5c82646721335ec3ebfbf245669a8392e
```

The before/after eight-row runtime XML matches as a multiset of
name/type/method/result rows, with no skips. Full Debug before/after and full
Release likewise have identical runtime rows and identical seven skipped
identities/reasons. Release also discovers 1,653 unique cases with the same
class/method/display names, theory arguments and explicit flags; its assembly path
and path-dependent IDs differ from Debug as expected.

The unchanged platform skips are:

- `MatchingCaseCollidingHostIdsFailRatherThanChoosingAStore`:
  `This host filesystem cannot create case-distinct synthetic store directories.`
- All four `destinationLink`/`checkOnly` rows of
  `ReceiverRejectsSymlinkAncestorsAndDestinationsWithoutTouchingTheirTargets`:
  `This host cannot create the synthetic symlink needed by the receiver test.`
- Both `generate` rows of
  `LinkedProjectFilesAreNeverReadAndDoNotPreventSelectedStores`:
  `File symlink tests require native link privileges on this host.`

None is counted as passing coverage.

### Completed final verification

Restored the repository's CSharpier tool and solution through SDK 10.0.404. Built
the solution serially in Debug before/after editing and in Release after editing;
all builds completed with zero warnings/errors. Tests used the executable xUnit
runner, not `dotnet test`, with a **1,800-second subprocess timeout per run**.

| Check | Passed | Skipped | Failed/errors/not-run | Seconds |
| --- | ---: | ---: | ---: | ---: |
| Eight moved rows, before | 8 | 0 | 0 | 0.543 |
| Full Debug baseline | 1,646 | 7 | 0 | 106.787 |
| Eight moved rows, after | 8 | 0 | 0 | 0.542 |
| Seven endpoint/generation suites listed above, after | 360 | 2 | 0 | 11.849 |
| Full Debug, after | 1,646 | 7 | 0 | 102.317 |
| Full Release, after | 1,646 | 7 | 0 | 107.103 |

Commands run from the integration worktree:

```powershell
$sdk10 = 'C:/Program Files/dotnet/sdk/10.0.404/dotnet.dll'
$artifacts = 'C:/Users/UI520628/AppData/Local/Temp/11/opencode/audit21-final-endpoint-fix'
dotnet $sdk10 tool restore
dotnet $sdk10 restore abox-cli/agentic-box.slnx --disable-parallel
dotnet $sdk10 build abox-cli/agentic-box.slnx -c Debug --no-restore -m:1 --nologo --verbosity minimal
dotnet $sdk10 build abox-cli/agentic-box.slnx -c Release --no-restore -m:1 --nologo --verbosity minimal
$debug = './abox-cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.exe'
$release = './abox-cli/test/Abox.Tests/bin/Release/net10.0/Abox.Tests.exe'
# Capture before/after discovery directly as raw stdout bytes with Python subprocess.run.
& $debug -list full/json -preEnumerateTheories -noColor
& $release -list full/json -preEnumerateTheories -noColor
& $debug -method Abox.Tests.UserSecretsLifecycleTests.EveryCreationAndReplacementPathGeneratesCurrentProjectOverridesWithoutAHostFile -method Abox.Tests.UserSecretsLifecycleTests.MalformedProjectConfigurationPreventsDestructiveReplacement -noLogo -noColor -maxThreads 2 -longRunning 120 -result-xml "$artifacts/after-focused.xml"
# The seven-class endpoint/generation command above was also rerun.
& $debug -noLogo -noColor -maxThreads 2 -longRunning 120 -result-xml "$artifacts/after-full-debug.xml"
& $release -noLogo -noColor -maxThreads 2 -longRunning 120 -result-xml "$artifacts/after-full-release.xml"
dotnet csharpier check abox-cli
git diff --check
git diff --cached --check
```

CSharpier passed for all **209 C# files**; working and staged whitespace checks
passed. Raw discovery, logs, XML, protected-file hashes, original source and
comparison reports are retained in `$artifacts`. The only remaining verification
limitations are the seven platform skips above. No live services, runtime/registry
operations, real secret stores, unrelated refactors, issue/PR mutations or worktree
cleanup were involved. The integration branch remains `integration/audit-21`.
