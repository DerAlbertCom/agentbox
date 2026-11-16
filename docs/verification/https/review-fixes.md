# #46 rigorous-review fixes — 2026-11-16

## Scope and Git state

Task-owned worktree: `app-ports-46-review-fixes`, branch
`feature/app-ports-46-review-fixes`, created from integration
`731832613e53ec500fc1e74135bace66940427ed`. Production fixes are committed in
`739d200328984f7a4f366efa847901fad0a67ae9`. Merging the current `feature/app-ports`
tip reported already up to date. Later changes record evidence and make the
verification helper select its matching Debug/Release CLI/payload configuration.

No push, issue/PR mutation or closure was performed. No real host development
certificate was exported or host trust changed. The live Deals sandbox/API,
runner 54711, app 55039, relay 60719, host 18203, manual runtime/trust/evidence,
user `run-abox.ps1`, credentials and global settings were not changed.

## Production contract fixed

- Activation extracts only the exact public leaf from the already-validated
  encrypted PFX. `SbxClient` sends **public PEM only** through root exec to the
  existing Debian-style system store/updater. PFX/password stay on the separate
  sensitive non-root transfer. Unsupported stores fail without package installs.
- Root-owned narrow directory/file, ordinary files, strict ownership/modes,
  symlink/hardlink/mount checks and a setup lock protect public trust. Renewal
  replaces one stable owned certificate, never unrelated anchors. An identical
  retry still updates system trust after a possibly interrupted prior update.
  Trust and private activation are not a cross-store transaction; failures warn
  of partial state and never print complete success.
- Node's protected extra-CA bundle merges original input bytes with the current
  public leaf. Propagated bundles reuse their protected original source rather
  than accumulating old leaves. Unreadable/unsupported input fails rather than
  discarding user roots; 1 MiB input and 16 distinct source variants bound storage.
  Readiness validates the default extra-CA input before setup can claim completion.
- The exec argument list now contains a short shell reader/hash, not the entire
  receiver source. Installed owner-only code is SHA-256 verified before execution.
  The shell, not CPython, execs the user's command. This preserves inherited
  ignored/default signals and locale, argv, stdin/TTY, workdir and child status.
  No HTTPS state still needs no Python. Unchanged bootstrap code is not replaced
  during certificate refresh.
- Stale existing agents block full setup/Herdr readiness and recognized new agent
  entrypoints, not ordinary shell/exec/pipe save/exit or explicit recovery access.
  Warnings never claim an old agent obtained the new environment. Process visibility
  errors have their own diagnosis, not misleading invalid-configuration 78.
- Reader/writer locking protects directory enumeration from transient transfer
  files. Disappearing `/proc` processes can raise `ProcessLookupError` as well as
  `FileNotFoundError`; both are normal process-exit races, not broken HTTPS state.
- Static discovery accepts `Microsoft.AspNetCore.App` framework references, ignores
  unsupported unrelated project XML, and fails closed on malformed bounded text
  explicitly mentioning ASP.NET declarations. DTDs/entities are not evaluated.
  Host staging failures retain their fixed safe temporary-directory guidance;
  arbitrary child exceptions remain sanitized. Host export stays CLI-only with
  no native key fallback, alternate identity or host-trust installation.

## Red → green evidence

Public seams: production `SbxClient` requests/orchestration, executable receiver /
exec protocol in isolated Linux processes, static discovery and host export.

1. A real `yes | head` / `pipefail` test failed with exit **1** and Broken pipe;
   the old CPython exec parent ignored SIGPIPE. It now returns native **141**.
2. A real named stale agent made a save/exit command return **42** instead of
   **37**; ordinary access now succeeds, readiness/new-agent launch still refuses,
   and all four explicit recovery commands reach inert disposable test stubs.
3. Root public-trust tests initially failed because there was no production
   public-trust receiver; exact-leaf/idempotence/refresh/default trust now pass.
   C# root-orchestration tests initially found no root call and no failure on a
   denied trust step; now they verify exact public-only stdin and ordering/refusal.
4. Framework-reference detection and unrelated malformed/DTD project cases went
   red, as did loss of private-staging guidance. All are green with focused tests.
5. Missing Node extra-CA input initially returned successful readiness; it now
   returns **44**, with sanitized actionable C# guidance and no complete claim.
6. Expanded concurrency caught intermittent launch **78** even without a writer.
   A narrowed repeated subprocess test identified `ProcessLookupError` in the
   `/proc/.../cmdline` read. After the fix, **five higher-churn repeated runs** and
   subsequent complete receiver runs passed. Temporary diagnostics were removed.

