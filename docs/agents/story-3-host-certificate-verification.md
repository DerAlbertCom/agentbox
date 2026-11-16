# Story #3: host certificate copy verification

Read this for evidence or separately authorized cleanup of the manual
host-certificate copy into `rwe-ai-sbx-41bc30`. This is a live verification spike, not the production
implementation or completion of #46. Port fallback work remains separate in #43.

## Latest result — exact host certificate served and trusted, 2026-11-16 13:07 CET

**Manual copy and explicit activation succeeded.** At **2026-11-16T12:07:47Z**, a
fresh verifier process, after the staging and activation helpers had exited,
confirmed the exact host certificate through the unchanged relay. Both Windows
host and sandbox clients passed default hostname/chain validation. The API,
relay, protected runtime copy and exact public sandbox trust entry are **left
running/installed**. This is manual live evidence, **not completion of #46**.

### Permission retry and existing-certificate gate

After the user's wildcard correction, the **original `functions.patch` operation**
succeeded for these public files inside the exact approved directory, before any
directory-read retry:

```text
C:/Users/UI520628/AppData/Local/Temp/12/opencode/abox-story3-host-cert-20261116/common.ps1
C:/Users/UI520628/AppData/Local/Temp/12/opencode/abox-story3-host-cert-20261116/preflight.py
```

The earlier directory-root `read` denial below was a different operation and did
not establish that descendant edits were denied after the wildcard correction.
No alternate-directory or denied-operation workaround was used. Subsequent
permitted ACL checks verified the owner-only staging DACL again before export:
current user owner, inheritance disabled, exactly one owner/full-control allow
rule inheritable by files and directories, ordinary non-reparse ancestors.

The installed **SDK 10.0.404 / dev-certs 10.0.15** CLI reported exactly the known
host certificate: `CN=localhost`, format **v6**, development certificate,
**exportable**, trust **Full**, with the known UTC validity
**2026-01-14T14:47:39Z–2027-01-14T14:47:39Z**. Independent read-only store checks
confirmed the sole development identity, private-key association, current dates,
localhost SAN and a successful system chain with no ignored errors. These checks
were repeated immediately before export. Pinned CLI source was reread: ordinary
export can create a certificate if no eligible candidate exists, so an absent or
ambiguous candidate would have blocked instead. No store/trust creation occurred.

The actual password-protected **CLI-exported PFX** was loaded ephemerally, matched
the expected full public DER, passed date/SAN/v6 checks, and proved private-key
usability by signing/verifying a challenge. Fresh CLI metadata correlated that
actual identity with `Full` trust. CurrentUser/LocalMachine My/Root public-DER
inventory snapshots were unchanged across export/copy. There was **no native
store private-key export fallback**, new certificate, SDK/global installation,
host `--trust`, `--clean` or `--import`.

### Public identities and the browser evidence

| Identity | SHA-1 thumbprint | Certificate DER SHA-256 |
|---|---|---|
| Pre-copy sandbox leaf, rejected as `UntrustedRoot` | `CA669D34D0FA19407DFD5788F3A639AA759A6953` | `BA890CDBED7EE419D3EFC58F3944DB68737A26881F9C8283EBBF75763FC30D91` |
| Existing host / actual post-copy served leaf | `8E84EA4F2F4EC8548DAF359CCB1179008251C714` | `6A53FFA474740955141D2A0162D5D686A5B27851EF4FF62ED71F4EE465CE26FC` |

The user's browser certificate fingerprint **exactly matched the measured pre-copy
sandbox DER SHA-256**, not the intended host certificate. SHA-256 over that
served leaf's actual **SubjectPublicKeyInfo** was
`FB19822520B4E36612840504B6CE143E5D5BC8E8A6E0C88D00C04C053650845E`, also exactly
matching the supplied public-key fingerprint. This is measured encoding
correlation, not an assumption that all browser key fingerprints use SPKI.
The host/post-copy SPKI SHA-256 is
`7C2693651A3B59A0962424439B2BEBA9301F2CFCD890B38D795B2E7F983D5930`.
No SHA-1 identifier was equated to a SHA-256 fingerprint.

### Protected copy, real PFX configuration and sandbox trust

