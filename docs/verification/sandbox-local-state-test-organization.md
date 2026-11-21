# Sandbox and local-state test organization (#33)

Initially verified on Windows on 2026-11-15 against integration baseline
`8df050820f6a2478ac1bd6bf6d542547838db3ed` (parent #21). Integration verification
against the later #30 baseline is recorded below.

## Scope

- Shared creation and record-writing behavior is covered by
  `Sandboxes/SandboxCreatorTests.cs`, renamed from `CreateServiceTests` to name
  the production interface actually exercised.
- Legacy naming, inventory selection and cross-command compatibility suites are
  grouped under `Sandboxes`, alongside published-source and state-creation tests.
- Build-store tests and the local-workload fixture are grouped under `LocalState`,
  alongside the existing local-image and tag-safety tests.
- Sandbox/published-source fixtures live under `Sandboxes`. Existing `Abox.Tests`
  namespaces and shared fixture signatures remain compatible with other batches.
- Every test in these two module folders has explicit Arrange/Act/Assert markers.
  Actions previously embedded in success assertions are captured separately;
  exception assertions still execute the captured action. Existing assertions,
  theory data and platform conditions are retained. No production API changed.

## Discovery comparison

The Debug test executable was run before and after the changes with
`-preEnumerateTheories -list full/json`:

- Before: **1,564** discovered cases; after: **1,564**.
- The sole intentional class rename maps all **36**
  `Abox.Tests.CreateServiceTests.*` cases to `Abox.Tests.SandboxCreatorTests.*`.
  Their xUnit IDs change with the class name; method names and theory arguments do not.
- After applying that explicit name mapping, the display-name multisets match
  exactly: zero missing or additional cases, with no introduced duplicates.
- All **1,528** other cases retain their exact xUnit IDs, display names and
  discovered explicit/skip metadata.
- Fact/theory/data attributes and assertion counts are unchanged in every edited
  suite. The pre-existing partial suites were retained, not copied or overwritten.

## Verification

- `dotnet build abox-cli/agentic-box.slnx -c Debug -m:1`: zero warnings/errors.
- Direct executable runner, all `Sandboxes`/`LocalState` classes: **219 passed**,
  zero failures/skips.
- Direct executable runner, full Debug suite before and after: **1,557 passed**,
  **7 skipped**, zero failures/errors (1,564 total in each run).
- The unchanged Windows skips are one case-distinct-store filesystem case, four
  receiver symlink cases and two project-file symlink cases requiring native
  privileges. They are not counted as passes.
- `dotnet csharpier check abox-cli` and `git diff --check`: passed.

All inputs and external tools were synthetic. No live sandbox, host-secret store,
registry publication, issue modification or production behavior change was needed.

## Integration verification after #26, #32 and #30

On 2026-11-15, merged ticket commit
`55242f70d4f5d66f3cad390caa1d7a18dfc3f1e0` into integration baseline
`293c687cf64b89c3eef8f832712fa199d37af5b6` with a non-fast-forward merge.

- Resolved three content conflicts: retained #26's team-qualified BuildStore
  calls, ownership-proving references and legacy-manifest setup; retained #32's
  partial `LegacySandboxInventoryTests` and its separate prune/exec/pipe files;
  kept #33's shared-creator rename and phase markers. Renamed #26's creator-team
  partial file/class to `SandboxCreatorTests.Teams.cs`/`SandboxCreatorTests`.
- Pre-enumerated full Debug discovery: **1,653 cases before and after**. This
  includes the **89** cases added since the original 1,564-case ticket baseline.
  The original **36** creator cases plus **5** later team cases receive the
  intentional class-name mapping. All **1,612** other cases retain exact IDs and
  discovery metadata. After mapping the creator class and excluding its changed
  IDs, all discovery metadata and display-name multisets match exactly. The seven
  pre-existing duplicate display-name groups are unchanged; none were introduced.
- Fact/theory/data attribute text and assertion/received-call counts are unchanged
  in all twelve changed or moved C# files. All later integration tests remain.
- `dotnet build abox-cli/agentic-box.slnx -c Debug -m:1`: **0 warnings, 0 errors**.
- Focused executable Debug run across all eleven sandbox/local-state test classes,
  including their partial files: **244 passed, 0 skipped, 0 failed**.
- Full executable Debug run before the merge (completing #30 verification) and
  after conflict resolution: **1,646 passed, 7 skipped, 0 failed/errors** in each
  run. The seven skip identities and reasons match exactly: the same unavailable
  case-distinct filesystem and native symlink capabilities described above.
- `dotnet csharpier check abox-cli`: **205 files clean**. Both staged and unstaged
  `git diff --check` checks passed.

No production files or user launcher changes are included. Release verification
and final reviews remain deferred until the remaining audit tickets are merged.
