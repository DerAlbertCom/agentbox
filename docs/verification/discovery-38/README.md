# Resolved-parent discovery verification (#38)

## Scope and safety

`ProjectSettings` resolves the workspace once. Enumeration starts with that resolved
path and passes ordinary child paths directly to the next descent. Every included
child is still checked for a directory link. Link targets go through the full
ancestor-aware resolver before containment, resolved-target exclusions and the
invocation-local visited set are applied. Exclusions, ordering, static XML
association, ordinary-file checks and all-store-before-write validation are unchanged.
There is no persistent cache, new public API or MSBuild evaluation.

The behavior tests were added and run against the original implementation before
the optimization. Because this ticket preserves behavior, those are characterization
tests, not an invented correctness failure. Performance is measured separately;
ordinary tests contain no timing threshold or filesystem-call-count assertions.

## Reproduce the safe performance comparison

From a fresh checkout with Git, PowerShell 7 and a .NET 10-capable SDK/runtime:

```powershell
./docs/verification/discovery-38/Measure-Discovery38.ps1 `
  -ScratchRoot D:/dev/.tmp/opencode/opencode/performance-37
```

Use any disposable writable scratch parent on another host. The script creates a
unique child and retains only archived source builds, revision/SDK provenance and
aggregate JSON results there. The generated synthetic snapshot is deleted by the
probe in `finally`. Nothing under an existing snapshot path is accepted or deleted.
The script exports committed Git revisions (uncommitted edits are not measured).
`-BaselineRevision` defaults to integration base
`7e37db305f96b75494802e30b9dfc4aa42a260d8`; `-CandidateRevision` defaults to the
isolated #38 implementation `6145ab0a2f85c3a19482956e4c4e8ef6b8bf8769`.
When later testing a branch with #39's independent exclusion change, choose the
isolated #38 implementation commit recorded in the results rather than attributing
that exclusion gain to ancestry reuse. The probe fails if the exclusion policies
differ; it does not silently change either implementation.

The probe builds independently of the solution and loads each compiled version's
existing private discovery iterator and root resolver by reflection. That binding
is diagnostic-only, intentionally not a new production/public testing seam. A
future signature change may require updating it. No CLI command is invoked, no
project XML/appsettings/host store contents are read, and no sandbox, policy,
process runner, transfer or real-secret payload is involved. It measures filesystem
project discovery only, excluding builds, snapshot creation, process startup, policy
authorization and transfers. Path normalization/sorting is included identically in
both timings. Only counts, relative-project-set SHA-256 and timings are emitted.

The deterministic synthetic broad workspace has 430 ordinary projects under eight
nested directory levels, 100 dependency branches under `node_modules` (eight levels,
ten leaf directories per level), and excluded `.git`/`bin`/`obj` sentinel projects.
Both implementations include the dependency directories in the traversal. Both use
the very same snapshot in the same process, each with a fresh visited set each run.
One unmeasured warmup per implementation precedes five measured runs each, alternating
baseline/candidate order. The probe checks the complete sorted relative project sets
on every run, not just counts, and requires exactly 430 projects. This is a warm-cache
synthetic comparison, not a cold-cache or end-to-end SLA; real layouts and link-heavy
workspaces may differ. The synthetic timing tree has no links; link safety comes
from behavior tests, not the benchmark.

## Behavior and verification commands

```powershell
dotnet build cli/agentbox.slnx -c Debug
$tests = 'cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.exe'
& $tests -noLogo `
  -class Abox.Tests.UserSecretsDiscovery38CommandTests `
  -class Abox.Tests.UserSecretsDiscovery38CancellationTests `
  -class Abox.Tests.UserSecretsAppSettingsCommandTests `
  -class Abox.Tests.UserSecretsSharedStoreCommandTests
& $tests -noLogo
dotnet csharpier check cli
dotnet csharpier check docs/verification/discovery-38
git diff --check
```

On Unix run `dotnet cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll` instead
of the `.exe`. The direct xUnit executable is deliberate: this host's existing
`dotnet test` / Microsoft.Testing.Platform handshake fails before running tests.
No `dotnet test` success is claimed.

The ticket-specific CLI tests cover nested ordinary shared consumers in both
discovery orders, equivalent and conflicting settings, immutability, no MSBuild,
source suppression and zero writes on validation failure. They also cover in-root
links, sibling-prefix escapes, targets with linked ancestors, aliases, cycles,
resolved-target `obj` exclusion, lexical sandbox identity through a linked workspace,
broken junction fail-closed traversal, and project/base/development appsettings
file-link requirements. The existing suites retain their broader static-association,
selection, sensitive-output and validation coverage.

Cancellation is exercised through the existing public `UserSecretsCopy.PrepareAsync`
seam with an already-cancelled token, asserting unchanged sources and no sandbox
operations/writes. This is deterministic cancellation before preparation, not a
claim of a controlled cancellation in the middle of a synchronous native directory
call; no new filesystem/cancellation test API was introduced. Existing traversal
and per-project cancellation checks remain, with an additional check on iterator
entry before even an empty directory is enumerated.

Directory-link cases use Windows junctions and native Unix symlinks. An additional
Windows directory-symlink variant and file-symlink cases report privilege-based
skips. Broken-junction traversal is Windows-specific: Unix directory enumeration
does not return dangling symlinks, so that one case reports its platform skip there.
Junction creation failures are test failures, not blanket capability skips.

See [results.md](results.md) for measured results and actual host coverage.
