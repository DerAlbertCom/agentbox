# Legacy sandbox compatibility (#10): verification

## Approved seam and source

The original specification is [issue #10](https://github.com/DerAlbertCom/agentbox/issues/10).
The subsequent user-requested on-demand squad-policy correction below supersedes
its historical user-secret record prerequisite, not its replacement safeguards.
Tests invoke the composed root CLI with the real shared resolver and fake external
sbx interaction. Inventory tests additionally use the real sbx adapter with a fake
process runner to exercise JSON parsing, exact matching and failed listings.
Historical path/name fixtures supplement this command seam; no test-only product
interface was introduced.

Reference source, inspected locally without changing that repository:

- [Create-AgentSandbox.ps1, lines 148–154](https://rwe.ghe.com/supply-and-trading/cao-docker-ai-sandbox/blob/1512ff71c31c6bfa7e923a43683c6d80624c40d0/Create-AgentSandbox.ps1#L148-L154): generated naming.
- [AgentSandbox.Sbx.psm1, lines 402–423](https://rwe.ghe.com/supply-and-trading/cao-docker-ai-sandbox/blob/1512ff71c31c6bfa7e923a43683c6d80624c40d0/AgentSandbox.Sbx.psm1#L402-L423): Windows full/final-link path and Unix physical-directory/fallback rules.

`LegacySandboxNamingTests` contains independent literal expected names produced
from that historical code, with Unix absent-path SHA-256 fixtures cross-checked
using Python `hashlib`. Examples: Windows `C:\__agentbox_legacy_fixtures__\Case_Name`
is `case_name-ai-sbx-cca0eb`, while retaining its trailing backslash yields
`case_name-ai-sbx-c1b1cb`. Lowercase path spelling yields `case_name-ai-sbx-0c1bf5`.
The Unix absent path `/__agentbox_legacy_fixtures__/Case_Name` is
`case_name-ai-sbx-e479dd`. Fixtures cover case, separators, trailing separators,
underscores, punctuation, non-ASCII sanitization and empty prefixes. A filesystem
link test checks the original alias prefix plus the final-target digest. Dynamic
temporary-folder acceptance fixtures calculate only their hash with an independent
test-data oracle, never by calling production naming/resolution helpers; the
literal fixtures prevent shared preferred-name assumptions hiding naming regressions.

## Vertical red → green evidence

Each feature slice was tested failing before its implementation:

1. **Run reuse:** the running/stopped CLI cases both failed (`expected 0, actual 1`)
   before run used the shared resolver; both passed after introducing fallback.
2. **Secret identity:** both squad-qualified and unqualified legacy copies failed
   (`expected 0, actual 1`) before on-demand authorization/copy used the selected
   identity; both passed after integration. Positive tests assert policy discovery,
   destination checks, same-port rewrite and transfer address that name. That
   revision also refused missing, unknown and mismatched records; this prerequisite
   is superseded by the on-demand policy correction below.
3. **Create replacement:** both consent cases failed before creator integration:
   declining incorrectly succeeded with a new agentbox sandbox, and accepting
   skipped the legacy replacement prompt. Both passed after shared lookup and
   carrying the selected name through removal, state paths, creation and records.
4. **Accepted run replacement:** after correcting test setup to supply valid local
   build metadata, the test failed because removal addressed `folder-abox-…`
   instead of the selected `folder-ai-sbx-…` when the current candidate appeared
   after selection. Passing the already-selected sandbox into creation fixed it.
   The test now covers accepted and declined consent; decline launches the legacy
   environment without reading the deliberately missing selected host store.

The first full regression run caught six existing filtering tests failing because
unqualified host validation had moved after runtime access. Restoring host-only
preparation before preflight/discovery when no squad check is required made the
focused filtering/compatibility set green (62 passed). At that revision,
squad-qualified access waited for the selected identity's record; the correction
below removes that wait. Existing MCP-order tests also caught and prevented moving
discovery ahead of read-only MCP validation.

Regression coverage additionally checks current-only/legacy-only/both/neither
inventories, lookalikes/custom workspace names, runtime/permission/malformed-JSON
failures, cancellation, effective-location precedence, Herdr identity/probes,
no fabricated records, ownership refusal/value-free output, explicit-name exec/
pipe and unchanged inventory-wide prune scope. Compatibility itself performs no
create/remove/rename/migration. Explicitly confirmed replacement is tested separately,
including `--yes`, retained name/state and post-create copying.

## Commands and execution limits

### Review-stage standards cleanup

The four minor standards suggestions were addressed without changing the approved
CLI seam or adding behavior:

- On-demand copying named its shared host-preparation phase once. At that revision,
  only unqualified validation preceded runtime access; the correction below makes
  host preparation unconditional. Missing `abox.json` still fails first.
- Run computes a current-name suggestion only in its missing-sandbox branch, then
  uses `sandbox.Name` throughout the existing-sandbox path. No creator change is
  needed: `existing?.Name ?? SandboxName.For(location)` already short-circuits,
  deriving a new name only when no existing sandbox was selected.
- Historical real-path I/O now lives privately in `SandboxResolver`; naming retains
  only pure sanitization/hash logic. Windows final-link and Unix physical/fallback
  behavior is unchanged, with no new public interface or general path abstraction.
- `SandboxTest.Resolver` shares existing service-test construction, and
  `LegacySandboxFixture` shares the independently sourced literal identity across
  the three compatibility suites. No command coverage or historical fixture case
  was removed, and expected legacy identities never call the production helper.

This is review-stage refactoring, not a new red/green feature slice. The focused
run covering compatibility, on-demand command/filtering/lifecycle, create command/
service and run service tests passed **260 tests** both before and after the edits.

Use the Microsoft.Testing.Platform/xUnit syntax configured by this repository:

```sh
dotnet test --solution cli/agentbox.slnx -- --filter-class '*LegacySandboxCompatibilityTests' --filter-class '*LegacySandboxNamingTests' --filter-class '*LegacySandboxInventoryTests'
dotnet test --solution cli/agentbox.slnx
dotnet build cli/agentbox.slnx --no-restore
dotnet build cli/agentbox.slnx --configuration Release --no-restore
dotnet csharpier check cli
git diff --check
```

Final post-review Debug suite: **796 passed, 4 explicit native-Windows-symlink-privilege skips,
800 total, zero failures**. Debug and Release solution builds both completed with
**zero warnings/errors**. CSharpier checked all 159 C# files; `git diff --check`
passed. The focused compatibility suite contains 43 Windows-host cases.

Execution is on a native Windows host. The Windows junction case needs no symlink
privilege. The pre-existing receiver tests requiring native Windows symlink creation
may explicitly skip on this host; they are not counted as passing. Unix fixture
definitions and implementation are not independent Unix-host execution evidence.
No live sbx sandbox, real host secret, registry, persistent global setting or Herdr
registration was changed. Port-command acceptance remains with #3; compatibility
does not implement forwarding or broaden discovery to arbitrary custom names.
Current and fallback lookups can read separate inventory snapshots; discovery is
not atomic against concurrent runtime changes. At that revision, lookup derived one
exact historical candidate from the effective path spelling. The Windows spelling
correction below adds a verified primary-workspace fallback after exact lookups.

## On-demand user-secrets squad-policy correction

The user subsequently reported that `user-secrets` failed with the squad-record
error and explicitly required **no squad verification for on-demand copying**.
That instruction supersedes the earlier on-demand provenance requirement in #2's
completion comments and #10. It does not change create/run replacement checks.

The regression uses the approved composed CLI, real resolver, synthetic folder
with `{"squad":"ac","userSecrets":["sample"]}`, synthetic empty store and fake
external sbx. No live user command, actual host store or user sandbox was accessed.

Red command, before editing production code:

```sh
dotnet test --project cli/test/Abox.Tests -- --filter-method '*OnDemandCopyDoesNotRequireASandboxSquadRecord*'
```

The one test failed in about one second, exposing the exact reported error:

```text
Expected (fixture.Run()) to be 0 because error: Cannot verify the sandbox's squad against abox.json. Create it with abox create for this folder before replacing it or copying user secrets., but found 1 (difference of 1).
```

The cause was the on-demand command applying the replacement squad guard before
host preparation. Removing only its guard/record dependency and making shared host
preparation unconditional made the same command pass. The replacement guard itself
is retained; its comment/error now describe replacement rather than on-demand copying.

Current and exact legacy-name regressions cover missing, matching, unknown and
mismatched records, successful transfer to the selected identity, no fabricated
records and no creation/removal. Squad-present deny/force and policy/destination/
transfer-failure tests retain the other safety checks. Obsolete on-demand refusal
assertions were replaced, while local/published/accepted-run replacement conflict
tests remain. Shared policy authorization and sensitive receiver tests are unchanged.

Verification after the correction:

- Original red command: **4 passed** after expanding the record-state matrix.
- Focused command/lifecycle/legacy-compatibility tests: **131 passed**.
- Full Debug solution suite: **799 passed, 4 existing native-Windows-symlink-privilege
  skips, 803 total, zero failures**.
- Debug and Release solution builds: **zero warnings/errors**.
- CSharpier: **159 files checked**; `git diff --check` passed.

This remains Windows-host synthetic verification, not a new live-sbx or independent
Unix-host run. The user's uncommitted `run-abox.ps1` is excluded from this correction.

## Windows legacy workspace spelling correction (PR #48)

The user reported that `ports` could not find the existing legacy sandbox for
`d:\dev\rwe\`, although its recorded workspace was `D:\dev\rwe`. The historical
case-sensitive path hash is `41bc30` for the recorded spelling, `b8bd16` with only
the drive lowercased, and `a7ad9b` with only a trailing backslash appended.
`Path.GetFullPath` preserves these differences. Both `ports` and `user-secrets`
already called the same resolver; this was not a missing ports-only compatibility
path. No historical naming/hash rule was changed.

Before production edits, the composed CLI/real sbx adapter with a substituted
process runner reproduced the exact missing-sandbox message twice: 3 controls
passed and 4 cases failed. Adding the uppercase-drive/trailing-separator control
also failed independently. Expanded pre-fix cases reproduced whole-path casing,
combined spelling changes, ambiguity handling and fallback-inventory errors.

The fix retains current identity precedence (including its Windows no-trailing-
separator equivalent), then exact legacy identity precedence. Only after genuine
absence does Windows match the runtime's primary workspace and recompute the
historical identity from its recorded spelling/real path. It returns the actual
identity, never renames or records an alias. Custom names, wrong hashes, other
folders, secondary/relative/missing workspaces and ambiguous verified matches are
not adopted. Inventory errors and cancellation remain failures, not absence.
Unix performs no new case-insensitive fallback; the historical symlink tests remain
unchanged.

Regression commands:

```sh
dotnet test --project cli/test/Abox.Tests -- --filter-method '*LegacyWorkspaceLookupSurvivesWindowsDriveCaseAtTheComposedCli*' --filter-method '*RecordedRweLegacyInventoryIsRecognizedByTheComposedPortsLookup*'
dotnet test --project cli/test/Abox.Tests -- --filter-class '*LegacySandboxInventoryTests' --filter-class '*LegacySandboxNamingTests' --filter-class '*LegacySandboxCompatibilityTests'
```

The first command now passes **8 cases**. The second passes **67 cases**, with
**1 explicit Unix-host-only skip** on this Windows host. Tests cover ordinary ports,
on-demand user secrets, run identity propagation and create's retained replacement
consent. The exact reported-name fixture is reconstructed from the known identity
and workspace, not a raw live inventory capture; it uses the ports refresh action
only to skip real-workspace reads, with all refresh transport substituted. Ordinary
ports and user-secrets cases use isolated temporary folders and no selected stores.

Final verification on this Windows host:

- Debug and Release solution builds: **zero warnings/errors**.
- Complete Debug and Release suites: each **2058 passed / 22 explicit skips /
  0 failures**, 2080 total. The additional Unix-only case is skipped, not claimed
  as Unix execution evidence.
- CSharpier: **254 files checked**; working/staged whitespace checks passed.
- Isolated clean-code comparison against `6793de5b7019aea7a57da8bf415068412eb42048`
  across all six changed source/test files: **UNCHANGED**, no maintainability
  regressions. The resolver remains **10/10**; this does not clear unrelated
  whole-branch debt.

No live sbx command, sandbox/process/mapping mutation, certificate export/trust,
create/recreate, or user launcher edit was performed. The user's actual `abox`
binary was not discoverable on this agent process's PATH. This synthetic regression
does not claim that the user's live command was rerun successfully. AC9 remains
postponed/nonblocking, not passed; existing whole-branch review findings are not
cleared by this focused fix.
