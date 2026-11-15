# Issue #13: appsettings endpoint override verification

Verified on Windows, 2026-11-15, against integration baseline `6eaab38`.
Only synthetic temporary workspaces and injected host-store roots were used.
No developer secret stores, live application services, existing sandboxes,
registry publication or global sbx settings were accessed/changed for this work.

## Red/green evidence and seam

`UserSecretsAppSettingsCommandTests` exercises the existing CLI with the existing
`UserSecretsCliHarness` and a substituted `ISbxClient`. No production public test
interface was added. The first tracer failed because a selected project-only
store had no host secrets file; it then passed with the generated JSON override.
Subsequent red/green slices covered development precedence, artifact/link
discovery, unsupported association diagnostics, loopback URL/PostgreSQL formats,
generated-original deny checks, temporary shared-store rejection and unsafe
mapped-local adaptation. Unsafe cases were exercised in both source kinds and
both normal/verbose modes with zero destination writes.

Assertions inspect emitted JSON, unchanged source bytes, value-free diagnostics,
all-store no-write failures and no build process invocation. Synthetic projects
include a build target that would fail if executed, and the command's process
adapter receives no calls in that discovery test. Filesystem link fixtures use
Windows junctions (Unix symlinks on Unix); they cover external escapes, indirect
linked parents, internal aliases and cycles without requiring Windows native
file-symlink privileges.

`UserSecretsLifecycleTests` extends the same command seam to local/published
creation, both explicit replacement kinds and accepted replacement from `run`.
Project-only generation uses current development configuration; invalid project
JSON prevents destructive replacement, and ordinary runs do not discover even
malformed projects. Repeated explicit copying proves stale generated keys vanish.
Existing copy/filter/safety/policy/transport suites remain regression coverage;
historical preservation cases newly required to fail are now at the CLI seam.

## Final checks

| Check | Result |
| --- | --- |
| `dotnet build cli/agentbox.slnx -c Debug --no-restore --verbosity minimal` | Passed; 0 warnings, 0 errors |
| `dotnet test cli/agentbox.slnx -c Debug --no-build --verbosity minimal` | 918 passed, 0 failed, 5 platform skips (923 total) |
| `dotnet build cli/agentbox.slnx -c Release --verbosity minimal` | Passed; 0 warnings, 0 errors |
| `dotnet test cli/agentbox.slnx -c Release --no-restore --verbosity minimal` | 918 passed, 0 failed, 5 platform skips (923 total) |
| `dotnet csharpier check cli` | Passed |
| `git diff --check` | Passed |

The five existing platform skips are four native-symlink receiver cases lacking
Windows link privileges and one case-distinct host-directory collision case on a
case-insensitive filesystem. Skipped cases are not claimed as passing; new
junction-based discovery cases ran successfully.

This verifies emitted configuration and fail-closed behavior, not service
availability, database authentication, arbitrary application provider ordering or
an independent Unix-host/live sbx run. The existing accepted live-policy matrix
and its [published/nonempty-policy limitation](user-secrets/README.md) are unchanged.
Shared-store generation remains temporarily rejected; Oracle, recognized split
settings and compatible shared-store resolution are separate tickets under #12.