- An empty export file inherited the verified owner-only Windows DACL **before**
  private bytes were written. File owner, DACL, ordinary type and single hard-link
  count were checked. The generated password existed only in memory on the host.
  Export used `ProcessOutputPolicy.Sensitive`; metadata used `CapturedSensitive`.
  **CLI limitation:** `dotnet dev-certs --password` necessarily exposes the
  generated password to sufficiently privileged process-argument inspection.
  Its value/argv were never printed or logged by the verifier.
- `SbxClient` carried a length/digest-checked frame over stdin, not secret argv.
  The receiver resolved actual non-root UID **1000** / `/home/agent`, checked
  ancestors, ownership, modes, links/hard links and `/proc/self/mountinfo`, and
  refused overlap with workspace/agent-state or other non-root mounts.
- Exactly one consumable **`host.pfx`**, protected **`runtime.json`** and readiness
  digest live under **0700**
  `/home/agent/.local/state/abox-story3-host-cert-20261116`; regular files are
  owner-only **0600**, single-link, non-symlink entries. This is not an opaque
  atomic bundle passed to Kestrel. The private config supplies canonical
  `ASPNETCORE_Kestrel__Certificates__Default__Path` and
  `ASPNETCORE_Kestrel__Certificates__Default__Password` only to the explicitly
  launched API. Neither secret was written to app sources, publication inputs,
  images, normal diagnostics or launch argv.
- The host export was deleted in `finally` after transfer; a final staging audit
  found **only the two public helpers, zero PFX/private exports, no password file**
  and the same protected ACL. Failure/cancellation paths include bounded,
  lock-checked cleanup of only the named task's partial runtime files; a complete
  committed generation is retained rather than deleting an active/unknown copy.
- The user's separate **“trust the host cert in the sandbox”** authorization was
  applied only to the exact **public** host leaf. Through the supported sandbox
  root exec user, existing `/usr/sbin/update-ca-certificates` added root-owned,
  ordinary single-link **0644** source
  `/usr/local/share/ca-certificates/abox-story3-host-8e84ea4f2f4ec8548daf359ccb1179008251c714.crt`.
  Trust destinations were checked for unsafe owners, links and mount overlap.
  No `--fresh`, trust package installation, sandbox-generated-leaf trust or
  unrelated local certificate source replacement was used. Normal generated
  system bundles/hook outputs are separate from that public source entry.

**Trust-verifier caveat:** the first supported trust update completed, but its
helper exited **1** because a whole-tree byte comparison detected an existing
derived entry change; that first comparison did not retain the changed-path list.
A read-only audit found the exact installed host DER, default `openssl verify
-purpose sslserver -verify_hostname localhost` exit **0**, and the existing regular
derived files `ca-certificates.crt` and `java/cacerts`. An idempotent repeat of the
same supported update exited **0**, added no source entry, and preserved all
then-existing system entries and unrelated local sources. That repeat proves
repeat safety, not retroactive byte-for-byte preservation of the first aggregate
rewrite. The final default TLS tests below independently establish actual trust.

### Task-owned restart and retained cleanup records

Before stopping anything, the old private ownership record, exact commands,
executables, UID, start ticks, parent/group membership and listeners were
revalidated. The live task group contained exactly the two known processes:
**43130 / 1378797** and **43843 / 1390583** (PID / start ticks). Linux pidfds bound
signals to those verified processes, avoiding PID-reuse signaling. The app exited
gracefully; no forced kill or unrelated process/core/agent/service restart occurred.

A new private supervisor launched the same normal command from the same backend
directory, with inherited development dependencies and unchanged `Web` profile:

```text
dotnet run --project /home/agent/workspace/Deals/backend/src/WebApi.External/WebApi.External.csproj --launch-profile Web
```

Retained `/proc` identities after the activation helper exited:

| Task process | PID | Start ticks | Process group |
|---|---:|---:|---:|
| Private supervisor | 54710 | 1891007 | 54710 |
| `dotnet run` | 54711 | 1891019 | 54711 |
| External API apphost | 55039 | 1896512 | 54711 |

