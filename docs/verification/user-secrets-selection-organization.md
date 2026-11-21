# User Secrets selection and lifecycle test organization (#34)

## Scope

Selection/filtering, store-prefix expansion, preparation/copy snapshots, deny-list
safety, force accounting and create/run lifecycle tests now live under
`abox-cli/test/Abox.Tests/UserSecrets/`, beside the shared feature they protect. The
existing `UserSecretsCliHarness` moved there unchanged; behavior-specific fixtures
remain private to their suites. All 76 test methods in this batch have explicit
Arrange, Act and Assert markers, including repeated phases for multi-step cases.

Namespaces, class/method names, theory inputs and synthetic filesystem/tool seams
are preserved. Partial classes retain compatibility with command-only cases under
`Features/UserSecrets/` (#32), the two endpoint-generation lifecycle cases (#35)
and the receiver/process safety cases (#36). This batch does not reorganize those
endpoint/adapter cases or change production behavior or public testing APIs.

## Preservation checks

- Baseline: integration `8df050820f6a2478ac1bd6bf6d542547838db3ed`.
- Latest integrated base verified: `integration/audit-21` at
  `293c687cf64b89c3eef8f832712fa199d37af5b6`, merged into this branch at
  `fad83c8b32037595579e36caca97c39bc0e9a095`. A subsequent merge check was
  already up to date; no other ticket branch was merged directly.
- Executable xUnit discovery with `-preEnumerateTheories -list full/json` found
  **1,564 cases before and after the mechanical organization**. After integration,
  discovery found **1,653 cases**: all 1,564 original IDs remain, plus 89 cases
  from integrated behavior fixes. The six focused classes retain **257 cases**,
  with zero differences in IDs, class/method identities, display names, explicit
  flags or skip metadata. Fresh discovery was captured as raw UTF-8 stdout; the
  earlier captures' reversible UTF-8/code-page-850 decoding mismatch was corrected
  in memory using strict encoding fallbacks before comparing display names.
- Syntax-token comparison of all 121 methods (including shared/private helpers)
  in the affected original suites found zero differences after ignoring comments,
  whitespace and the mechanical extraction of immediately asserted awaited results.
  Original assertions, callbacks, theory data and skip branches remain intact.
- Post-merge full-run XML comparison found the same seven skipped case identities
  and reasons; the focused run retained the same five. No cases were not run.

## Verification

The solution was restored with `dotnet restore abox-cli/agentic-box.slnx --disable-parallel`
and built with `dotnet build abox-cli/agentic-box.slnx -c Debug --no-restore -m:1`, with zero
warnings/errors.
Tests ran through `abox-cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.exe`, not a
discovery-only `dotnet test` invocation.

| Check | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| Baseline full Debug | 1,557 | 7 | 0 |
| Focused selection/copy/filtering/lifecycle/safety/store-prefix classes | 252 | 5 | 0 |
| Pre-merge full Debug | 1,604 | 7 | 0 |
| Final post-merge focused Debug | 252 | 5 | 0 |
| Final post-merge full Debug | 1,646 | 7 | 0 |

Focused execution included all of `UserSecretsCommandTests`, including the
command-specific partial, `UserSecretsCopyTests`, `UserSecretsFilteringTests`,
`UserSecretsLifecycleTests`, all remaining adapter cases in `UserSecretsSafetyTests`
and `UserSecretsStorePatternTests`, selected using repeated `-class Abox.Tests.<name>`
arguments. Final focused/full execution completed with zero errors, failures or
not-run cases in 2.345/35.943 seconds, respectively.

The five focused skips are four native symlink receiver rows
(`ReceiverRejectsSymlinkAncestorsAndDestinationsWithoutTouchingTheirTargets`, all
`destinationLink`/`checkOnly` combinations: this host cannot create the synthetic
symlink) and `MatchingCaseCollidingHostIdsFailRatherThanChoosingAStore` (this host
filesystem cannot create case-distinct synthetic store directories). The full suite
additionally skips both `LinkedProjectFilesAreNeverReadAndDoNotPreventSelectedStores`
rows (`generate: false/true`: native file-link privileges unavailable). They are
platform limitations, not passing coverage.

`dotnet csharpier check abox-cli` and `git diff --check` passed. No live sandbox, host
secret store, registry publication or production service was used. No Release
verification was performed for this test-only batch.

## Integration verification after #33

On 2026-11-15, merged ticket commit
`dd5acd7d954930c47d2da55597181b4198e10b63` into integration baseline
`f99806a3569defbd87e1a2fddee3ac492eb0ed47` with a non-fast-forward merge.

- The merge was conflict-free and required no code adaptations. Incoming C# files
  match the ticket commit byte-for-byte; #33's moved sandbox/local-state files and
  creator-team partial remain byte-for-byte unchanged from the integration baseline.
- #32's command-only `UserSecretsCommandTests` partial remains unchanged under
  `Features/UserSecrets/`. The transport suite's internal `ShellSandbox` and its
  safety-suite alias remain unchanged and compatible for the remaining #36 split.
- Fresh pre-enumerated executable discovery found **1,653 cases before and after**,
  with exact equality of every discovery field, including IDs, class/method names,
  display names and explicit/skip metadata. The six focused classes retain **257**
  cases; all **41** renamed `SandboxCreatorTests` cases retain their current IDs.
- Assertion/received-call counts and test/data attribute text remain unchanged
  across each of the six affected suites, including their partial files.
- Debug builds before and after the merge completed with **0 warnings/errors**.
  Executable focused verification: **252 passed, 5 skipped, 0 failed/errors/not-run**
  in 2.285 seconds. Full executable verification before and after: **1,646 passed,
  7 skipped, 0 failed/errors/not-run**, in 30.220/30.286 seconds respectively.
- Full-run XML confirms identical skipped case identities and nonempty reasons;
  the focused skips are the same five-case subset. The native symlink and
  case-distinct filesystem limitations described above are unchanged.
- `dotnet csharpier check abox-cli`: **207 files clean**. Staged and unstaged
  `git diff --check` checks passed.

No production files, live runtime/registry/secret operations, issue/PR edits or
user launcher changes are included. Release verification and final reviews remain
deferred until the remaining audit tickets are integrated.
