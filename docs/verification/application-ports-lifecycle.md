# Creation/replacement app-port setup (#47)

## Scope

Implementation builds on `7274f59a5d6c3d94db922f86c53f616b493f6fa6` and reuses
the existing `ApplicationPorts`, development HTTPS and sbx modules. The agreed test
seams are composed create/run commands through real shared lifecycle/orchestration,
with synthetic host files/certificate CLI output and substituted external adapters;
one transport test also uses the real `SbxClient` with a substituted process runner.

This is not evidence of registry publication, a new Linux host, real-host renewal,
or a new browser/Herdr run. #46's [Windows acceptance clarification](https://github.com/DerAlbertCom/agentic-box/issues/46#issuecomment-6918879462)
remains in force: Copilot is excluded, independent Linux-host proof is deferred,
and the retained Deals 18203/manual browser result is distinct from disposable
production-flow evidence. Existing Deals and the user's root script must not change.

## Behavioral matrix

`cli/test/Abox.Tests/ApplicationPorts/AppPortsLifecycle*` currently covers 53 cases:

- Local and published creation without folder configuration; automatic table and
  default offset after record save; no host certificate access for Angular-only,
  non-Web .NET or empty folders.
- Mixed .NET/Angular mappings, folder offset and selected user-secret copying before
  ports, activation and verification; no sensitive certificate child diagnostics.
- Explicit local/published replacement and accepted newer-local-build replacement
  from run; certificate export/activation requests the current **synthetic** identity
  again on replacement. This does not renew or access the real host identity.
- Entry-time offset snapshots, including missing configuration, survive later file
  edits. Legacy actual identity is retained for creation, records, ports and launch
  despite a new candidate appearing after selection.
- Malformed discovery and mapped overflow fail before destructive work or registry
  source-setting changes. Existing user-secret lifecycle/squad tests remain intact.
- Failed/cancelled create starts no setup; failed required copy blocks ports/HTTPS;
  failed publishing, missing host dotnet, activation and verification retain the
  created record, report partial state/retry, and prevent full success/launch.
- Cancellation immediately after successful creation, publishing and activation
  starts no following setup stage. Recovery through `ports` works without recreation.
- Real sbx adapter with mocked processes starts/restores the selected stopped target,
  reuses an existing owned relay on on-demand retry and publishes once. Ordinary
  launch and declined replacement add no implicit discovery, setup or unpublish.

## Red → green observations

Observed failures before their fixes (not merely proposed tests):

1. Both creation modes returned success without invoking application-port setup.
2. Six malformed/overflow replacement cases removed/recreated the existing sandbox
   before discovery failed.
3. Six publishing failure/cancellation cases lacked truthful retained-record recovery.
4. Cancellation after publishing still started certificate lookup; cancellation
   after activation still started readiness verification.

Each slice was followed by a passing focused run. Final pinned builds, complete
test results, maintainability checks and any disposable-live evidence are recorded
below after verification. The initial `dotnet test --filter` invocation was not a
valid runner invocation and ran zero tests; verification uses the direct xUnit v3
executable instead. One intermediate rebuild collided with the still-running
task-owned test executable; the subsequent serial build passed.

## Pinned implementation verification

Exact implementation/source commit: **`1985584d18e95cf8b6a60a75826831ad076d35de`**.
It normally incorporates integration `feature/app-ports` at
`7274f59a5d6c3d94db922f86c53f616b493f6fa6` (`git merge --no-edit feature/app-ports`
reported already up to date; no integration worktree/branch was written).

| Check on that commit | Result |
|---|---|
| Debug solution build, serial | **PASS**, zero warnings/errors |
| Complete direct Debug xUnit v3 suite | **2026 passed, 21 explicit skips, zero failures/errors** (2047 total) |
| Release solution build, serial | **PASS**, zero warnings/errors |
| Complete direct Release xUnit v3 suite | **2026 passed, 21 explicit skips, zero failures/errors** (2047 total) |
| Focused new lifecycle matrix | **53 passed**, no skips/failures |
| Focused lifecycle plus touched creator/run/create/tag/published suites, earlier checkpoint | **220 passed**, no skips/failures; later added cases are also in the complete runs above |
| CSharpier | **252 CLI files checked**, pass |
| `git diff --check` | **PASS** |

The 21 skips are the existing Windows file/symlink/case-distinct fixtures and guarded
non-root Linux disposable bootstrap case, not passing tests. No new skip was added.
The complete suites include existing standalone ports, bootstrap-only refresh,
user-secret lifecycle, squad and safety coverage. Result XML was retained only in
the approved task temporary directory, not committed. The Release executable
reported `1.0.0+1985584d18e95cf8b6a60a75826831ad076d35de`; its `abox.dll` SHA-256 was
`6D570E1ACD7E4480F85425DC1AE395F9F59DB1D9C4EE0CE19B5FA77B1A906945`.

### Maintainability workflow

Before edits, clean-code MCP `hotspots` and discovered `coupled_files` were called
with the absolute task repository, including the shared lifecycle/app-port/HTTPS
files. `check_changes` compared against the immutable implementation base
`7274f59a5d6c3d94db922f86c53f616b493f6fa6`.

