# Bounded user-secret policy authorization (#40)

## Contract and scope

`SbxClient.GetUserSecretsHostPortsAsync` retains one strict inventory lookup and
the existing candidate normalization and strict authorization parser. Four fixed
workers check all candidates, independent of selected endpoint usage. Each result
is retained at its candidate index; no authorized list escapes until every
successful-path check completes. A failed worker cancels its peers, and the request
awaits every worker (including process cancellation cleanup) before throwing a
value-free error. Caller cancellation remains cancellation. There is no public
concurrency option, cache, discovery dependency or transfer change.

The implementation started from integration commit
`7e37db305f96b75494802e30b9dfc4aa42a260d8` on its own
`implement/performance-40` branch/worktree. Only the policy adapter's implementation
changes; the added tests and benchmark use existing public seams.

## Reproduce safely from a fresh checkout

Prerequisites: a .NET SDK supporting `net10.0` and the repository's NuGet packages.
Run from the repository root:

```sh
dotnet --info
dotnet tool restore
dotnet build cli/benchmark/Performance40.Policy -c Release
dotnet run --project cli/benchmark/Performance40.Policy -c Release --no-build -- 5
```

The final argument is the repetition count (at least two; default five). The
standalone harness substitutes `IProcessRunner`: it never launches `sbx`, Docker
or any other child process, reads no workspace/configuration/store inputs, and
performs no discovery, sandbox startup or transfer. Building writes only ordinary
build outputs. All policy metadata is generated from fixed, non-secret literals.
No investigation worktree or live sandbox is needed.

Both modes call the **current production SbxClient and strict parsers**, not a
duplicate policy implementation. The serial baseline limits the synthetic runner
to one active check; the bounded mode permits four. This models serialized versus
overlapped independent process latency; it is **not execution of an old abox
binary** or a measurement of real process startup. The generated snapshot is the
same fourteen candidates in both modes, with three effective denials and eleven
allowed ports. Each inventory has a synthetic 75 ms delay, each check 125 ms.
Before reporting any row, the harness requires exactly one inventory, fourteen
checks, the expected measured concurrency and an independently listed ordered
result. Every row reports equivalence and the authorized-port SHA-256 fingerprint.

Both modes warm up once without reporting. Five measured pairs alternate which
mode runs first, in the same process, with identical initialized snapshot/cache
conditions. Build and startup are outside the stopwatch. `inventory_ms` measures
the inventory runner call; `authorization_and_parsing_ms` is the rest of the
public policy call (including parsing/scheduling); `total_policy_ms` is their sum.
Do not compare these synthetic numbers with discovery or whole-command timings.

## Repeated evidence

Recorded on 2026-11-15: Windows `10.0.26200`, x64; SDK `11.0.100`, target
`net10.0`, runtime `.NET 10.0.15`; Release harness, commands above. All ten rows
reported 14 candidates, 11 authorized, equivalent results and fingerprint:

```text
FB672D71031F616B0784E4DA022374F98C161629E43FF334F025DFA3E86EAB07
```

Times in milliseconds; row order is execution order:

| Repetition | Measured check concurrency | Inventory | Authorization + parsing | Total policy |
| --- | ---: | ---: | ---: | ---: |
| 1 | 1 | 74.70 | 1765.85 | 1840.55 |
| 1 | 4 | 75.40 | 541.12 | 616.52 |
| 2 | 4 | 93.82 | 506.55 | 600.37 |
| 2 | 1 | 74.86 | 1890.09 | 1964.95 |
| 3 | 1 | 91.65 | 1776.60 | 1868.25 |
| 3 | 4 | 74.41 | 503.30 | 577.70 |
| 4 | 4 | 74.53 | 501.97 | 576.50 |
| 4 | 1 | 76.30 | 1757.50 | 1833.79 |
| 5 | 1 | 75.66 | 1762.80 | 1838.46 |
| 5 | 4 | 74.58 | 503.18 | 577.77 |

Synthetic total policy ranges were **1.834–1.965 s serialized** and
**0.577–0.617 s bounded**. The parent investigation's separate live read-only
reference was inventory plus 14 checks at **2.19–2.38 s serial** versus
**0.91–1.07 s with four workers**; that reference was not rerun here. Neither set
establishes end-to-end transfer gains, service reachability, an SLA or performance
on another host. No real secrets were read or copied.

## Correctness verification

The ticket-specific suites are:

- `Sbx/Performance40PolicyConcurrencyTests.cs`: completion gates establish overlap,
  a four-check ceiling, normalization/deduplication, all-candidate coverage,
  deterministic mixed allow/deny results, and waiting for the last candidate.
  Zero/one/more-than-four candidates, pre-cancellation, in-flight cancellation and
  process, parse, protocol, sandbox, target and schema failures are covered.
  Cancellation cleanup is separately gated so an early return cannot pass.
- `Features/UserSecrets/Performance40PolicyCommandTests.cs`: the composed real CLI
  with synthetic stores and the real policy adapter behind substituted process
  operations blocks **every** destination write on concurrent failures or
  cancellation, even for otherwise valid multi-store selections and `--force`.
  Checks remain pending until cleanup finishes. Sources stay unchanged, verbose
  diagnostics are value-free, unused candidates must complete, and a second
  invocation observes a changed effective authorization rather than a cache.
- `Infrastructure/Performance40SensitivePolicyProcessTests.cs`: actual synthetic
  PowerShell/Unix-shell children produce metadata and sensitive-looking scalar
  fragments concurrently. A shared completion gate releases their policy results.
  Normal/verbose modes retain metadata for strict parsing while suppressing all
  raw child stdout/stderr, including malformed output and nonzero exits.

No correctness test uses a stopwatch threshold or sleep to establish concurrency.
Timeouts are deadlock guards only. Existing strict-parser, process and command
tests remain unchanged.

```sh
dotnet test --project cli/test/Abox.Tests -c Debug -- --filter-class '*Performance40*'
dotnet test --project cli/test/Abox.Tests -c Debug -- --filter-class '*Policy*'
dotnet test --project cli/test/Abox.Tests -c Debug -- --filter-class '*Process*'
dotnet test --project cli/test/Abox.Tests -c Debug -- --filter-class '*CommandTests'
dotnet test --project cli/test/Abox.Tests -c Debug
dotnet build cli/agentbox.slnx -c Debug
dotnet csharpier check cli
git diff --check
```

Initial verification: ticket-specific **29 passed**; policy **129 passed**;
process **31 passed**; command **570 passed, 2 skipped**; full Debug **1675 passed,
7 skipped, zero failures**. The seven capability skips concern native file
symlinks/receiver symlinks and case-distinct directories on this Windows host;
they do not count as passing, and no new ticket test was skipped. Release was
used only for the standalone benchmark; no Release test run or live sandbox
operation is claimed.
