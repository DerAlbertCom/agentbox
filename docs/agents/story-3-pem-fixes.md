# #46 PEM compatibility and bootstrap-only recovery — 2026-11-16

Isolated worktree/branch: `app-ports-46-pem-fixes` /
`feature/app-ports-46-pem-fixes`, starting at integration
`13a5f4024eca18e1fe8cafbfaed989cad2f61b2f`. This record concerns synthetic
regression checks, not a new real-host certificate export or acceptance waiver.

## Agreed seams and red/green results

Tests cross the requested production receiver/installed bootstrap and composed
CLI → `SbxClient` seams, not private Python functions.

1. **Non-ASCII caller metadata.** The new real-receiver test first failed on all
   three inputs with status **44**, expected **0**: a UTF-8 comment prepended to
   the real disposable image's Debian CA bundle, UTF-8 Bag Attributes before a
   synthetic certificate, and opaque non-ASCII bytes outside a PEM block. Actual
   Node accepted each original input before the receiver assertion. Validation
   now extracts complete certificate blocks from raw bytes, requires at least one
   for nonempty content and validates each ASCII block using SSL. Original bytes,
   digest, `.source` and merged bundle are asserted unchanged; Node loading and a
   recognized fresh agent entrypoint succeed. The actual Node default-TLS test
   also uses UTF-8 Bag Attributes/comments and checks both the development leaf
   and independent synthetic caller root. No roots are replaced or reserialized.
   Empty/unset input retains the existing no-original-extra-roots contract.
2. **Protected-state classification.** The new receiver test first exposed status
   **44** for missing/empty public PEM and missing/corrupt inherited source, plus
   false success for invalid public PEM. A second red check exposed false status
   success when an independently selected input reused a corrupted owned source.
   Only `CallerCAInputError` and a separate capacity error permit ordinary/recovery
   access with the original Node variable and valid Kestrel defaults. Protected
   state now fails closed (launch **78**, non-44 status failure), including when a
   bad caller input could otherwise hide a bad public PEM. Diagnostics are fixed,
   value-free and never include a traceback/password/file contents. Matching
   cached source bytes are checked before reuse. Existing ownership, single-link,
   no-follow and mount guards remain intact. Missing/unreadable files, FIFOs,
   directories, oversize input, no PEM, malformed/incomplete PEM and non-ASCII
   inside PEM remain actionable failures, not silently empty roots.
3. **Explicit upgrade recovery.** Both the composed CLI contract test and the
   disposable Linux execution test first failed because
   `--refresh-https-bootstrap` was unrecognized. The new option and one matching
   `ISbxClient` operation are a deliberate small interface addition: full `ports`
   setup cannot be a reliable save/recovery prerequisite after a CLI upgrade.
   The production receiver runs from embedded verified source, validates existing
   protected state under its lock and atomically replaces only `bootstrap.py`.
   It does not execute the old reader, read projects/`abox.json`/caller Node input,
   export host identity, publish ports or request root trust. It refuses missing
   or unsafe activation, rejects `--offset`, and does not claim readiness.
   The executable composed-CLI test replaces only sbx transport with local process
   execution in a guarded fresh non-root Linux container; it uses actual
   `SbxClient` argv/resources, not direct activation as the upgrade operation.
   Root public trust is stubbed only during synthetic fixture preparation.
   A changed installed reader cannot execute its marker, private runtime bytes
   remain identical across refresh, and the next launch receives the same
   canonical path/password. Injected raw adapter exceptions initially leaked in
   the new operation; the red test now passes with sanitized failure/cancellation.
4. **Launch contention allowance.** Launch lock retry is restored to **five
   seconds**, separately from the other operations' three-second retry and the
   host cleanup's five-second cancellation budget. No exclusive-lock design change
   or test-side operation retry was introduced. A synchronized **24-launch** burst
   behind a real lock holder uses the real Debian bundle with a UTF-8 comment and
   requires every launch to succeed, preserve exact roots and share one source.
   It does not assert tight elapsed-time thresholds. Existing mixed refresh/launch,
   setup/status/cleanup overlap, timeout/busy and signal-cancellation checks remain.

