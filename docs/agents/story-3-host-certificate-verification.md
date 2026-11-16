# Story #3: host certificate copy verification

Read this when resuming the explicitly authorized manual host-certificate copy
into `rwe-ai-sbx-41bc30`. This is a live verification spike, not the production
implementation or completion of #46. Port fallback work remains separate in #43.

## Latest result — approved-path retry still denied, 2026-11-16

**Still blocked before export. No certificate was exported, copied or configured;
no password was generated, and the API was not stopped or restarted.**

This fresh manual run received the user's explicit decisions **“Require dev-certs
CLI”** and **“Approve access”** for exactly:

```text
C:\Users\UI520628\AppData\Local\Temp\12\opencode\abox-story3-host-cert-20261116
```

The `patch` retry to create only a public `access-check.txt` marker in that exact
directory was nevertheless rejected with **`Permission denied:
external_directory`**. The marker was not created. Execution stopped at that
boundary; no other tool, alternate path, native private-key export API, or
replacement certificate was used to work around it. The permission gate therefore
remains an actual tool denial despite the supplied user approval, not a pending
request for the same user decision.

### Fresh read-only preflight completed before the denial

- Reread repository standards/domain pointers, both live verification notes,
  [HTTPS research](story-3-https-research.md),
  [certificate-mechanism findings](story-3-certificate-mechanism.md), and the full
  GitHub #3/#46 bodies/comments. Both issues remain open with empty comment arrays.
- `CurrentUser/My`, opened with `ReadOnly | OpenExistingOnly`, still has exactly
  one development-OID certificate: **`8E84EA4F2F4EC8548DAF359CCB1179008251C714`**,
  subject `CN=localhost`, private-key association present, UTC validity
  **2026-01-14T14:47:39Z–2027-01-14T14:47:39Z**. Public SAN metadata includes
  `localhost`, development/container hostnames and both loopback addresses.
- Crucially, the development-OID raw extension is **`06`** (base64 `Bg==`), i.e.
  **certificate-format v6**. The older-format/new-identity hazard was checked,
  not assumed. No private-key export operation was requested by this store read.
- Installed host SDKs: **8.0.428, 9.0.321, 10.0.303, 10.0.404, 11.0.100**.
  Existing dev-certs tool DLLs were found for **8.0.34, 9.0.23, 10.0.11 and
  10.0.15**; SDK 11's inspected `DotnetTools` directory has no dev-certs entry.
  Nothing was installed or downloaded for execution.
- Reread the pinned **10.0.15** CertificateManager/CLI source at
  `547fc16b976e18911f9df7630f1f75758ab248f2`: its interactive export requires
  format **>=6** (runtime minimum is 4), selects the first eligible existing
  subject, and creates a certificate if none qualifies. The observed target meets
  that version filter; this is **not** a completed CLI exportability/trust or
  exact-candidate preflight. No export command was invoked blindly or otherwise.
- Before the retry, staging ACL read-back again found owner
  `ENERGY\UI520628`, inheritance disabled, exactly one allow/full-control rule for
  that owner, and an **empty directory**. There is no host private export or
  generated password from this run to clean up.

### What remains unperformed

Fresh machine-readable CLI exportability/trust correlation, system-chain check,
sandbox mount/runtime-destination checks, PID/start-tick revalidation, secure
transport, canonical Kestrel runtime configuration and post-copy TLS/HTTP checks
were **not** completed. No sbx call or mapping mutation was made in this run.
The working relay and old API PID/start-tick values in the
[Deals note](story-3-deals-verification.md) are the prior session's evidence, not
fresh observations by this interrupted run.

Retry only when access to the exact approved directory is actually available;
then redo the complete existing-only CLI/candidate/trust preflight and process
ownership checks before export or stop. Unsupported existing identities must
block, never trigger replacement creation or native private-key export fallback.
Host trust must remain untouched. Even a future successful direct-bootstrap
manual launch would not establish universal launcher/agent-parent propagation or
complete #46.