## Final automated verification

On production source `739d200...`:

- **Debug and Release solution builds:** zero warnings/errors.
- **Debug and Release full direct xUnit DLL runs:** each **1964 passed, 17 explicit
  platform skips, zero failures/errors (1981 total)**. These are native Windows
  link/case fixtures, not claimed as passing from container coverage.
- **Debug and Release activation-probe builds:** zero warnings/errors. The final
  matching-configuration helper update was also rebuilt in both configurations.
- CSharpier checked **245 files** (CLI plus helper C#/project), and
  `git diff --check` passed.
- `RunReceiverChecks.ps1`: **28 executable Python protocol/process/security tests
  passed**, plus a separate fresh container rejected **actual mounted** runtime
  and trust destinations. Five focused churn repeats also passed. Direct native
  Windows invocation explicitly **skips before imports/mutation**; the suite
  requires its marked fresh Docker runner. It has no host mounts, network,
  certificate export or package installation, and uses `--rm --pull never`.

Receiver coverage includes framing corruption, duplicate/invalid payloads, private
state preservation, ownership/modes/directory/file symlinks/hardlinks, integrity,
atomic refresh, concurrent first activation/launch/renewal, lock contention,
transfer/launch cancellation, stale and nondumpable real parent processes,
cleanup, no-Python fast path, binary streams, argv/TTY/locale/exit status,
default/ignored SIGPIPE/SIGXFSZ/SIGINT and native SIGTERM, public trust retry/renewal,
preserved unrelated anchors, protected Node merges and variant bounds. Actual Node
default TLS validated **both** the synthetic host leaf and the preexisting extra
CA leaf, without callbacks or insecure TLS flags.

Reproduce the isolated suite:

```powershell
pwsh -File docs/verification/https/RunReceiverChecks.ps1
# Focused stress loop, same exact production receiver/exec source:
pwsh -File docs/verification/https/RunReceiverChecks.ps1 -Test `
  ReceiverChecks.test_atomic_refresh_and_concurrent_launches_do_not_race_transfer_files
```

## Fresh sbx synthetic production-path reruns

The cached `cao-docker-ai-sandbox:latest` template was used only to create new
`abox-issue46-activation-*` disposables. Both the Debug run and the final **Release
CLI/payload/helper run** passed initial/renewed exact-leaf Kestrel through direct,
sh, Bash, production exec/pipe/run-shell after setup exited, now with **default
sandbox system-store chain validation** (no custom root store/callback). The
expected synthetic public DER was independently compared with the remote leaf.
Host dev-certs remains mocked; actual root trust and non-root activation are real.

Actual local hc **0.1.17**, pi **1.5.0**, Claude Code **2.1.316** and OpenCode
**2.2.5** shell seams passed exact-leaf/default sandbox system trust. Existing
disposable hc/OpenCode services were detected stale and deliberately recovered
only inside those targets. No inference/prompt/credential changes were used.
Production pipe retained all 256 bytes, separate stderr and exit 37. Every run
confirmed removal of its exact sandbox and workspace.

One earlier shell-only cached-template attempt failed independent provisioning
(no full activation claimed); suppressed diagnostics did not establish the exact
missing prerequisite. It was cleaned, not installed into or counted as passing. Another
iteration correctly refused a stale Debug CLI bootstrap hash after the helper had
been rebuilt alone. Rebuilding matching artifacts resolved that test setup, and
cleanup was confirmed after both failed attempts. Preexisting Bash startup `bind`
warnings occurred on noninteractive Bash launches; direct pipe invariants passed.

`prepare-production` was exercised **only through clean creation/preflight and
exact cleanup** in a separate disposable. No certificate setup/export was run.
The helper and [production-verification.md](production-verification.md) prepare
the separately authorized real-host production gate without reusing manual state.

## Still required — not waived

- Actual production `abox ports` with the **real** existing trusted host identity
  in an uncontaminated disposable, default host TLS/exact leaf and browser reload.
- Independent **Linux-host** execution; these were Windows-host Linux containers.
- Actual authenticated **Copilot shell-tool** invocation; version/help is not proof.
- Host **Herdr desktop** connection; its actual core/transport is not desktop proof.
- Any separately required real-host renewal and exact **18203** acceptance run
  without disturbing the live Deals mapping.
- Later whole-branch review and #47 lifecycle work remain separate.

The existing CLI certificate-store race and explicit app/Node restart requirements
remain documented limitations. Synthetic proof does not complete #46 or authorize
mutating the working Deals runtime or host trust.
