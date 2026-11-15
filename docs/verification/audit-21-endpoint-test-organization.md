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

## Verification

Used the existing executable xUnit runner, not the incompatible `dotnet test`
handshake on this host. Commands run from the ticket worktree:

```powershell
$sdk10 = 'C:/Program Files/dotnet/sdk/10.0.404/dotnet.dll'
$tests = 'cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll'
dotnet $sdk10 build cli/agentbox.slnx -c Debug --no-restore -m:1 --nologo --verbosity minimal
dotnet $tests -list full/json -preEnumerateTheories
dotnet $tests -class Abox.Tests.UserSecretsRewriteTests -class Abox.Tests.UserSecretsRewriteCommandTests -class Abox.Tests.UserSecretsAppSettingsCommandTests -class Abox.Tests.UserSecretsOracleCommandTests -class Abox.Tests.SplitSettingsCommandTests -class Abox.Tests.UserSecretsSharedStoreCommandTests -class Abox.Tests.UserSecretsEndpointCommandTests -noLogo -maxThreads 2
dotnet $tests -noLogo -maxThreads 2
dotnet csharpier check cli
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
