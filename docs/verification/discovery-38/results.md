# #38 verification results

## Isolated performance measurement

Measured on Windows build 26200, x64, SDK 11.0.100, .NET runtime 10.0.15.
Builds came from archived Git sources, not the investigation worktree:

- Baseline: `7e37db305f96b75494802e30b9dfc4aa42a260d8`.
- Candidate: `6145ab0a2f85c3a19482956e4c4e8ef6b8bf8769` (isolated ancestry reuse,
  before integrating #39's exclusion).
- Command: `./docs/verification/discovery-38/Measure-Discovery38.ps1
  -ScratchRoot D:/dev/.tmp/opencode/opencode/performance-37
  -CandidateRevision 6145ab0a2f85c3a19482956e4c4e8ef6b8bf8769`.
- The committed [probe](Program.cs) generated one 12,776-directory synthetic
  snapshot, then warmed both implementations and alternated their execution order.
  Builds, generation, startup, policy and transfer were outside the timed interval.
  It reads no XML, appsettings or host secrets and performs no transfer.

| Discovery-only run | Baseline (ms) | Resolved-parent reuse (ms) |
|---|---:|---:|
| 1 | 13218.507 | 2841.969 |
| 2 | 16025.822 | 3308.495 |
| 3 | 17868.191 | 3888.991 |
| 4 | 16854.771 | 3266.430 |
| 5 | 16383.038 | 3493.694 |
| Median | **16383.038** | **3308.495** |

Median discovery was approximately **4.95x faster** (79.8% lower elapsed time) on
this synthetic snapshot. These are warm-cache local measurements with visible
machine variability, not a universal SLA or an end-to-end copying measurement.
The probe was scaled down from an initial larger diagnostic snapshot so that
repeated verification is practical; the stopped baseline-versus-itself diagnostic
produced no reported result and is not included in the comparison above.

Both implementations used unchanged case-insensitive `.git`, `bin`, `obj`
exclusions and traversed `node_modules`. All warmup/measured project sets were
compared in full and contained the same 430 relative project paths. Their normalized,
ordinal-sorted, newline-joined SHA-256 fingerprint was:

```text
1C9490DFA9FBD80AC9526C1F1B3537C7C2013C1FD5CA228286A043639478D892
```

[Aggregate machine-readable evidence](results-windows.json) retains every measured
sample plus platform/runtime/revision provenance. The script's defaults pin the
two isolated commits, so the same comparison remains runnable from a fresh checkout
after the independent exclusion/policy tickets merge. Override the revisions only
when comparing versions with the same exclusions; the probe rejects policy changes.

## Correctness verification (isolated #38)

| Check | Result |
|---|---|
| Debug solution build | Passed, zero warnings/errors |
| Windows focused new + appsettings/shared-store CLI suites | 174 passed, 9 skips, 0 failures (183 total) |
| Windows complete Debug suite | 1656 passed, 14 skips, 0 failures (1670 total) |
| Ubuntu 26.04 WSL focused suites, SDK 10.0.109/runtime 10.0.9 | 182 passed, 1 skip, 0 failures (183 total) |
| CSharpier `check cli` | Passed |
| CSharpier `check docs/verification/discovery-38` | Passed |
| `git diff --check` | Passed |

Windows junction cases actually executed: internal aliases/cycles, escaping sibling
targets, linked target ancestors, resolved `obj` targets, a linked workspace ancestor
with unchanged sandbox identity, and broken targets failing before any write.
Windows lacks native symlink privileges: seven new symlink cases and seven existing
capability-dependent cases skipped in the full suite. WSL ran all new Unix directory
and project/appsettings file-symlink cases. Its one focused skip is explicitly the
Windows-only broken-junction case; dangling Unix symlinks are not directories returned
by enumeration. The new helper treats unexpected junction-creation failures as test
failures, not skips.

The existing composed CLI harness exercised all-store validation, shared-consumer
conflicts, selected payloads, static association without MSBuild and unchanged source
bytes. The deterministic cancellation test uses the existing preparation seam and
an already-cancelled token, proving zero writes/sandbox calls and unchanged sources;
it does not claim a controlled cancellation midway through native synchronous I/O.
The full suite also retains sensitive process-output and all-store-before-write
coverage. The initial `dotnet test` attempt failed with the known runner handshake
error and zero tests; the counts above are actual direct-xUnit executions.

## Integration verification

Merged integration tip `5b48ba6b60ae6d41251ea1985dbd3d96d6fb5054` into
`implement/performance-38` as `e2ab471`, preserving #39's `node_modules` exclusion,
tests, documentation and benchmark artifacts. The production merge was automatic.
The isolated comparison above deliberately remains pinned before that independent
coverage change.

Reran the resolved combination:

- Debug solution build: passed with zero warnings/errors.
- Windows focused #38 + existing appsettings/shared-store + #39 suites: **191
  passed, 9 skips, 0 failures** (200 total).
- Windows complete Debug suite: **1673 passed, 14 skips, 0 failures** (1687 total).
- Ubuntu WSL focused same suites: **199 passed, 1 Windows-only skip, 0 failures**
  (200 total). Native Unix directory/file symlink cases executed successfully.
- `dotnet csharpier check cli`, `dotnet csharpier check
  docs/verification/discovery-38` and `git diff --check`: passed.

No real secret payload was read, no sandbox transfer was run, and no issue/PR action
was taken. The private diagnostic reflection binding is a maintenance caveat; native
Windows symlink privilege skips and the scope of the deterministic cancellation
check are stated above rather than treated as executed coverage.