The first verdict was **worse** (the enlarged creator method and mechanical test
constructor injections). Cohesive extraction of required user-secret reporting and
small touched-fixture construction helpers removed all worsened existing findings.
The repeated verdict was **better**, eight existing files improved; production
`AppPorts` **8.1 → 9.2**, `SandboxCreator` **6.6 → 7.1**, `DevelopmentHttps` **10 → 10**.
New matrix tests retain warnings for long arrange/assert blocks and boolean theory
inputs; the new critical-length retry test was shortened to a warning. This is not
a claim of a smell-free repository, nor a blanket refactor of unrelated debt.

## Windows-host disposable synthetic live flow

**This is synthetic live lifecycle/transport evidence, not a production host-export
or browser-trust acceptance run.** An ad-hoc helper outside the repository referenced
the exact committed product project. Its composed `AboxRootCommand` used the real
`SandboxCreator`, `AppPorts`, `DevelopmentHttps` and `SbxClient`. To stay within the
authorization boundary, it substituted only:

- Published squad metadata with synthetic empty state/MCP metadata, and the
  registry-source read with a synthetic covered-prefix result (no host settings write).
- The creation source transport with an actual task-owned `sbx create shell` using
  the **already cached** `docker.io/library/cao-docker-ai-sandbox:latest` template,
  inventoried as **685acd19ffe7**, `--pull never`, no static MCP. This is not live
  published-v3 fetching or local-workload kit creation evidence.
- Host dev-certs output/export with a newly generated **in-memory synthetic** identity;
  actual protected export validation, transport and sandbox activation were reused.
  No host certificate store/export/trust command or real renewal was invoked.

Everything after the cached creation, including actual sbx sandbox discovery,
replacement/removal, record save, relay creation/publishing and private/public
certificate activation, used the production adapters against the disposable.

```text
Date:      2026-11-16 (Windows host, sbx 0.51.0)
Workspace: C:\Users\UI520628\AppData\Local\Temp\12\opencode\abox-47-lifecycle-live\workspace
Sandbox:   workspace-abox-E8FC9
Pass 1:    create/setup began 14:33:39Z, completed before app startup
Pass 2:    explicit --yes replacement/setup began 14:34:14Z, completed before app startup
Mappings:  .NET HTTPS 9247 → 19247; Angular-discovered HTTP 9447 → 19447
Offset:    10000, no abox.json
```

Inventory and temporary loopback binds checked both alternate host ports free
first. **18203 and 18443 were never selected.** Each pass invoked the composed
`create --squad fixture --yes` command with no separate `ports` call, then started
a certificate-unaware ASP.NET Core fixture and a small Node HTTP marker through
the real shared exec bootstrap **after setup returned successfully**. The Node
fixture's port came from `angular.json`; this is not an installed Angular compiler
or `ng serve` demonstration. The helper process itself had not exited at app start.

Both passes reached the exact markers at **https://localhost:19247/** and
**http://localhost:19447/**. HTTPS used normal hostname validation and a .NET
`CustomRootTrust` policy containing only the synthetic certificate, with **no
validation callback or insecure bypass**. This proves synthetic identity/hostname
TLS, **not default host/browser trust**. Exactly two synthetic export requests
occurred, one for each successful create/replace. Protected staging was empty
afterward. Product builds were warning-free; the ad-hoc Web-SDK helper itself had
one ASP0000 analyzer warning for intentionally composing a plain CLI provider,
plus harmless image `.bashrc` noninteractive line-editing warnings at app start.

The exact disposable was removed and fresh shared resolver lookup confirmed its
absence; the workspace was deleted and ports 19247/19447 no longer listened.
Before/after inventories retained only the original two user sandboxes. Existing
Deals remained **80793caf-9dad-4238-973d-c57bed89c244**, running, unchanged last-used
time, and **127.0.0.1:18203 → 60719/tcp4** (same host listener PID 66712). No exec,
restart, creation, remap or certificate work targeted either existing user sandbox.
The user's original `run-abox.ps1` was not edited.

Ad-hoc helper source SHA-256:
`ED3C3778C14BCEEBF91BC1BFF4B07E9D78BDC0676186A78B0DD26713A7A4A20C`.
After hashing, its entire task-owned temporary directory (helper/build artifacts,
empty staging and saved synthetic record) was removed as well.
This temporary helper was not added to the product or presented as a separately
reviewed production executable. No SDK/package/runtime update, host PATH/global
setting/service change, registry publication, push or issue/PR closure was done.
Final documentation verification checked **72 local Markdown link targets**.

## Remaining gates

Independent heavy review and the final **whole-branch `code-review` skill after
integration** remain parent-session gates. A fresh verifier needs explicit scoped
authorization before any real-host export for a new automatic-create/replace
production-TLS demonstration; the synthetic run above is not a substitute for that
unperformed evidence. Existing #46 production evidence remains pinned to its own
recorded SHA. Real-host renewal is not authorized or claimed; independent Linux-host
proof remains deferred and Copilot remains excluded, not reinstated requirements.
