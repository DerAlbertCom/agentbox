# Issue #16: recognized split settings verification

## Scope and seam

Implemented only the explicit case-insensitive effective pair
`Database_Config:Host` + `Database_Config:Port`, through existing preparation and
copying. The confirmed primary seam is the real `user-secrets` CLI command with
`UserSecretsCliHarness`, temporary synthetic workspace/host files and substituted
`ISbxClient`; output assertions inspect the emitted sandbox secrets JSON.
No developer secret stores or live services were read or required.

## TDD evidence

- First command test failed with `{}` instead of the host-only generated override;
  adding the explicit adapter made it pass.
- Host-only filtered generation failed until unfiltered appsettings port metadata
  survived base/development resolution separately from emitted keys.
- Selected secret port precedence failed in both directions (incorrect generation
  and missing generation); overlaying **only selected secrets** before eligibility
  made both pass. Filtered-out secret ports remained absent from effective resolution.
- Selected secret host adaptation failed for the literal loopback forms before the
  copied-host path received the same effective project/secret context.
- Mapped-local ambiguous split settings initially returned success (including
  malformed brackets rewritten as valid hosts); fail-closed recognition made all
  unsupported-form tests pass with safe normal/verbose diagnostics and zero writes.

## Coverage and checks

`SplitSettingsCommandTests` currently has **79 passing cases** covering host-only
generation, case-insensitive base/development/selected-secret precedence, selected
and filtered ports/hosts, exact/wildcard/empty filters, literal loopback forms,
remote/unmapped/unresolved forms, no-project secret pairs, original port retention,
no arbitrary sibling inference, unsupported mapped-local syntax, deny/force
accounting, policy/destination validation, stale generated-host removal and source
immutability. Selected-store validation failures assert no destination writes.
Shared-store coverage includes reversed project order, compatible pairs with
different mapped ports, remote/unmapped consumers, already-adapted hosts, selected
versus filtered secret ports, and authoritative selected hosts whose consumer ports
would require incompatible adaptation. Malformed structured Port strings are not
reinterpreted as standalone URLs/connection strings.

On 2026-11-15, before sibling integration:

- `dotnet test cli/agentbox.slnx -c Debug --filter FullyQualifiedName~SplitSettingsCommandTests`:
  **62 passed**, no failures/skips.
- `dotnet build cli/agentbox.slnx -c Debug`: zero warnings/errors.
- `dotnet test cli/agentbox.slnx -c Debug --no-build`: **1050 passed**, zero failures,
  **7 explicit platform skips** (1057 total).
- `dotnet csharpier check cli`: passed (164 files).
- `git diff --check`: passed.

The seven existing skips cover native Windows symlink privileges and case-distinct
directory support; they are not reported as passing. Release integration verification is
owned by the orchestration session. No live sandbox/service readiness, database
authentication, issue/PR writes, publication or global-setting changes are claimed.

## Shared-store integration

Merged integration tip `32569b8` (containing #14 `cf7d583`/`0092805`) into the issue
branch. Resolution retained the generic `ProjectOverrides`/`MergeGeneratedOverrides`
safeguards, with desired consumer hosts calculated using each consumer's complete
appsettings metadata plus selected secrets **before** proposing host-only writes.
The seven shared-pair scenarios were red under the temporary multi-consumer
restriction and passed after integration; the focused split/shared suites pass
**103 cases**, no failures/skips. An additional red/green slice proved invalid
structured ports never become endpoint-generated Port overrides.
Selected hosts depending on project port metadata also use the generic shared-store
checks, including unknown-consumer rejection; a red/green test proved a selected
host alone could not bypass those safeguards. Authoritative selected host/port
pairs retain validated copy-only behavior.

Final post-merge Debug checks:

- `dotnet build cli/agentbox.slnx -c Debug`: zero warnings/errors.
- `dotnet test cli/agentbox.slnx -c Debug --no-build`: **1087 passed**, zero failures,
  **7 platform skips** (1094 total).
- `dotnet csharpier check cli`: passed (166 files).
- `git diff --check`: passed.
