# #46 activation verification - 2026-11-16

PR #48 removed the activation probe project, its `Program.cs` and tool driver,
along with the earlier certificate/transfer probe folders. Historical results below
are preserved, not rerun. [Independent receiver checks](#retained-receiver-checks)
remain in this folder and need none of those projects.

Current PEM/bootstrap recovery fixes and red/green results are recorded in
[the PEM regression record](../../agents/story-3-pem-fixes.md), following
[recovery-fixes.md](recovery-fixes.md) and the earlier implementation and
Debug/Release reruns in [review-fixes.md](review-fixes.md). The record below is historical:
the new client uses sandbox **system trust**, and ordinary stale-agent transport
is no longer blocked. The [real-host production gate](production-verification.md) was
subsequently executed on exact PEM/bootstrap candidate
`5b40b570aff9fb76b9c6379e55569eef83dc2df9`; see
[its separate evidence](real-host-production-verification.md), including repeat
setup, bootstrap-only refresh and the supplemental UTF-8 caller-CA check. Older
candidate results stay separately pinned, and the remaining acceptance gates are
explicitly retained. None of the synthetic results below is real-host proof.

## Scope and result

Production on-demand export/activation is implemented, not just transport storage.
**Do not close #46 on this evidence alone.** This synthetic probe performed no real
host dev-certs export, host private-key/store/trust mutation, default host/browser
TLS success, independent Linux-host run, Copilot shell-tool execution or actual
Herdr desktop connection.
The activation implementer did not retry/bypass the then-blocked real-host staging
operation or touch `rwe-ai-sbx-41bc30`/Deals. The
[separate manual verifier](../../agents/story-3-host-certificate-verification.md)
later completed the actual CLI export and explicit Deals activation with default
Windows-host TLS (Swagger HEAD 200) and sandbox TLS (HEAD 404), exact host leaf and
unchanged host trust. That supersedes the earlier denial but remains manual proof,
not production real-host propagation, a browser reload or completion of #46.

All runtime tests here use a fresh `abox-issue46-activation-*` disposable sandbox
created through a verification-only `SbxClient` partial with a cached template,
`--pull never --static-mcp none`. Every sbx call stays in `SbxClient`. No install,
upgrade, publication, global setting, credential/authentication change or billed
model prompt is needed. Explicit core stop/start operations below were confined to
that new disposable sandbox, never an existing user core/app.

## Retired activation harness

The [archived helper sources](https://github.com/DerAlbertCom/agentbox/tree/4ad3b459368789e72400769017153cc1269b49f3/docs/verification/https)
preserve the live harness used below. Its project commands no longer apply to this
checkout. Removing it also removes the ready-made synthetic ASP.NET/tool subprocess
and real-host target preparation/cleanup harness; receiver tests do not replace
those integration checks. The following describes its historical operation.

The host helper publishes a self-contained synthetic ASP.NET Core/.NET 10 app into
an otherwise fresh ignored workspace. A **separate setup process** exercises
production `AppPorts` static Web SDK detection, `DevelopmentHttps`, host exporter
validation/private cleanup, and real `SbxClient` activation. Only host dev-certs
execution/metadata is mocked, using an in-memory generated RSA development PFX;
no host certificate store is consulted. Private staging is outside the mounted
workspace and removed on scope exit. Only public expected DER is written there.
Runtime PFX bytes are checked against the exact mocked CLI export's SHA-256.

The setup process must exit before each app launch. Apps contain no certificate
configuration: ordinary WebApplication/Kestrel gets canonical environment defaults
from production bootstrap. TLS uses `TargetHost=localhost`, normal hostname checks
and a per-client in-memory **synthetic public anchor**, with no validation callback
or insecure option. Exact peer DER and the synthetic HTTP marker are required.
This is not evidence that a real Windows/Linux/browser trust store trusts the app.

## Retained receiver checks

[ReceiverChecks.py](ReceiverChecks.py) and [RunReceiverChecks.ps1](RunReceiverChecks.ps1)
exercise the production embedded Python/shell scripts directly, independently of
the removed .NET projects. From the repository root, with the approved image
already cached locally:

```powershell
pwsh -File docs/verification/https/RunReceiverChecks.ps1
```

The guarded runner uses new `--rm --pull never --network none` containers, no host
mounts/exports or real certificates, and synthetic identities only. Its root trust
changes are confined to the disposable container. It also checks mounted runtime
and public-trust destination rejection in a separate disposable container.

## Demonstrated behavior (historical)

- Initial enable and renewed identity: new direct apphost, noninteractive `sh` and
  Bash apps selected the exact leaf after the setup helper exited.
- The generated sandbox password/path stay stable. An inherited old-parent reader
  can repeatedly load PFXs across atomic renewal without mismatched passwords.
- Production CLI `abox run shell --location <disposable> -- <apphost> serve`, hidden
  `abox exec` and `abox pipe` selected the same exact leaf after helper exit.
- An actual production `abox pipe` round trip retained **all 256 byte values**,
  separate stderr and child exit **37**; bootstrap did not consume stdin. Mocked
  exec tests additionally verify original argv (spaces/metacharacters/Unicode),
  workdir, input/TTY flags, attached streams and child exit preservation.
- PFX/config are owned ordinary single-link 0600 files in a 0700 runtime directory;
  successful refresh has exactly `active.pfx`, `config.json`, `.lock`, no generations.
  Both PFX and configuration reject permissive modes, symlinks and hardlinks before
  replacement. Existing transport probes separately cover ownership, directory
  links/modes, damaged framing, cleanup and old-bundle preservation. Mount rejection
  is implemented; a mounted-destination live fixture is not demonstrated.
- A synthetic named stale parent fails readiness and shared launch without restart;
  a deliberately fresh initialized parent survives certificate renewal.
- **Actual hc 0.1.17** started without bootstrap was detected as stale. Installed
  help has no `--fresh`, so the test deliberately stopped/started only its disposable
  core through bootstrap. `hc exec` then launched exact-leaf Kestrel.
- **Actual OpenCode 2.2.5** service started without bootstrap was detected as stale,
  explicitly stopped in the disposable, and recreated through bootstrap. A red live
  check exposed Linux packaging's `opencode.exe` argv; suffix-aware detection fixes
  it. No user service was restarted and no credentials were introduced.

Installed local tool subprocesses, all **without prompts/model requests**:

| Tool/version | Actual local subprocess entry | Exact-leaf Kestrel TLS |
|---|---|---|
| hc 0.1.17 | `hc exec <apphost> serve` | Passed |
| pi 1.5.0 | offline RPC `bash` request, no session/extensions/MCP | Passed |
| Claude Code 2.1.316 | `claude mcp serve`, MCP initialize/list/call **Bash** | Passed |
| OpenCode 2.2.5 | API session creation + session **shell**, no prompt endpoint | Passed |
| Copilot 1.0.105 | installed/help/version only | **Unverified** |

Copilot's documented direct dynamic-workflow route requires authentication; this
task has not assumed permission to introduce credentials or send a billed prompt.
Further accepted verification needs an authenticated disposable/local tool seam
that does not initiate inference, or explicit tightly scoped authorization for a
representative Copilot shell-tool check. No coverage is invented from `--help`.
Herdr's real core/transport path was exercised; the host desktop UI itself was not.
Custom wrappers/renamed processes and arbitrary pre-existing shells remain subject
to the documented explicit fresh-parent requirement.

Completed disposable runs verify sandbox removal with a fresh inventory and remove
only their task-owned workspace. Early failed probe iterations (project working
directory, ICU prerequisite and stale-OpenCode executable detection) also confirmed
cleanup. One expanded run exceeded the shell's 120-second outer timeout after all
tool checks; a separate exact-workspace cleanup invocation confirmed that task's
sandbox absent and its workspace removed. Subsequent full runs use a 300-second
outer allowance; the helper itself retains its bounded timeout.
No private host key was read/exported or real-host staging directory accessed.

## Automated coverage and final verification

Behavioral red/green slices cover fail-closed missing/malformed metadata before
export, export success/cleanup, invalid date/SAN/format/key/trust/identity, ambiguous
candidates, changed post-export metadata, child exceptions/raw diagnostics and
cancellation. SDK discovery reads a metadata-only installed-tool fixture; a newer
incompatible tool can be skipped for an already-installed compatible older tool.
Non-Web projects skip certificate work; explicit Web SDK projects require it even
without launch ports. A Herdr launcher red test verifies stale readiness prevents
desktop opening/false success. Sensitive process-policy and protected-directory
tests are shared with the existing transport groundwork.

Integrated Debug solution build: **zero warnings/errors**. The direct xUnit DLL
runner passed **1953 tests**, with **17 explicit platform skips**, zero failures or
errors (**1970 total**). Focused new export/setup/activation/Herdr behavior tests:
**37 passed**, zero failures/skips. CSharpier checked **243 CLI files** and the two
probe C# / project files; `git diff --check` passed. The existing complete disposable
transport probe also passed all damage/permission/link/ownership/cancellation/
refresh/concurrent-reader checks and confirmed sandbox removal.

Published probe dependency metadata confirms both .NET and ASP.NET Core runtime
packs **10.0.15**. Windows-host disposable Linux-container execution is **not** an
independent Linux-host run. Debug only was built/run; Release remains unverified.

## Integration retest, 2026-11-16

Integrated exact activation tip `1abfc945301eb2a3b3056908f903e0109eae5f0d`
after separately preserving the manual verifier note and eight public source files
in `9c6a5be563c96742fa9449bc7694b23d580e5acf`. The normal merge required no textual
conflict resolution; documentation now distinguishes historical staging/denials,
manual trusted HTTPS and incomplete production acceptance. Product source and
README match the activation tip; the earlier integration work is preserved.

- Combined Debug solution build and all three certificate probe builds: **zero
  warnings/errors**.
- Direct xUnit DLL full suite: **1953 passed, 17 platform skips, zero failures or
  errors (1970 total)**.
- Focused export/setup/transport/activation, Herdr, exec/stream/process-policy,
  .NET/Angular ports, shared offsets and configuration regression set: **334 passed,
  3 platform skips, zero failures or errors (337 total)**. Classes use the flat
  `Abox.Tests` namespace, not filesystem-derived namespaces.
- Complete fresh disposable transport probe: passed damage/framing, file/directory
  permissions, links, ownership, cancellation-before-stage cleanup, renewal and
  concurrent-reader checks; task-owned sandbox removal confirmed.
- Fresh disposable activation probe using cached shell template
  `sandbox-kit-assembled:2cbc7aa649a63792`: passed direct/sh/Bash exact-leaf Kestrel
  checks for initial and renewed identities after provisioning helper exit, unsafe
  PFX/config rejection, stale/fresh synthetic-parent readiness, concurrent renewal,
  actual production `exec`/`pipe`/`run shell`, and the 256-byte pipe/stdout/stderr/
  exit-37 contract. TLS used the synthetic public anchor, not host trust. This
  template has no agent tools; no real service or agent-tool check was repeated.
  Task-owned sandbox absence and workspace removal were confirmed.
- Retained manual receiver framing functions tested in memory: one valid frame
  and seven malformed frames. Production receiver parsing additionally passed one
  valid and 19 invalid frames, including duplicate/incorrect JSON fields. No live
  manual helper entrypoint or host export was run.
- CSharpier: **243 CLI files** and **9 probe C#/project files**; staged/unstaged
  whitespace checks passed. No Release retest was performed.

Only public Markdown/source/project files were included; ignored build outputs and
private runtime/export material were excluded. The integration retest did not
touch the retained Deals sandbox/API/relay, host certificate/trust, user services,
root `run-abox.ps1` or issue states. #3/#46/#47 remain open; final whole-branch
review and the acceptance cases above remain separate.
