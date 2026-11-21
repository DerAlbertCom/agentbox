# Technical-adapter test organization (#36)

## Scope and ownership

This is the remaining technical-module organization and test-phase batch of #21.
Both issue bodies and their empty comment lists were reread with `gh issue view`.
No production behavior or test-only production interface was changed.

- Configuration/path tests live in `Configuration/`; YAML-reader tests follow
  their actual production owner in `Infrastructure/`.
- Descriptor/reference, squad resolution, state mixin and workload-generation
  tests live in `Kits/`; sandbox/image naming tests live in `Naming/`; preflight
  behavior lives in `Health/`.
- Process output, cancellation, byte-preserving input and sensitive diagnostics
  live in `Infrastructure/`. Sbx arguments, source trust, user-secret policy and
  receiver/transport safeguards live in `Sbx/`.
- `UserSecretsPolicyTests` is a namespace-compatible partial: its composed
  command/reporting case lives in `Features/UserSecrets/`, policy parsing and
  authorization cases in `Sbx/`, and process diagnostics in `Infrastructure/`.
- The remaining eight `UserSecretsSafetyTests` methods were split by ownership:
  six sbx/receiver methods in `Sbx/` (18 theory/fact cases) and two sensitive
  process methods in `Infrastructure/`. Their `Abox.Tests` namespace, partial
  class, test names, data, assertions and skip branches are unchanged. The
  selection/deny-list partial integrated from #34 stays in `UserSecrets/`.
- Common `TempDirectory` and `TestConsole` fixtures live in `Support/`, with
  byte-identical contents and their existing namespace. Specialized process and
  shell fixtures remain with their consumers. In particular, the existing
  internal `UserSecretsTransportTests.ShellSandbox` remains unchanged and is
  reused by the safety suite through its existing alias, not copied or exposed
  through production code.
- Integrated #22 already split OCI behavior among `Infrastructure/OciRegistryTests`,
  `LocalState/LocalWorkloadImagesTests`, `Sandboxes/PublishedSquadSourceTests` and
  `Features/Publish/PublishServiceTests`. Those supported-operation seams and
  Docker fixtures are preserved rather than duplicated. Existing composed
  launcher coverage remains with run/compatibility behavior; no new launcher
  tests or interfaces were invented.

All 169 affected test methods have explicit Arrange/Act/Assert markers. Supplied
theory data or constructor fixtures are described where no additional setup is
needed; multi-step tests retain repeated phases and their meaningful assertions.

## Integration and preservation

