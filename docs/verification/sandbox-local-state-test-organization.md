# Sandbox and local-state test organization (#33)

Verified on Windows on 2026-11-15 against integration baseline
`8df050820f6a2478ac1bd6bf6d542547838db3ed` (parent #21).

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
  exactly: zero missing, additional or duplicated cases.
- All **1,528** other cases retain their exact xUnit IDs, display names and
  discovered explicit/skip metadata.
- Fact/theory/data attributes and assertion counts are unchanged in every edited
  suite. The pre-existing partial suites were retained, not copied or overwritten.

## Verification

- `dotnet build cli/agentbox.slnx -c Debug -m:1`: zero warnings/errors.
- Direct executable runner, all `Sandboxes`/`LocalState` classes: **219 passed**,
  zero failures/skips.
- Direct executable runner, full Debug suite before and after: **1,557 passed**,
  **7 skipped**, zero failures/errors (1,564 total in each run).
- The unchanged Windows skips are one case-distinct-store filesystem case, four
  receiver symlink cases and two project-file symlink cases requiring native
  privileges. They are not counted as passes.
- `dotnet csharpier check cli` and `git diff --check`: passed.

All inputs and external tools were synthetic. No live sandbox, host-secret store,
registry publication, issue modification or production behavior change was needed.
