# Story #3 / #46: staging and activation status

## Activation follow-up, 2026-11-16

The integration branch now includes the host CLI exporter, shared on-demand
HTTPS setup, consumable protected PFX/configuration, stable password refresh and
shared exec bootstrap. This file's original staging-only status below is historical.
Read the current [HTTPS contract](../https-development-certificates.md) and
[synthetic activation/tool verification](../verification/https/README.md) first.
No #47 lifecycle integration is included and #46 is not fully accepted. The
[separate manual verifier](story-3-host-certificate-verification.md) subsequently
completed exact host CLI export/copy and explicit activation: default Windows TLS
passed (Swagger HEAD 200), and sandbox default TLS passed (HEAD 404). It supersedes
the earlier permission denial, not the production acceptance gate. The production
export/launch flow using that real host identity, browser reload, Copilot shell
tool, host Herdr desktop and independent Linux-host verification remain unproven.

Read alongside [HTTPS research](story-3-https-research.md),
[the selection refutation](story-3-certificate-mechanism.md),
[live Deals evidence](story-3-deals-verification.md) and
[the manual host verification and retained-state record](story-3-host-certificate-verification.md).

## Historical staging-only status, 2026-11-16

**Partial implementation only; #46 must remain open.** No production host exporter,
trust-metadata correlation, Kestrel configuration, shared activation orchestration
or launcher/Herdr/agent bootstrap is enabled. `abox ports` retains its existing
truthful “HTTPS certificates are not configured” behavior. No #47 lifecycle work,
offset or Angular implementation is included.

The exact-existing-only export versus CLI creation risk and the unverified
Herdr/agent parent initialization contract were surfaced before implementation.
See [the product contract and unresolved decisions](../https-development-certificates.md).
Do not treat the reusable staging adapter as approval for narrower HTTPS acceptance.

## Historical staging scope

- Owner-only host staging, protected at creation, with synthetic export mocks and
  success/failure/cancellation scope cleanup.
- All runtime operations through `SbxClient`, argv and sensitive framed stdin;
  default non-root application user and runtime-only protected destination.
- Digest/size/base64 validation, link/owner/mode/mount checks, one atomic
  PFX/password bundle, bounded failure cleanup and repeat refresh without obsolete
  generations. It is transport storage, not a consumable Kestrel PFX configuration.
- New fixtures live under this task checkout's ignored `.abox/certificate-tests`.
  Probe keys are generated synthetically in memory, never selected from a store.

No actual host key/store/trust operation was performed. In particular, the blocked
external staging directory was not accessed or substituted with another location
for a real-host operation. The existing user sandbox, running Deals app, relay,
profiles, launch settings and root `run-abox.ps1` were not touched. No installation,
upgrade, global setting, registry publication, push, issue or PR mutation occurred.

## Synthetic runtime probe

From this checkout, with a cached shell template and .NET 10 available:

```powershell
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet run --project docs/agents/story-3-transfer-probe/TransferProbe.csproj -- `
  'docker.io/library/sandbox-kit-assembled:2cbc7aa649a63792'
```

The source-linked verification adapter follows the existing #43/#46 probe pattern;
all sbx calls remain inside `SbxClient`, including verification-only creation and
fixture checks. It refuses an existing identity, creates a fresh `abox-issue46-transfer-*`
sandbox with `--pull never --static-mcp none`, and removes only that sandbox in
`finally`. Host workspace is an empty task-owned ignored directory and is removed.
No app, host forwarding or real certificate store is involved.

The initial complete run on the Windows host passed:

- Unchanged synthetic encrypted PFX bytes/password in the expected private bundle;
  Linux application-user directory 0700 and ordinary single-link files 0600.
- Truncated data, digest corruption, incorrect length and extra bytes rejected;
  previous bundle retained.
- Permissive file, symlink, hardlink and directory-at-file destination rejected
  without replacing the old certificate bundle.
- Cancellation **before the stage process starts**, followed by a separate bounded
  cleanup operation. This does not establish remote process-death cleanup during
  a filesystem write or after a lost acknowledgement.
- Repeated refresh to a new synthetic PFX and new password, with no accumulation;
  a concurrent reader saw only complete old or new pairs.
- Task-owned sandbox removal and subsequent absent inventory.

The second complete run also passed directory-link/mode and wrong-owned-file
fixtures, with task-owned sandbox removal verified again. The receiver's mount
rejection is implemented but a mounted-destination live fixture has not been demonstrated.
Contended writers fail closed for retry; no successful simultaneous-writer claim
is made. Synthetic Linux execution inside a sandbox on Windows is **not** independent
Linux-host evidence.

## Automated verification

- First staging/transport tests failed to compile before their interfaces existed,
  then passed after implementation. A later behavioral red test caught an already-
  canceled call attempting runtime/cleanup work; an entry cancellation check fixes it.
- Focused private-staging/transport tests: **11 passed, zero skips/failures**.
- After merging integration `8cca672` at `7561b43`, Debug full solution executable
  suite: **1821 passed, 15 explicit platform skips, zero failures** (1836 total).
- A third complete disposable transport probe passed on that merged tree, including
  all directory/file safety fixtures, pair-atomic renewal/concurrent readers and
  verified sandbox removal.
- The repository's `dotnet test` MTP handshake returned exit 5 with zero tests, so
  verification uses the existing direct xUnit executable/DLL runner instead.
- Debug solution build and synthetic probe build: zero warnings/errors.
- `dotnet csharpier check cli` (230 files), the three probe files and whitespace
  checking passed. No Release build/run or independent Linux-host run was performed.

The Windows directory-ACL test checks owner-only protection before its mock export.
Mock partial PFX exports cover failure/cancellation cleanup; an exclusive-file-lock
fixture verifies cleanup warnings cannot hide the original export failure. These
are synthetic exports, not actual `dotnet dev-certs` or host-store evidence.

## Historical acceptance gate — superseded by activation and manual records above

Real Windows export/copy/use verification remains blocked by the explicit external
directory permission gate. No access grant was received in this task. Forwarding
has separately been demonstrated against the unchanged live Deals application;
its existing sandbox certificate remains untrusted on the host. Do not repeat or
reinterpret that evidence as successful host-certificate provisioning.

Still required: existing-only exact trusted export and correlated metadata tests,
private-key/date/SAN/multiple-candidate failures, complete persistent canonical
configuration and stale-parent behavior across shell/direct argv/exec/pipe/Herdr
and **real tools of every intended agent after abox exits**, running-app renewal
semantics, and authorized Windows plus independent Linux host/browser/default-TLS
evidence. The protected bundle cannot itself meet those requirements.
