# Issue #14: shared-store endpoint override verification

Verified on Windows, 2026-11-15, from integration baseline `2e51210` (including
#13), in branch `feat/issue-14-shared-stores`. Both #14 and parent #12 bodies and
their empty comments responses were read through `gh api`. No issue/PR writes,
developer secrets, live sandbox/service operations or global settings changes
were performed. The main worktree's `run-abox.ps1` was not touched.

## Seam and red/green evidence

The confirmed seam is the existing CLI with temporary synthetic workspace/store
files and the existing `UserSecretsCliHarness`/substituted `ISbxClient`. No public
testing interface or format-specific copying path was introduced.

The first tracer, `CompatibleOverridesMergeOnceAcrossCaseInsensitiveKeys`, failed
in **both discovery orders** with the old temporary multi-consumer rejection.
It then passed with one generated effective key, canonical spelling and identical
payloads, while unrelated project differences and source bytes were preserved.
The new `UserSecretsSharedStoreCommandTests` replaces the obsolete blanket-rejection
test; existing discovery, unknown-consumer, lifecycle, policy and transport tests
remain regression coverage.

The 24 new test cases (some internally exercise both discovery orders and both
normal/verbose output) cover:

- Compatible URL/PostgreSQL overrides, loopback spelling differences,
  case-insensitive effective keys, deterministic generated spelling/order, and
  disjoint generated keys. Missing appsettings/key consumers impose no competing
  effective value; already-adapted consumers must agree.
- Differing generated destinations and full values, including credentials/database
  names, plus unchanged remote/unmapped-local and null consumers. Conflicts report
  store/key/project paths without synthetic values and cause **zero writes across
  all selected stores**, even with force and an earlier valid store.
- Authoritative selected host null, arbitrary, remote and mapped-local values,
  existing-host adaptation, unrelated lower-priority differences, development
  precedence, exact/wildcard/empty/no-match filters and excluded keys.
- Unknown consumers still block generation even when known consumers agree;
  authoritative keys do not become appsettings conflicts. Existing #13 tests also
  preserve unknown-consumer copy-only behavior and unsupported-association checks.
- Deny checks on **every generated original**, even when candidate targets are
  identical and only a later original matches. Force allows only that policy
  override and reports it after successful copying.
- Byte-for-byte source immutability in successful and rejected cases. All values
  and credentials are synthetic fixtures; host roots are injected temporary paths.

## Generic merge contract

`UserSecretsCopy.SharedStores.cs` accepts each project's desired effective values
(unchanged values included) and generated-key candidates after selection/precedence
and endpoint adaptation. It examines only the union of generated keys, checks all
present consumer values with ordinal value equality and case-insensitive keys, and
rejects incompatible candidates before destination writes. It does not parse URLs,
database connection strings or split settings. Sibling format adapters must feed
the same maps after resolving their own effective context; no bypass copy loop is
needed. Canonical key spelling and emitted generated ordering are ordinal and
independent of project discovery order.

## Checks

The repository's `global.json` opts `dotnet test` into Microsoft.Testing.Platform,
but the current xUnit executable does not handshake with that runner on this host.
Both the ambient SDK 11 command and explicit SDK 10.0.404 command exited with a
runner/handshake failure and **zero tests**; these are not claimed as test passes.
No repository runner configuration was changed. Reliable verification used the
existing xUnit executable directly, after a serial Debug solution build:

```powershell
$sdk10 = 'C:/Program Files/dotnet/sdk/10.0.404/dotnet.dll'
dotnet $sdk10 build abox-cli/agentic-box.slnx -c Debug --no-restore -m:1 --nologo --verbosity minimal
dotnet abox-cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll -class 'Abox.Tests.UserSecrets*' -class Abox.Tests.CreateCommandTests -class Abox.Tests.RunCommandTests -noLogo -maxThreads 2
dotnet abox-cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll -noLogo -maxThreads 2
dotnet csharpier check abox-cli
git diff --check
```

| Check | Result |
| --- | --- |
| Debug solution build | Passed; 0 warnings, 0 errors |
| Focused user-secrets/create/run regression set | 525 passed, 0 failed, 7 platform skips (532 total) |
| Full Debug executable | 1008 passed, 0 failed, 7 platform skips (1015 total) |
| CSharpier and whitespace check | Passed |

After implementation commit `0092805`, `git merge
integration/issue-12-endpoint-overrides` reported already up to date at `2e51210`;
the integration tip was confirmed to be an ancestor of this branch. The Debug
solution build and **full 1015-test executable run were repeated after that merge
check**, with the same zero-warning build and 1008 passes/7 skips/zero failures.

The seven existing skips are four native-symlink receiver fixtures, two
native-file-symlink discovery fixtures and one case-distinct host-directory
collision fixture unsupported by this Windows host. They are not claimed as
passing. No Release build/tests are claimed here; final integration Release
verification belongs to the orchestrator. These tests establish configuration
output and safety, not authentication, readiness, arbitrary provider ordering or
an independent Unix/live sbx run. Oracle/split-settings implementation belongs to
the concurrent sibling tickets, not #14.
