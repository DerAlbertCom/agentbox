# Explicit selected-project association: regression and verification

Verified on Windows, 2026-11-15, in
`D:/dev/rwe/agentic-box-selected-store-fix`, from `b4696967b3cd8ad871a91347d8e7438204e5290e`
on `integration/issue-12-endpoint-overrides`. Before committing, changes were
isolated on `fix/selected-user-secrets-projects`, leaving the original checkout's
branch and HEAD unchanged.
PR #19 was still open at that baseline. No PR was pushed, updated or merged.

This requested policy simplification supersedes the global unknown-consumer
blocking/warnings recorded in earlier #13/#14/story verification. Those historical
records are unchanged. The current [association contract](../user-secrets-appsettings.md#bounded-discovery-not-application-startup)
defines the intentionally static model:

- Only explicit case-insensitive selected-ID matches establish consumers.
  Unrelated computed/import-only/absent IDs neither warn nor block generation.
  Conditional, multiple or invalid declarations with no matching value are ignored.
  Import contents are never inspected or evaluated, even for matching projects.
- Readable XML explicitly naming a selected ID must have exactly one unconditional
  literal declaration in a top-level `PropertyGroup`. Conditional, multiple,
  attributed, nested or misplaced matching declarations fail before any writes,
  even with authoritative host values, empty filters and `--force`.
- Unreadable/invalid/DTD-bearing/linked XML establishes no association and is
  ignored, even when raw text mentions a selected ID. This is not a guarantee about
  hidden or MSBuild-inferred consumers. Associated appsettings remain strictly
  validated; unrelated appsettings are never read.
- Every valid explicit matching shared-store consumer still participates in
  compatibility checks, including unchanged remote/unmapped/null values.

## Red before green

The existing CLI seam uses temporary synthetic files and `UserSecretsCliHarness`'s
substituted `ISbxClient`; no real Deals project, developer secrets, sandbox or live
service was used. Added the exact regression before editing production code:

```powershell
$sdk10 = 'C:/Program Files/dotnet/sdk/10.0.404/dotnet.dll'
dotnet $sdk10 build cli/agentic-box.slnx -c Debug -m:1 --nologo --verbosity minimal
dotnet cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll -method '*UnrelatedComputedProjectIdNeitherWarnsNorBlocksSelectedStoreGeneration' -noLogo -maxThreads 2
```

**Red: 1 failed, 0 passed.** The command returned **1**, not **0**, reporting:

```text
Warning: project "Deals\\backend\\src\\CS.Messaging.Deals\\CS.Messaging.Deals.csproj": UserSecretsId is computed, imported, conditional, invalid or unresolved and may affect the selected stores.
error: User-secret store 'sample', key "Endpoint", project "Api\\Api.csproj": cannot generate overrides because consumers could not be determined ...
```

The unrelated synthetic project's ID was `$(SyntheticPrivateId)` and its
appsettings was deliberately invalid JSON. A separate project explicitly declared
`SAMPLE` for selected store `sample`, with a mapped-local endpoint and no host
secrets file. After the change, **1 passed**: exact generated JSON was
`{"Endpoint":"http://host.docker.internal:8004/path"}`, without the warning.

## Verification

Used installed SDK **10.0.404** explicitly, and the direct xUnit executable rather
than `dotnet test` (the prior story record explains the runner-handshake limitation).

```powershell
dotnet tool restore
dotnet csharpier format cli
dotnet $sdk10 build cli/agentic-box.slnx -c Debug --no-restore -m:1 --nologo --verbosity minimal
dotnet cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll -class Abox.Tests.UserSecretsAppSettingsCommandTests -class Abox.Tests.UserSecretsSharedStoreCommandTests -class Abox.Tests.SplitSettingsCommandTests -class Abox.Tests.UserSecretsOracleCommandTests -noLogo -maxThreads 2
dotnet cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll -noLogo -maxThreads 2
dotnet $sdk10 build cli/agentic-box.slnx -c Release --no-restore -m:1 --nologo --verbosity minimal
dotnet cli/test/Abox.Tests/bin/Release/net10.0/Abox.Tests.dll -noLogo -maxThreads 2
dotnet csharpier check cli
git diff --check
```

| Check | Result |
| --- | --- |
| Focused discovery/appsettings/shared/split/Oracle | **322 passed, 0 failed, 2 platform skips** |
| Debug solution build | 0 warnings, 0 errors |
| Full Debug executable | **1173 passed, 0 failed, 7 platform skips** |
| Release solution build | 0 warnings, 0 errors |
| Full Release executable | **1173 passed, 0 failed, 7 platform skips** |
| CSharpier | Passed, 168 files checked |
| `git diff --check` | Passed |

The seven skips are not passes: four receiver symlink cases, two project-file
symlink cases, and one case-distinct host-directory case unsupported on this host.
Boundary fixtures now contain explicit matching IDs plus invalid appsettings, so
they detect unintended association even though unreadable XML is ignored. Shared
conflict tests also include an unrelated computed-ID project, preserving zero-write
protection in both discovery orders and normal/verbose output.

The original checkout's modified `run-abox.ps1` was not edited; its SHA256 remained
`F303DBA9D64E8B51D6BE9CC7F4F4ECCE59C0D1DD97A4CE71E292F71557E0D967`.
No live readiness, authentication/query, publication or independent Unix-host sbx
claim follows from these synthetic checks.