The new **0600** private `ownership.json` under the runtime directory records these
identities; app/supervisor logs remain private there. For separately authorized
cleanup, revalidate that record, UID, start ticks, executable and lineage before
signaling only this task's apphost/runner (or verified group **54711**). Its
supervisor records exit and terminates after the runner exits. The **old cleanup
PIDs in the Deals baseline are superseded**, not reusable stop targets. Preserve
runtime files while the app uses them and preserve the separately authorized
public trust entry unless its removal is also intended.

The API still owns **127.0.0.1:8203 and [::1]:8203**, not wildcard listeners. The
existing relay remains **PID 46234 / start ticks 1509747**, forwarding **60719 ->
8203**. Inventory still contains only **127.0.0.1:18203 -> 60719/tcp4**; host
inspection found only **127.0.0.1:18203**. No mapping/relay mutation occurred.

### Post-helper-exit checks and scope

Separate `--check` processes passed at **12:05:29Z** and **12:07:47Z**:

| Client / request | Result with default trust and hostname validation |
|---|---|
| Windows host `https://localhost:18203/` | callback-free `SslStream` authenticated; full peer DER equals host DER; curl **0**, HEAD **404** |
| Windows host `https://localhost:18203/swagger/index.html` | curl **0**, HEAD **200** |
| Sandbox `https://localhost:8203/` | `ssl.create_default_context()`, `CERT_REQUIRED`, hostname checks on; exact host DER SHA-256; curl **0**, HEAD **404** |

No validation bypass, custom trust bundle, browser exception, credentials,
redirects, response-body retrieval or business endpoint request was used.
The earlier failed pre-copy diagnostic callback returned only
`errors == SslPolicyErrors.None`; **the successful host check has no callback**.

[Public helper source and usage](story-3-host-certificate-probe/README.md) are
retained for evidence. Debug and Release builds passed with **zero warnings /
errors**; framing self-tests passed a round-trip and **7** invalid/truncated/
corrupt/oversized cases without filesystem changes; CSharpier passed. The
read-only helper preflight initially needed BOM-aware JSON handling for the
unchanged launch profile. Product/app source was not edited and product solution
tests were not rerun for this live-verification-only task.

Full #3/#46 bodies/comments were reread; both remain open with empty comment
arrays. This establishes **Windows-host manual HTTPS success plus sandbox-client
trust**, not an observed browser reload, independent Linux-host verification,
automatic launcher/agent/core propagation, renewal behavior or production #46
activation. The separate implementer owns that production work. The verifier
changed only this note and public helper source, leaving them unstaged and
uncommitted for the integration coordinator to preserve separately. During that
verification, no Git/index/issue writes, host trust changes, sandbox lifecycle,
app-source/profile/bind edits, daemon/global-settings or registry changes occurred.

## Historical result — confirmed allow-rule directory-read retry denied, 2026-11-16

**Stopped at the external-directory gate again, before export or activation.**
After the user confirmed **“it's added”** for narrow OpenCode
`external_directory` and `edit` allow rules on
`C:/Users/UI520628/AppData/Local/Temp/12/opencode/abox-story3-host-cert-20261116/*`,
this fresh run's first operation was `functions.read` on the exact staging
directory:

```text
C:/Users/UI520628/AppData/Local/Temp/12/opencode/abox-story3-host-cert-20261116
```

It returned **`Permission denied: external_directory`**. This was a directory-read
attempt, **not** a retry of the previous `patch` marker creation; the edit rule
was not exercised in this run. No alternate tool/path or permission workaround
was attempted after the read denial, and no broader permission was requested.

Repository standards/domain pointers, both verification notes, HTTPS research,
certificate-mechanism findings and the full #3/#46 bodies/comments were reread.
Both issues remain open with empty comment arrays. Certificate eligibility/trust,
staging ACL/contents, sandbox mounts and task-owned PID/start ticks were **not
revalidated** in this run. Earlier observations below remain historical.

No password, private export, runtime copy/configuration or helper was created;
no certificate/store operation, sandbox call, API stop/restart or TLS request was
performed. There is no private export from this run to clean up. Only this note
was changed, left unstaged/uncommitted; application/relay state was not touched.
CLI-only export and the existing-only candidate/ACL/ownership gates still apply
if a later authorized run can actually access the approved staging directory.

## Historical result — approved-path patch retry denied, 2026-11-16

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

## Historical resume gate — superseded by the successful manual run above

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
