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
$tests = './cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.exe'
$artifacts = 'C:/Users/UI520628/AppData/Local/Temp/11/opencode'
dotnet $sdk10 build cli/agentbox.slnx -c Debug --no-restore -m:1 --nologo --verbosity minimal
# Capture discovery stdout as raw bytes, avoiding shell code-page decoding.
python -c "import subprocess; subprocess.run(['$tests', '-list', 'full/json', '-preEnumerateTheories', '-noColor'], stdout=open('$artifacts/ticket-35-integrated-discovery.json', 'wb'), check=True)"
& $tests -class Abox.Tests.UserSecretsRewriteTests -class Abox.Tests.UserSecretsRewriteCommandTests -class Abox.Tests.UserSecretsAppSettingsCommandTests -class Abox.Tests.UserSecretsOracleCommandTests -class Abox.Tests.SplitSettingsCommandTests -class Abox.Tests.UserSecretsSharedStoreCommandTests -class Abox.Tests.UserSecretsEndpointCommandTests -noLogo -noColor -maxThreads 2 -result-xml "$artifacts/ticket-35-integrated-focused.xml"
& $tests -noLogo -noColor -maxThreads 2 -longRunning 120 -result-xml "$artifacts/ticket-35-integrated-full-debug.xml"
dotnet csharpier check cli
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
