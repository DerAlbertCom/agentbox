# Story #3: host certificate copy verification

Read this when resuming the explicitly authorized manual host-certificate copy
into `rwe-ai-sbx-41bc30`. This is a live verification spike, not the production
implementation or completion of #46. Port fallback work remains separate in #43.

## Result — blocked before export, 2026-11-16

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