Resumed from clean ticket HEAD `e588d25a16dc26959848d548da34d5f3e0fee222`,
which contains implementation `922ea44f2aac7a1ceaaf26ef8d16bd6f239326a3`.
Merged only `integration/audit-21`, first at
`acf6977eb310a5195e753260165792a5ffe94722` (#34), then at
`37cc2d696ba54f5db24d762220281f8fb083b6c8` (#35), and finally at
`475fc7b984e3802650819270dc49f82edbe2c9d3` before final verification.
The final integration merge is `d3c66b3e73a202b25b9fa584d2f24ae4543c6fad`.
All merges were conflict-free. No ticket branch was merged directly and no
integration worktree was edited.

- Original executable discovery captures before/after `922ea44` are exactly
  equal: **1,564 cases**. The earlier team-integrated capture contains **1,611**;
  current discovery contains **1,653**. The 89/42 additions respectively are
  integrated #26/#30 coverage, not new cases in this organization batch.
- #33 renamed `CreateServiceTests` to `SandboxCreatorTests`. All 36 creator
  cases in the original capture match after that deliberate class/display-name
  rename; the earlier 1,611-case capture matches all 41 current creator cases.
  Other original IDs and metadata are unchanged, with zero lost cases. Older
  captures' reversible UTF-8/code-page-850 display-name mismatch was corrected
  in memory using strict encoding fallbacks; evidence files were not rewritten.
- Fresh pre-resume and final raw-stdout discovery lists are **exactly equal in
  all 1,653 entries**, including order, assembly, ID, class, method, display name,
  explicit flags and any skip metadata. Thus #32 partials, #33's current creator
  identities, #34 selection/lifecycle and #35 endpoint cases remain discoverable.
- Roslyn token comparison of the resumed safety split found all 15 methods
  (including its integrated feature partial/private helpers) and all six
  constructor/field/property members unchanged after ignoring comments and
  whitespace. No test/data/assertion/skip token was changed by the split.
- Comparing the original technical batch with its pre-implementation sources
  found 149 of 179 methods token-identical, and 28 equal after inlining named
  local results used to separate actions from assertions. The two remaining
  differences were inspected: the integrated #26 team-qualified build path and
  an explicit `NotThrowAsync` assertion replacing an implicitly successful
  awaited next-client call. No assertion was removed. All 46 original
  constructor/field/property members have identical tokens.
- Runtime XML comparison preserves all skipped names/reasons and all full-suite
  name/type/method/result rows; comparisons use multisets because truncated
  theory display names need not be unique. No cases were not run.

The final full-discovery SHA-256, omitting only `Assembly` and serializing with
Python `json.dumps(rows, sort_keys=True, separators=(',', ':'), ensure_ascii=True)`,
is:

```text
f4b71167d93d489e4e873fa6d72c8625d685821127540bea447e41b295c3dd77
```

## Verification

Restored with `dotnet restore cli/agentic-box.slnx --disable-parallel` and built with
`dotnet build cli/agentic-box.slnx -c Debug --no-restore -m:1`; builds completed with
zero warnings/errors. Tests ran through the executable xUnit runner, not a
discovery-only `dotnet test` invocation, with a 30-minute outer timeout and
`-longRunning 120` diagnostics.

| Check | Passed | Skipped | Failed/errors/not-run |
| --- | ---: | ---: | ---: |
| Original technical focused run, before/after `922ea44` | 348 | 0 | 0 |
| Earlier full Debug after team-build integration | 1,604 | 7 | 0 |
| Resumed full Debug before the safety split | 1,646 | 7 | 0 |
| Final focused Debug after latest integration | 389 | 4 | 0 |
| Final full Debug after latest integration | 1,646 | 7 | 0 |

Final focused/full execution completed in 22.534/30.395 seconds respectively.

Final focused execution selects these 24 classes with repeated
`-class Abox.Tests.<name>` arguments:

```text
AboxConfigLoaderTests AboxPathsTests PreflightTests ProcessOutputTests
ProcessRunnerTests UserSecretsPolicyProcessTests YamlReaderTests
KitDescriptorTests KitReferenceTests SquadResolverTests StateMixinGeneratorTests
WorkloadFileTests WorkloadKitGeneratorTests ImageNameTests SandboxNameTests
SbxAllowedSourcesTests SbxClientTests UserSecretsPolicyTests
UserSecretsTransportTests UserSecretsSafetyTests SbxSecretInputTests
OciRegistryTests SquadDiscoveryTests SourceDefaultsTests
```

Discovery uses `-preEnumerateTheories -list full/json -noColor`, captured directly
as raw stdout bytes. Execution uses `-noLogo -noColor -longRunning 120
-result-xml <path>`, plus those class arguments for the focused run.

The focused skips are the existing four receiver-symlink theory rows: all
`destinationLink`/`checkOnly` combinations of
`ReceiverRejectsSymlinkAncestorsAndDestinationsWithoutTouchingTheirTargets`.
The full suite also skips `MatchingCaseCollidingHostIdsFailRatherThanChoosingAStore`
and both `LinkedProjectFilesAreNeverReadAndDoNotPreventSelectedStores` rows.
This host lacks the native symlink privileges and case-distinct filesystem
support those synthetic fixtures require. None is claimed as passing coverage.

`dotnet csharpier check cli` passed for all 209 C# files, as did staged/unstaged
`git diff --check` checks.
Raw discovery, run logs, XML and source/discovery comparison reports are retained
under `C:/Users/UI520628/AppData/Local/Temp/11/opencode/ticket-36/`; final run
artifacts have the `post475-` prefix. No live sandbox, registry, host-secret store,
production service, issue/PR mutation or original user-modified launcher script
was involved. No Release verification is claimed for this bounded test-only batch.

## S1/S2 endpoint follow-up outside #36 (resolved)

A repository-wide syntax inspection found no remaining missing phase markers in
the technical-module batch. At that checkpoint it found two endpoint-generation
lifecycle methods left in root `cli/test/Abox.Tests/UserSecretsLifecycleTests.cs`:

- `EveryCreationAndReplacementPathGeneratesCurrentProjectOverridesWithoutAHostFile`
  (five theory rows).
- `MalformedProjectConfigurationPreventsDestructiveReplacement` (three rows).

These endpoint cases belong to #35's endpoint/create-replacement coverage and were
originally reported rather than changed outside #36's scope. The final integrated
follow-up now moves both into
`UserSecrets/UserSecretsLifecycleTests.EndpointGeneration.cs` and adds explicit
Arrange/Act/Assert phases. All eight rows, identities, data, assertions and private
fixture/partial access are preserved; the existing selection/lifecycle partial and
`ShellSandbox` remain unchanged. The S1/S2 gap is resolved, not an open residual.
See the [final endpoint-lifecycle verification](audit-21-endpoint-test-organization.md#final-integrated-endpoint-lifecycle-follow-up)
for exact before/after discovery/metadata and runtime/skip comparisons, plus full
Debug and Release execution: each discovers 1,653 cases, passes 1,646 and skips the
same seven platform-dependent cases, with zero failures/errors/not-run.

## Integration verification after #35

On 2026-11-15, reread #36 and #21 and their empty comment lists through
`gh issue view`. Both worktrees were clean at their expected commits. Merged only
ticket `0a82785633100b79445775e9fd2bea47e2f42c97` into integration baseline
`475fc7b984e3802650819270dc49f82edbe2c9d3` with `--no-ff --no-commit`, inspected
the staged result, then committed the conflict-free merge as
`5f376599101b8a0c93f453270d948200f5728a59`.

- The inspected merge tree exactly equals the ticket tip. Changes are confined to
  the agreed test files and this evidence document; no production files changed.
  The other 74 test-project files remain byte-for-byte identical to the integration
  baseline, preserving #32–35's organization, namespaces, partials and fixtures.
- A fresh whole-test-project Roslyn comparison preserves all **953** method,
  constructor, property and field identities. **923** members have identical
  nontrivia tokens. All 30 differences were inspected: 29 introduce named locals
  to separate phases without removing assertions, and one makes an already
  required successful call explicit with `NotThrowAsync`. Theory data, helpers,
  process-byte/cancellation checks, skip branches and the shared `ShellSandbox`
  remain intact. No constructor, property or field tokens changed.
- Fresh pre-enumerated executable discovery before/after the merge is exactly
  equal across every JSON field and entry order: **1,653 unique cases**, including
  all **41 `SandboxCreatorTests` cases**, with no added, removed or changed entries.
- Full-run XML matches the completed integration baseline
  (`audit21-merge35/after-full.xml`) as a multiset of name/type/method/result rows.
  All seven skipped identities and nonempty reasons match exactly; focused results
  and their four skips match the corresponding baseline subset too.

| Check | Post-merge result |
| --- | --- |
| SDK 10 restore and Debug builds before/after merge | Passed; zero warnings/errors |
| Focused technical-adapter executable, same 24 classes listed above | 389 passed, 4 skipped, zero failures/errors/not-run; 27.528 seconds |
| Full Debug executable | 1,646 passed, 7 skipped, zero failures/errors/not-run; 100.478 seconds |
| CSharpier | Passed; 209 C# files checked |
| Working/staged/baseline diff whitespace checks | Passed |

Restore/build used `dotnet 'C:/Program Files/dotnet/sdk/10.0.404/dotnet.dll'`
with `restore cli/agentic-box.slnx --disable-parallel` and
`build cli/agentic-box.slnx -c Debug --no-restore -m:1 --nologo --verbosity minimal`.
Discovery was captured as raw stdout bytes with
`-preEnumerateTheories -list full/json -noColor`. Tests used the Debug
`Abox.Tests.exe` directly with `-noLogo -noColor -maxThreads 2 -longRunning 120
-result-xml <path>` and a 30-minute subprocess timeout per run, not `dotnet test`.
Raw discovery, logs, XML and source/discovery/runtime comparison artifacts are
under `C:/Users/UI520628/AppData/Local/Temp/11/opencode/audit21-merge36/`.

The before/after fingerprint using the normalization procedure above is:

```text
Integration full discovery (1,653): 04e49791d1b7105878535a7fa24ba3d797a9d094ffa919e10e575e9e9424c135
```

This is a same-worktree comparison: discovery IDs depend on the assembly path,
so this hash is not expected to match the ticket-worktree hash above. The existing
four receiver-link, two file-link and one case-distinct-directory skips remain
platform limitations, not passing coverage. At this checkpoint the two root
endpoint-generation lifecycle methods were left for the final reviewer/fixer;
their subsequent focused fix and completed whole-program Debug/Release verification
are recorded in the resolved follow-up above. All 15 implementation tickets are
integrated. No other branch was merged, no ticket worktree
was edited, and no live runtime, registry, real secret store or issue/PR mutation
was used. The original user-modified `run-abox.ps1` retains its pre-merge SHA-256.
