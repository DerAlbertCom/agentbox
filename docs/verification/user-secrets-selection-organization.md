# User Secrets selection and lifecycle test organization (#34)

## Scope

Selection/filtering, store-prefix expansion, preparation/copy snapshots, deny-list
safety, force accounting and create/run lifecycle tests now live under
`cli/test/Abox.Tests/UserSecrets/`, beside the shared feature they protect. The
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

The solution was restored with `dotnet restore cli/agentbox.slnx --disable-parallel`
and built with `dotnet build cli/agentbox.slnx -c Debug --no-restore -m:1`, with zero
warnings/errors.
Tests ran through `cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.exe`, not a
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

`dotnet csharpier check cli` and `git diff --check` passed. No live sandbox, host
secret store, registry publication or production service was used. No Release
verification was performed for this test-only batch.
