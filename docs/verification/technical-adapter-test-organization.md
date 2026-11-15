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

Restored with `dotnet restore cli/agentbox.slnx --disable-parallel` and built with
`dotnet build cli/agentbox.slnx -c Debug --no-restore -m:1`; builds completed with
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

## Remaining S1/S2 ownership outside #36

A repository-wide syntax inspection found no remaining missing phase markers in
the technical-module batch. It did find two endpoint-generation lifecycle methods
left in root `cli/test/Abox.Tests/UserSecretsLifecycleTests.cs`:

- `EveryCreationAndReplacementPathGeneratesCurrentProjectOverridesWithoutAHostFile`
  (five theory rows).
- `MalformedProjectConfigurationPreventsDestructiveReplacement` (three rows).

These are the endpoint cases deliberately left outside #34 and belong to #35's
endpoint/create-replacement coverage. Their remaining root placement (S1) and
missing phase markers (S2) are reported rather than silently claimed compliant
or changed outside this ticket's agreed scope. They still execute and retain
their shared lifecycle fixture/partial compatibility. Whole-program Release and
final reviews remain parent-level verification work.
