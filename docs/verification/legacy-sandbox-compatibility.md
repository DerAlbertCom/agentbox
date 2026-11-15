# Legacy sandbox compatibility (#10): verification

## Approved seam and source

The complete specification is [issue #10](https://github.com/DerAlbertCom/agentbox/issues/10).
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
   destination checks, same-port rewrite and transfer address that name. Missing,
   unknown and mismatched records still refuse, even with force and an unrelated
   matching preferred-name record.
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
focused filtering/compatibility set green (62 passed). Squad-qualified access still
waits for the selected identity's trusted record. Existing MCP-order tests also
caught and prevented moving discovery ahead of read-only MCP validation.

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

- On-demand copying names its shared host-preparation phase once. Unqualified
  validation remains before runtime access; squad-qualified host reads still wait
  for the selected identity's trusted record. Missing `abox.json` still fails first.
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
not atomic against concurrent runtime changes. Lookup derives one exact historical
candidate from the effective path spelling, not every alternative historical
trailing-separator spelling.