Only this existing note was edited; no product/app/helper source, Git state,
issue, global setting, core, agent, service, sandbox lifecycle or registry was
changed. This note remains uncommitted for the integration coordinator.

## Historical result — first blocked attempt, 2026-11-16

**No certificate was exported, copied or configured. The API was not restarted.**
The host-certificate copy/use test and fresh post-copy HTTPS tests remain undone.

The user explicitly requested copying and using the existing host ASP.NET Core
certificate. Full #3/#46 bodies and empty comment arrays were reread, along with
the [HTTPS research](story-3-https-research.md) and
[Deals live baseline](story-3-deals-verification.md).

### Current read-only observations

- The Windows user's `CurrentUser/My` contains one ASP.NET development certificate
  identified by extension OID `1.3.6.1.4.1.311.84.1.1`:
  **`8E84EA4F2F4EC8548DAF359CCB1179008251C714`**, with a private-key association.
  Its SAN includes `localhost`, and its UTC validity is
  **2026-01-14T14:47:39Z–2027-01-14T14:47:39Z**.
- The same thumbprint is present in `CurrentUser/Root`. This run has not yet
  correlated an actual export with fresh machine-readable trust metadata or
  performed a new system-chain check. Store presence is not a served-identity or
  browser-validation result.
- `sbx ls --json` reports the exact existing sandbox running, and `sbx ports`
  confirms the existing **127.0.0.1:18203 -> 8203/tcp4** mapping. No mapping was
  changed. Its mount inventory lists workspace and selected agent-state mounts,
  not a whole-home mount; receiver-side mount/path safety validation is still
  required before choosing a certificate destination.
- PowerShell uses .NET **10.0.15**. The existing built `Abox.Infrastructure.ProcessRunner`
  loaded successfully; no build or dependency installation was needed. Host
  SDK **10.0.404** and its `dotnet-dev-certs.dll` are available.

### Exact security blocker

An empty host task staging directory was created at:

```text
C:\Users\UI520628\AppData\Local\Temp\12\opencode\abox-story3-host-cert-20261116
```

Creation used `FileSystemAclExtensions.CreateDirectory(DirectorySecurity, path)`
with an owner-only, inheritable full-control DACL established at creation. A
read-back verified inheritance disabled, current user as owner, exactly one access
rule, and no other principal. No private material was written there.

The subsequent `patch` call to create `common.ps1` and `preflight.py` there was
rejected with **`Permission denied: external_directory`**. Those helpers were to
use `ProcessOutputPolicy.Sensitive`/`CapturedSensitive` and validate the sandbox
runtime destination before any export. Execution stopped rather than using
another tool to bypass the denial. No host private export or password exists to
clean up. The empty protected staging directory remains for explicit approval
and inspection before reuse.

## Resume gate and scope

Obtain approval for that exact external staging directory, then revalidate current
certificate/trust, sandbox mounts and task-owned PID/start-tick records. Follow
the linked research's protected export/framed receiver requirements. Preserve
the unchanged `Web` profile and `https://localhost:8203`; stop only positively
verified task-owned API processes. Use protected runtime configuration to pass
canonical Kestrel Path/Password settings without secrets in normal diagnostics
or launch argv.

After the copy and restart, record the **actual served public leaf thumbprint**
and a host default-validation request to `https://localhost:18203/`. Neither has
been freshly measured by this interrupted run. Historical pre-copy results are
in the Deals baseline, not evidence that the copied host certificate is served.
If #43 still prevents reaching TLS, record that failure separately and verify
the served host certificate inside the sandbox with full hostname/chain validation
and, if needed, an explicit ephemeral client trust anchor.

Even a successful explicit-bootstrap launch would prove only this manual launch,
not persistent automatic propagation through every supported launcher or agent.
During this interrupted verification, no host trust changes, sandbox lifecycle
operations, product implementation edits, app-source/profile edits, issue mutations,
staging or commits were performed.
