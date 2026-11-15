# Ticket #39: safe dependency-exclusion discovery measurement

This standalone diagnostic compares two built production `abox.dll` assemblies
against **one generated synthetic snapshot**. It calls the existing internal
`ProjectSettings.ReadAsync` by reflection, without adding a product API. It does
not invoke `abox`, `UserSecretsCopy`, `sbx`, Docker, policy checks or transfer.
It never discovers a real workspace or reads host User Secrets. Every synthetic
project uses the sentinel `benchmark-39-no-real-store` and empty `{}` appsettings;
there are no secret payloads or sandbox writes. The only snapshot it can discover
is a unique child it creates in the supplied scratch directory and removes in
`finally`. Build inputs/outputs and package restore remain ordinary .NET SDK work.

## Reproduce from a fresh checkout

Requirements: Git (with both compared commits available), `tar`, .NET SDK supporting
the repository's `net10.0` target, and PowerShell 7 for the following commands.
Run from the repository root. Choose a disposable scratch parent, **not** a source
workspace or a User Secrets directory. No investigation worktree is needed.

```powershell
$scratch = Join-Path $HOME ("agentbox-discovery-39-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $scratch | Out-Null
$before = '7e37db305f96b75494802e30b9dfc4aa42a260d8'
$after = 'b39fe2d' # #39 alone, without parent-path reuse or policy concurrency
foreach ($pair in @(@('before', $before), @('after', $after))) {
    $name, $revision = $pair
    $directory = Join-Path $scratch $name
    New-Item -ItemType Directory $directory | Out-Null
    git archive --format=tar --output="$scratch/$name.tar" $revision
    if ($LASTEXITCODE) { throw 'git archive failed' }
    tar -xf "$scratch/$name.tar" -C $directory
    if ($LASTEXITCODE) { throw 'tar failed' }
    dotnet build "$directory/cli/src/Abox/Abox.csproj" -c Release --nologo
    if ($LASTEXITCODE) { throw 'production build failed' }
}
dotnet build benchmarks/performance-39/Discovery39.csproj -c Release --nologo
if ($LASTEXITCODE) { throw 'benchmark build failed' }
dotnet run --no-build -c Release --project benchmarks/performance-39/Discovery39.csproj -- `
    "$scratch/before/cli/src/Abox/bin/Release/net10.0/abox.dll" `
    "$scratch/after/cli/src/Abox/bin/Release/net10.0/abox.dll" $scratch
if ($LASTEXITCODE) { throw 'discovery or project-set verification failed' }
```

After saving any desired console evidence, remove only the scratch directory you
created. The benchmark's generated source snapshot is automatically removed.

The recorded pair isolates **only #39 against its actual integration base**, with
the original resolver on both sides: parent-path reuse (#38) is not a dependency.
To measure another pair, replace the revisions with comparable before/after
commits. A pair that already excludes dependencies on both sides intentionally
fails this harness's expected before-set assertion; it would not measure #39.

## What is measured

- 1,200 ordinary project-shaped directories, with 400 matching projects; 2,600
  three-level dependency directories, with ten matching projects; nested and
  mixed-case `node_modules` consumers; and two similarly named included consumers.
- Snapshot creation, inventory/hashing, assembly loading, selection parsing,
  startup and build are **outside** measured intervals. Intervals include real
  production directory traversal, XML association and static empty-appsettings
  reading/validation. They do not measure policy, sandbox startup or transport.
- Each assembly gets one unrecorded warmup. Five samples per assembly then
  alternate before/after order on the exact same snapshot in the same process.
  These are warm-cache observations, not cold-disk guarantees. Run the entire
  experiment again for another independent snapshot/run; do not flush OS caches
  or add timing assertions to correctness tests.
- Every sample's discovered project paths must equal an independently enumerated
  expected set. The only allowed difference is the twelve consumers under
  case-insensitive `node_modules` components. Fingerprints, removed relative paths,
  project counts and directory inventory counts are printed. Directory counts
  describe the reachable synthetic inventory, **not measured filesystem calls**.
  A final content fingerprint checks source immutability.
- This snapshot has no links; filesystem link/security behavior is tested through
  the composed CLI, not inferred from this timing harness.

## Recorded results

Results and correctness evidence are recorded in
[the ticket-specific verification note](../../docs/verification/user-secrets-node-modules-39.md).
Local stopwatch results are not universal guarantees, end-to-end timings or an SLA.
