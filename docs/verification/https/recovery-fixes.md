# #46 recovery and concurrency fixes — 2026-11-16

Isolated branch `feature/app-ports-46-recovery-fixes` starts at exact
`d600a117e47f08cb09ba5098a2d10bb9d525d3b8`. No changes were made in the active
review-fixes verifier worktree. This record supplements, not replaces, the
[earlier review-fix evidence](review-fixes.md) and its open production gates.

## Behavior and red/green evidence

Tests use the requested public seams: composed CLI commands, `SbxClient`'s public
certificate operations, and the exact embedded receiver/installed bootstrap and
shell sources executed in fresh disposable Linux containers.

- **F1, ordinary/recovery access:** the modified missing-input receiver test was
  run before the fix and failed: ordinary `true` returned **78**, expected **0**.
  Node-only errors now warn and preserve the caller's original Node variable plus
  verified Kestrel defaults for ordinary commands and the four explicit recovery
  argv. Child exit **37**, original input and both Kestrel variables are asserted.
  Status returns **44** and new recognized agents return **78**, never false trust
  success. Missing files, unreadable root-owned files, directories, oversize files,
  FIFOs and variant overflow are exercised. A further malformed-PEM red test showed
  status **0**, expected **44**, and an incorrectly replaced caller environment;
  in-memory PEM validation made it green without reserializing the original bytes.
  The caller-selected corporate-CA symlink test follows a root-owned readable target
  as uid 1000, while protected abox paths retain strict no-follow checks. The old
  tests intentionally expecting `true` to fail on missing input/cap overflow were
  corrected, not deleted. The cap remains 16 retained source variants, with no
  automatic eviction of files running Node clients might need; cap overflow also
  tests shell and every explicit recovery argv with original environment/exit code.
- **F2, unknown project applicability:** the new composed command test held
  `Web.csproj` unreadable using a Windows sharing lock. Before the fix `ports`
  returned **0**, expected **1**. The production edit permits unrelated-malformed
  skipping only after project text has actually been read. The green command test
  requires safe filename/access guidance and no publishing/export. Native Unix
  permission denial is the alternate fixture (explicitly skipped for users able
  to bypass permission bits). File-link command cases cover `.csproj`, `.fsproj`,
  `.vbproj` and launch settings; Windows reports missing native link privileges as
  skips. All five command cases passed as non-root in the disposable Linux image,
  with **no skips**. Existing malformed-unrelated/DTD policy and explicit
  FrameworkReference/Web SDK tests passed (9 focused tests).
- **F3, real overlap rather than outer serial retries:** a held runtime lock forced
  prepare/status/activate/cleanup and launch to overlap. Before the fix setup and
  cleanup completed immediately with errors, failing the pending-operation
  assertion. All operations now retry their own exclusive lock, for at most three
  seconds, interruptible by signals. The overlap test passes after release;
  mixed concurrent renewals/launches now assert success without test-side retries,
  and concurrent first activation requires every operation to succeed. Separate
  checks assert busy exit **75**, bounded duration, and SIGTERM **143** for waiting
  setup/status/activation/cleanup, without configuration errors or secret output.
  The adapter's new prepare/stage/status timeout cases first failed because only
  generic configuration guidance was returned; all now give sanitized busy/retry
  guidance. Cleanup remains bounded inside its separate five-second host budget.
  The actual implementation is serialized exclusive access, including process
  scans and Node bundle writes, **not** shared readers/writers; docs say so.
- **F4, explicit remaining upgrade restriction:** installed-code hash mismatch
  still fails closed for ordinary/recovery/agent commands. A new receiver test
  checks exit **78**, no child stdout or unverified execution, specific CLI-upgrade
  and folder-qualified refresh instructions, and successful launch after refresh.
  No unsafe environment or unverified-code fallback was introduced.

## Verification

Run the executable Linux receiver checks from this checkout:

```powershell
./docs/verification/https/RunReceiverChecks.ps1
```

The runner copies only public source over stdin into new `--rm`, network-disabled,
no-host-mount containers using the already-cached `cao-docker-ai-sandbox:latest`.
It generates only synthetic keys/certificates and tests trust changes solely in
that new disposable writable layer. It also runs separate actual mounted-runtime
and mounted-trust rejection checks. No receiver guard was disabled.

Full suite reproduction (the direct executable uses xUnit's `-method`/`-class`
filters, not `--filter-method`; combining those filter types means intersection):

```powershell
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet build abox-cli/agentic-box.slnx -c Debug -m:1
dotnet abox-cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll -noLogo -maxThreads 4
dotnet build abox-cli/agentic-box.slnx -c Release -m:1
dotnet abox-cli/test/Abox.Tests/bin/Release/net10.0/Abox.Tests.dll -noLogo -maxThreads 4
dotnet csharpier check abox-cli
git diff --check
```

- Debug build: **zero warnings/errors**; full suite **1968 passed, 20 explicit
  platform skips, zero failures/errors (1988 total)**.
- Release build: **zero warnings/errors**; full suite **1968 passed, 20 explicit
  platform skips, zero failures/errors (1988 total)**.
- Final executable receiver suite: **34 passed**, no skips/failures, followed by
  successful actual mounted-runtime and mounted-trust rejection checks. This
  includes the final cap-overflow shell/all-recovery assertions.
- Focused public adapter activation/readiness checks: **10 passed**, no skips.
- Focused discovery command tests on Windows: **1 passed, 4 link-privilege skips**;
  same five tests in a fresh non-root Linux container: **5 passed**, no skips.
- Expanded Release Linux-container regression set (`PortsCommandTests`,
  `DevelopmentHttpsTests`, `DevelopmentCertificateActivationTests`): **177 passed**,
  no skips/failures. An initial binary-only attempt failed the certificate fixtures'
  source-checkout prerequisite (19 fixture failures, not product failures). The
  successful rerun extracted a public `git archive` of implementation commit
  `cc41935596e96ba782120e6507b8ac7c7364cd73` inside the new container, copied the
  matching Release binaries beneath that checkout, and ran as uid 1000. No guard
  was disabled, no host mount/network was supplied, and the container was removed.
- CSharpier checked **243 CLI files**; `git diff --check` passed.

## Remaining gates and safety boundaries

**F5 remains a CI integration gap.** There is no tracked `.github/workflows`, Azure
pipeline, GitLab CI or Jenkins workflow in this checkout to extend. Receiver checks
are automated executable tests explicitly run here, but **not** part of `dotnet`
test discovery or a claimed CI job. A future Linux Docker-capable CI job needs an
approved image with Python 3.8+/ssl, Node, OpenSSL and Debian ca-certificates; it
must run the guarded disposable runner and must not run the root trust tests on
the CI host or a user sandbox. No speculative CI/provider/image publication was
added in this narrowly scoped recovery fix.

Windows-host Linux containers are not independent Linux-host sbx verification.
This task did not perform a real host export, host trust change, production
`abox ports` real-identity run, browser reload, Copilot shell-tool request, host
Herdr desktop check, or #47 lifecycle integration. The separate active production
verifier remains authoritative for its work and is not superseded by these
synthetic results. No live Deals / `rwe-ai-sbx-41bc30`, task 54711/55039, relay
60719, host 18203, verifier sandbox or verifier port was touched. No sandbox/service
inventory mutation, package installation, publish/push, issue or PR closure was
performed. CLI-only export and unchanged host trust contracts remain intact.