The stricter protected-public validation exposed two old concurrency fixtures
that refreshed with placeholder text instead of a certificate. Their framing
helper now defaults to the same generated synthetic public PEM used by the rest
of the suite; the success assertions were retained, not weakened.

## Verification

Final verification of the fix tree:

- Guarded `RunReceiverChecks.ps1`: **38 passed**, no skips/failures, followed by
  actual mounted-runtime and mounted-public-trust rejection. All operations run
  in new network-disabled `--rm` containers, without host mounts.
- Debug and Release solution builds: **zero warnings/errors** each.
- Debug and Release full direct xUnit suites: **1994 total, 1973 passed,
  21 explicit skips, zero failures/errors** each. The new guarded Linux-only
  upgrade test is one of the Windows skips, not a claimed Windows pass.
- Fresh non-root Linux-container Debug **and Release** regression sets (`PortsCommandTests`,
  `DevelopmentHttpsTests`, `DevelopmentCertificateActivationTests`): **183 passed**,
  no skips/failures each, including the executable composed CLI upgrade check. Public
  source and matching test binaries are copied through stdin, not host-mounted.
- Activation-probe builds in Debug and Release: **zero warnings/errors** each.
- CSharpier checked **244 CLI files**; `git diff --check` passed.

Implementation commit: `fdd1916`. Normal evidence merge: `70ce048`, incorporating
exact verifier evidence commit `8fa8e035570e3b88f2b54d3ccb66ceaab81ec519` after the
code commit. It made no CLI/source changes. Integration `feature/app-ports` at
`13a5f40` is an ancestor; a final normal merge reported already up to date.
Both verifier evidence/procedure files are byte-identical to `8fa8e0`.
No integration worktree/branch was modified.

Reproduce the receiver checks:

```powershell
./docs/verification/https/RunReceiverChecks.ps1
```

Build/run all managed tests with `DOTNET_GENERATE_ASPNET_CERTIFICATE=false`:

```powershell
dotnet build cli/agentbox.slnx -c Debug -m:1
dotnet cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll -noLogo -maxThreads 4
dotnet build cli/agentbox.slnx -c Release -m:1
dotnet cli/test/Abox.Tests/bin/Release/net10.0/Abox.Tests.dll -noLogo -maxThreads 4
dotnet csharpier check cli
git diff --check
```

For the guarded executable upgrade test, copy the matching public checkout and
test binaries into a new network-disabled, no-host-mount Docker container, run
as uid 1000 with `ABOX_DISPOSABLE_RECEIVER_CHECKS=1`, and invoke the test executable
with `-method '*BootstrapOnlyUpgradeThroughComposedCliPreservesActivationAndNeverRunsOldCode'`.
The cached approved `cao-docker-ai-sandbox:latest` supplies the existing runtimes.
It must not be run in a user sandbox or against preexisting HTTPS runtime state.

## Evidence and remaining boundaries

The verifier's completed real-host production evidence commit
`8fa8e035570e3b88f2b54d3ccb66ceaab81ec519` concerns tested candidate
`4f5719e09e8946a311c93e0cc77a20bbd20052bc` (its CLI tree matches integration
`13a5f40`). Its actual Release production export, separate app startup, strict
host/system/Node TLS, original-root preservation, repeat safety, stable private
configuration and cleanup are **not** tests of this new PEM/recovery code.
It is preserved separately, not overwritten, relabelled or rerun here.

Browser UI, actual Copilot shell-tool, Herdr desktop, independent Linux **host**,
real-host renewal and exact 18203 production fixture remain open. No real host
private export/trust operation, Deals/`rwe-ai-sbx-41bc30` mutation, host 18203 or
18443 bind/process action, verifier workspace write, issue/PR/push or integration
branch write is part of this task. Only task-owned synthetic containers are used.

Unreadable **any** project remains intentionally fail-closed: applicability is
unknown until its bytes can be read; there is no silent HTTPS skip. The existing
CI gap is unchanged: no tracked workflow exists to extend, and the guarded Linux
checks are not falsely claimed to run in CI. No pipeline was invented.
