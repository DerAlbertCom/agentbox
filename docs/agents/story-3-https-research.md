# Story #3: HTTPS research for #46

Read this when implementing certificate export, runtime propagation, refresh, or
verification for [#46](https://github.com/DerAlbertCom/agentbox/issues/46).
Port publishing belongs to #43 and is outside this research.

## Evidence and acceptance gate

Research date: 2026-11-16; source baseline:
`8103a51eddc2d3cf713ea7249fda8c83055fe176`.
Read the full bodies/comments of [#3](https://github.com/DerAlbertCom/agentbox/issues/3)
and #46; both comment arrays were empty. Native dependency lookup reported #46
blocked by open #43. Recheck issues before implementation.

**No mechanism was live verified. No acceptance deviation was approved.**
Findings below are source/help evidence or explicitly labelled proposals, not
proof of runtime propagation or trusted `https://localhost:18203` access.
Research performed no sandbox exec/probes, certificate exports or real-store
checks, settings/trust changes, builds, or tests. Local executable inspection was
help/version only: sbx v0.51.0 (`7d55286de0c775148b985ed349f760f1eba06203`)
and host hc 0.1.21. Sandbox hc versions remain unknown.

## Verified propagation limits

- Docker documents `/etc/sandbox-persistent.sh` for future sessions of existing
  sandboxes. Noninteractive Bash loads it through `BASH_ENV`; direct `sbx exec`
  commands do not start a shell. Existing sessions/agents must restart. [D1–D3]
- Current abox launch paths include both cases: `ShellLauncher` defaults to
  `bash -l` but passes explicit argv directly; hidden `exec` and `pipe` share
  `SbxClient.ExecAsync`. Herdr's registered transport is
  `abox pipe <sandbox> -- sh -c`. Noninteractive `sh` does not load Bash startup
  files. Login profiles alone therefore miss supported launch paths. [B1]
- Herdr-core is a separate long-lived environment parent. Upstream says ordinary
  `hc server restart` preserves its original environment; `restart --fresh` takes
  the invoking shell's environment. `hc exec` runs argv without a shell, and its
  configurable default shell need not be Bash. A new transport client's
  environment does not establish propagation into an existing core. Host help
  confirms `--fresh`, not its availability/behavior in the sandbox. [H1–H2]
- Create `--env/--env-file` stores sandbox environment. Reattach `sbx run --env`
  updates that agent session only; `exec --env` updates one command. Updated
  `sbx env run` values apply to new agent sessions. No supported command to
  rewrite every existing sandbox launch's stored environment was found in the
  inspected help/docs; this is not proof no private mechanism exists. [D1, D4]
- `sbx kit add` recreates the container; it is not an innocuous environment setter.
  Local startup hooks run alongside agents, and exporting in a hook subprocess
  cannot update another process's environment. [D5]

Local entry points to inspect before changing the launch contract:

- `cli/src/Abox/Launchers/{ShellLauncher,HerdrAppLauncher}.cs`
- `cli/src/Abox/Features/Sandbox/{ExecCommand,PipeCommand}.cs`
- `cli/src/Abox/Sbx/SbxClient.cs` (`ExecAsync`)
- [Implemented launchers](../implementation.md):
  shell and herdr-app. Orca is proposed, not implemented. The intended agents
  are Copilot, OpenCode, pi and Claude Code.

## Unverified design options

**Explicit bootstrap:** persist canonical Kestrel Path/Password configuration in
protected runtime-only state; integrate with the existing persistent Bash entry
point and a fixed, quiet bootstrap at abox's shared exec seam for direct argv and
pipe. Preserve original argv, stdin/stdout, TTY/workdir and exit codes. A bootstrap
must read current state and execute argv without interpolating secrets/user
arguments into shell text. Preserve unrelated startup-file content and validate
its owner/type/link status. Root-owned `/etc` does not allow the same non-root
same-directory atomic replacement strategy as a private home directory.

This still needs an explicit policy for already-running hc cores, such as an
authorized, correctly initialized fresh restart, and evidence for all agent tool
paths. A desktop reconnect or ordinary core restart is not that evidence.
Keep user application restarts explicit.

**Certificate-store fallback:** pinned .NET 10 Kestrel source reads development
certificates from `CurrentUser/My` after explicit/default configured certificates.
It returns a found certificate even without sandbox trust, logging a warning.
Importing the host certificate into the sandbox application user's store could
therefore avoid shell inheritance, including direct apphost launches. [M2–M4]

This is **not an approved substitute** for #46's runtime configuration contract.
CLI import requires `--clean`; cleanup can remove existing sandbox certificates
and trust and is not atomic with import. SDK availability, protected store access,
existing-certificate preservation and runtime compatibility need investigation.
.NET 10 selection requires development-certificate version >=4, so importing an
older host certificate does not establish that an app will select it. Avoid
hardcoding the Unix store layout as a supported interface.

## Verified export/trust hazards

- Password-protected PFX export includes the private key. The current dev-certs
  CLI offers no password-stdin/password-file option. Diagnostic suppression does
  not prevent OS process-argument inspection of `--password`. [M1–M2]
- `--check --trust` checks without changing trust, but succeeds when any valid
  candidate is fully trusted. Export selects the first eligible certificate,
  without filtering to trusted candidates. An export success is not trust proof.
- In pinned .NET 10 source, `--check-trust-machine-readable` emits a list and exits
  0 even for empty/untrusted results. Parse and validate metadata, not just status.
- `--check` cannot be combined with export; there is no thumbprint export selector.
  Ordinary export can create a certificate if none qualifies. Prerequisite checks
  help but do not make concurrent renewal/deletion atomic. [M2–M3]
- Kestrel defaults do not override arbitrary endpoint certificates or custom TLS
  callbacks. Canonical settings are a default contract, not universal forced TLS
  configuration. [M6]

Proposed sequence: detect applicable .NET sources without MSBuild execution;
resolve the actual existing sandbox through `SandboxResolver`; privately capture
trust metadata; export into protected temporary storage; load the PFX ephemerally;
validate private key, dates and localhost SAN; correlate the actual certificate
with current Full-trust metadata before copying. Reject changed/mismatched or
malformed/empty metadata. Suppress SDK first-run dev-certificate generation with
the documented `DOTNET_GENERATE_ASPNET_CERTIFICATE=false` setting. [M5]

Host trust stays untouched: missing/unusable/untrusted certificates receive
user-operated recovery guidance, never automatic standalone `--trust`, `--clean`
or `--import`. Browser NSS trust on Linux and curl/OpenSSL trust are distinct;
an explicit CA bundle does not prove that the browser already trusts it. [M5]

## Secure transport and refresh guidance

- Reuse patterns from `SbxClient.UserSecrets.cs`: actual default non-root exec
  user, runtime HOME/APPDATA resolution, ancestor-link/type/owner/single-link
  checks, 0700 directories, 0600 files, framed stdin and atomic replacement.
  Its current payload is text JSON, not generic binary transport. For PFX,
  propose strictly checked base64/length/digest framing or deliberate binary
  support; raw PFX cannot pass unchanged through string `StandardInput`.
- `ProcessRunner` provides `Sensitive` output discard and `CapturedSensitive`
  private metadata capture. Use these for secret operations instead of attached
  `ExecAsync`/normal child diagnostics. Keep sensitive inner exceptions and
  record/string representations out of errors, including verbose failures.
- `TemporaryDirectory.cs` creates an ordinary directory: it is not sufficient
  unchanged for private-key exports. Unix `CreateTempSubdirectory` documents
  0700; Windows inherits ACLs. Establish an owner-restricted inheritable DACL
  before exporting, validate ordinary paths, and avoid post-export permission
  tightening. At-creation `DirectorySecurity.CreateDirectory` is an option. [F1–F3]
- Choose application-user runtime storage outside workspace/build-source mounts
  and publication inputs. HOME alone is insufficient: whole-home state mounts
  can make that path host-persistent. Docker's example persistent shell file is
  0644: keep passwords in separately protected state, not public shell exports.
- Treat PFX and password/config as one refresh transaction. Fresh password plus
  overwritten PFX breaks launches inheriting old settings. Stable per-sandbox
  generated password/path or coordinated activation require design/tests; two
  atomic renames do not create multi-file atomicity. Old generations cannot
  accumulate, but deleting them too early races launches.
- Cleanup covers partial exports, failures and cancellation after child
  termination. Remote traps alone do not prove cleanup after killing sbx; use
  safe retry cleanup of validated abox-owned temporary files. Preserve primary
  failures/cancellation and report cleanup recovery without payload. Crash/power
  loss cleanup is best effort, not secure erasure.
- PFX plus password grants the host localhost identity/private key to sandbox
  application user/root. Document that trust grant and warn that manual sandbox
  template snapshots capture filesystem secrets. Keep abox build/publication
  paths entirely separate. [D1, M6]

Behavioral test patterns: `cli/test/Abox.Tests/Sbx/{UserSecretsTransportTests,UserSecretsSafetyTests}.cs`
and infrastructure process-policy tests. These were inspected, not rerun.

## Required verification before claiming completion

1. Synthetic coverage: non-.NET no-op; missing SDK/cert/private key/trust; malformed
   and empty successful metadata; multiple-candidate export/trust mismatch;
   wrong SAN/dates; normal/verbose child-output suppression; export-time ACLs;
   cleanup on success/failure/cancellation; non-root destination/link/owner checks;
   truncated/corrupt transfer; idempotent and concurrent refresh/renewal.
2. Provisioning abox must exit before test apps start: default shell, direct argv,
   hidden exec, byte-transparent pipe, fresh and already-running hc, each intended
   agent's real tools, noninteractive Bash/sh, direct dotnet and apphost launches.
   Record initial-enable and renewal/password-change restart requirements.
3. Already-running apps need restart for a reliable renewal contract; environment
   changes may also need new agent/core parents. Kestrel store fallback caches its
   lookup; no universal certificate hot-reload claim is supported. [M4–M5]
4. With separate authorization, record independent Windows-host and Linux-host
   live evidence at `https://localhost:18203` using ordinary trust and hostname
   validation, without bypass callbacks or `-k`. Coordinate connectivity with #43.
   Synthetic tests and source inspection cannot satisfy this live evidence gate.

## Primary sources

- **D1:** [Docker usage](https://docs.docker.com/ai/sandboxes/usage/): environment scope,
  persistent file, restarts and template secret-capture warning.
- **D2:** [Workload tutorial](https://docs.docker.com/ai/sandboxes/customize/author/build-an-agent/):
  persistent-file/BASH_ENV wiring and example permissions.
- **D3:** [sbx@1 contract](https://github.com/docker/sandbox-kit-spec/blob/main/docs/spec/capabilities/com.docker.sandbox/sbx@1.md).
- **D4:** [Environment updates](https://docs.docker.com/ai/sandboxes/configuration/environment-files/#update-an-environment).
- **D5:** [Capability support and kit-add limits](https://docs.docker.com/ai/sandboxes/customize/author/capabilities/).
- **H1:** [herdr-core README](https://github.com/herdrdev/herdr-core/blob/master/README.md).
- **H2:** [Generated hc command reference](https://github.com/herdrdev/herdr-core/blob/master/docs/commands.md).
- **B1:** [GNU Bash startup files](https://www.gnu.org/software/bash/manual/html_node/Bash-Startup-Files.html).
- **M1:** [dotnet dev-certs](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs).
- **M2:** [.NET 10 CLI source](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Tools/dotnet-dev-certs/src/Program.cs):
  checks, JSON report, incompatible flags and import requirements.
- **M3:** [.NET 10 CertificateManager](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Shared/CertificateGeneration/CertificateManager.cs):
  selection, export, import, cleanup and certificate-version rules.
- **M4:** [.NET 10 KestrelServerOptions](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Servers/Kestrel/Core/src/KestrelServerOptions.cs):
  `ApplyDefaultCertificate` and `GetDevelopmentCertificateFromStore`.
- **M5:** [HTTPS trust/renewal guidance](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-10.0).
- **M6:** [Container HTTPS settings](https://learn.microsoft.com/en-us/aspnet/core/security/docker-https?view=aspnetcore-10.0)
  and [Kestrel certificate defaults](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0).
- **F1:** [CreateTempSubdirectory contract](https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.createtempsubdirectory?view=net-10.0).
- **F2:** [Windows temporary-directory source](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/Directory.Windows.cs).
- **F3:** [At-creation directory ACL extension](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemaclextensions.createdirectory?view=net-10.0).

Docker/Herdr links track current upstream documentation; pinned .NET source proves
the named version only. Recheck installed versions before relying on these facts.
