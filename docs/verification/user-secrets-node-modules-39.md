# User Secrets node_modules boundary — #39 / spec #37

## Scope and implementation

Ticket branch `implement/performance-39` was created in its own worktree from
`integration/user-secrets-performance-37` at
`7e37db305f96b75494802e30b9dfc4aa42a260d8`. Implementation commit
`b39fe2d3f31d53fa66de8e5f696cf42307eb7077` adds `node_modules` to the existing
case-insensitive excluded-directory predicate. The existing lexical child-name
and workspace-relative resolved-target checks both consume that predicate.
No public API, location/sandbox identity, cache, additional exclusion or command
configuration was introduced. Parent-path reuse (#38) and policy concurrency
(#40) are independent work, not prerequisites for this evidence.

The approved boundary is explicit in the
[user-facing discovery documentation](../user-secrets-appsettings.md#bounded-discovery-not-application-startup):
dependency projects are not consumers for association or shared-store checks;
ordinary included consumers still receive complete validation before writes.
An explicitly selected root at or beneath `node_modules` is not silently narrowed.

## Behavior verification

`cli/test/Abox.Tests/Features/UserSecrets/NodeModulesDiscovery39Tests.cs` exercises
the existing composed CLI harness with synthetic files and a substituted sbx
adapter. No real stores or transfers are used.

- TDD red: the initial three nested/case-variant dependency fixtures failed before
  the production change, reporting the dependency consumer's invalid JSON.
  The same three passed immediately after the exclusion-policy entry was added.
- All **17 ticket-specific cases pass**, with **zero capability skips** on this
  Windows host. Nested `node_modules`, mixed/uppercase spellings, four similarly
  named ordinary directories, missing-host association, and explicit root
  selection at/beneath `node_modules` are covered through exit/payload behavior.
- Conflicting shared-store values, invalid JSON and conditional matching-ID
  declarations are ignored only under dependency exclusions. Corresponding
  ordinary-directory fixtures still fail with **zero writes to any store**,
  even with `--force` and verbose output.
- Four native Windows **junction** cases cover direct targets and targets with
  linked ancestors, both inside excluded trees and ordinary targets outside root
  containment (which cannot be protected just by the dependency-name exclusion).
  Ordinary aliases and a cycle are included in these layouts. Fixtures contain
  matching IDs and invalid JSON that would fail if followed. The same fixture
  creates Unix directory symlinks on supported Unix hosts; no independent Unix
  host run is claimed. Capability failures are explicit dynamic skips, not passes.
- Every case checks original source bytes/text remain unchanged. Console assertions
  suppress synthetic endpoint/private markers, including verbose execution.

Initial implementation verification on Windows x64, SDK **11.0.100**,
`net10.0`, Debug:

```text
dotnet test cli/agentbox.slnx -c Debug --filter FullyQualifiedName~NodeModulesDiscovery39Tests --nologo
  17 passed, 0 failed, 0 skipped
dotnet test cli/agentbox.slnx -c Debug --nologo
  1663 passed, 0 failed, 7 skipped (1670 total)
dotnet csharpier check cli
  210 files checked successfully
git diff --check
  passed
```

The seven existing full-suite capability skips are one case-distinct host-store
fixture, four receiver symlink fixtures, and two linked project-file fixtures.
Their platform limitations are not evidence of passing those behaviors here.
Directory junction coverage for this ticket ran successfully, not skipped.

## Reproducible safe measurement

The committed [standalone harness and fresh-checkout instructions](../../benchmarks/performance-39/README.md)
generate a synthetic snapshot and compare built production discovery implementations.
There is no dependency on the investigation worktree. Only internal discovery and
static empty-appsettings preparation are timed; assembly load, build, startup,
policy, transfer and source creation/hashing are outside intervals. No real secret
payloads, host User Secrets access or sandbox writes occur.

The applicable base is `7e37db3`; the after implementation is `b39fe2d`.
Both sides use the **original resolver**, isolating #39 without #38. Five recorded
samples follow one warmup per assembly, with alternating measurement order on the
same snapshot. These are warm-cache local diagnostics, not universal timings or
an end-to-end/SLA claim.

Two complete runs on **Microsoft Windows 10.0.26200, x64, .NET 10.0.15**
(SDK 11.0.100, Release builds) passed. The second run followed the documented
`git archive` reproduction, building **both** variants from archived commits into
a fresh scratch directory. The first run used an archived base and the ticket
worktree's after build; other verification work was also active on this shared
machine. Absolute path depth, filesystem/cache state and other machine activity
can affect this original ancestry resolver, so compare paired samples within a
run rather than cross-run absolute times.

| Sample | Initial before (ms) | Initial after (ms) | Fresh archived before (ms) | Fresh archived after (ms) |
| --- | ---: | ---: | ---: | ---: |
| 1 | 12529.94 | 2820.29 | 14206.90 | 3608.25 |
| 2 | 12789.65 | 2923.95 | 13850.53 | 3469.81 |
| 3 | 13467.89 | 3348.32 | 13036.41 | 3723.29 |
| 4 | 17025.86 | 3003.32 | 14990.89 | 3551.08 |
| 5 | 12878.77 | 3145.61 | 13964.14 | 3713.07 |
| **Median** | **12878.77** | **3003.32** | **13964.14** | **3608.25** |

Both runs reported:

| Inventory / validation | Before | After |
| --- | ---: | ---: |
| Reachable ordinary synthetic directories, including root | 10253 | 2448 |
| Discovered matching projects in every sample | 414 | 402 |
| Project differences outside dependency boundaries | 0 | 0 |
| Original source-content mutations | 0 | 0 |

The twelve intentionally removed consumers are ten
`web/node_modules/package-0000..0009/lib/nested/Synthetic.csproj` files,
`web/node_modules/package-0000/node_modules/child/Synthetic.csproj`, and
`web/nested/NoDe_MoDuLeS/package/Synthetic.csproj`. The 400 ordinary project
consumers and both similarly named directories remain discoverable. These are
**inventory counts**, not instrumentation of filesystem calls.

Reproducible content fingerprints (same in both runs):

```text
Synthetic input SHA256:
F104F911B88263EBB030A1D880A81AEA3F9503ED1378E00DC88E2B4254CD43A3
Included project-set SHA256:
10E7986378E7B6F7C1F4678ED4488839D1CE7AB07A9765D9C02E72DEEDA008F6
Fresh archived before assembly SHA256:
51C0B64B3D6A10EE46EB351CD33126B46799620646028DBFAFB4361A6D43CAB2
Fresh archived after assembly SHA256:
DA3BD15E7E4F181C8FE602FC8F04BA02AFE08CAFBC0D972C5427DF1AAFF90C74
PASS: all samples match expected sets, no differences outside node_modules, source snapshot unchanged.
```

Assembly hashes identify the measured builds, not a portable build-output hash
promise (build locations can differ). Prototype observations in the issue
(roughly 26 s to 1.3 s without parent reuse, about 0.5 s combined) concern another
snapshot/machine state and are not asserted by this synthetic measurement.

Additional focused verification passed **181 tests**, with zero failures and the
two existing file-symlink capability skips, using the filter
`NodeModulesDiscovery39Tests | UserSecretsAppSettingsCommandTests | UserSecretsSharedStoreCommandTests`.
The explicit Debug solution build and both archived Release production builds
completed with **zero warnings/errors**. The standalone benchmark build and
`dotnet csharpier check cli benchmarks/performance-39` passed (212 source/project
files checked); `git diff --check` passed.
