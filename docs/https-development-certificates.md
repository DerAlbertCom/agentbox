# Development HTTPS: exact identity and persistent activation

`abox ports` now configures a protected, explicit Kestrel default certificate for
statically detected ASP.NET Core Web SDK projects, in addition to publishing ports.
It does not start/restart applications or implement automatic create/replace setup
(#47). **#46 acceptance remains incomplete.** Synthetic production-path evidence
and separate manual real-host evidence are distinguished below; neither establishes
the complete production flow with the real host identity, browser reload,
independent Linux-host verification or a Copilot shell-tool run.

## Prerequisites and host export

- An existing, currently valid, exportable ASP.NET Core development identity:
  `CN=localhost`, localhost DNS SAN, usable private key and **Full** host trust.
  Resolve missing/untrusted certificates yourself; abox never automatically runs
  standalone `--trust`, `--clean`, `--import`, installs tools or substitutes a new
  identity. No host trust-store changes or sandbox certificate-store import occurs.
- An **already installed** SDK containing a compatible dev-certs CLI. abox discovers
  `dotnet --list-sdks`, reads the tool's certificate-format constant from its PE
  metadata without loading it, then executes its explicit DLL with `dotnet exec`.
  Certificate format is not SDK major version. An older-format existing identity
  must be eligible for an installed tool or setup fails actionably, before export;
  there is no native-store private-key export fallback.
- The selected tool's machine-readable report must contain one eligible identity.
  Ambiguous, empty, malformed, untrusted or nonexportable reports never authorize
  export. Successful metadata exit status alone is not trust evidence.

Export uses `https --export-path ... --password ... --format Pfx`, with a generated
password. `ProtectedTemporaryDirectory` creates an owner-only directory **before**
the CLI writes: inheritable current-user-only Windows DACL, or Unix 0700. Linked
ancestors are rejected. Child-only settings disable SDK first-run certificate
generation/telemetry and direct tool-created temporary files into private staging.
Sensitive export output is discarded even with `--verbose`; metadata is privately
captured. Raw exceptions/diagnostics never become user-facing errors. Partial
exports are scoped for cleanup on success, failure and cancellation; cleanup
warnings preserve the primary failure without printing private material.

The actual PFX is loaded ephemerally, checked for the expected thumbprint, subject,
format extension, dates, localhost SAN and usable RSA private key (sign/verify),
and correlated with a **fresh** exact Full-trust report before transfer. Its PFX
bytes are not reserialized or replaced by another certificate.

**CLI race limitation:** dev-certs has no identity selector or atomic existing-only
export switch. Prevalidation prevents the known unsupported-format/missing-candidate
creation path; postvalidation rejects changed identities. It cannot prevent CLI
creation during concurrent store deletion/renewal between those checks. Do not
change certificates concurrently; no absolute TOCTOU/store-mutation guarantee is
made. The product decision requires this CLI rather than a native export fallback.
Its password argument is visible to privileged process-argument inspection for the
export's lifetime; output suppression cannot remove that CLI limitation.

## Detection and shared setup

The shared `ApplicationPorts` discovery reads ordinary bounded `.csproj`, `.fsproj`
and `.vbproj` XML for an explicit `Microsoft.NET.Sdk.Web` project SDK (including
literal version suffixes and `Sdk Name` elements). It never evaluates MSBuild,
imports, targets, scripts or package tasks. Standard artifact/link exclusions still
apply. Computed/import-only custom SDKs are not inferred. Such projects need an
explicit supported SDK declaration to opt into this static detection contract.

Non-Web/non-.NET folders perform **no host dotnet certificate operation**. An
explicit Web SDK project requires certificate setup even without discovered launch
ports. Shared `DevelopmentHttps` orchestration lives outside command slices, so
#47 can reuse it through `AppPorts.PublishAsync` with the immutable entry-time folder
snapshot's **explicitly selected offset**, without a second command implementation.
No lifecycle integration is included here.

Port conflicts/offset validation retain #44/#45 behavior: unproven native direct
bindings are conflicts at every offset, never automatically unpublished. HTTPS
failure can occur after forwarding succeeds; no rollback of mappings or a committed
certificate is promised. Correct the prerequisite and rerun folder-qualified
`abox ports --location "<folder>" [--offset N]`.

## Runtime storage and atomic refresh

Every runtime operation uses `SbxClient`, argv lists, the actual default non-root
application user, and sensitive length/digest-framed stdin. Linux, Python 3.8+,
`sh`, `id` and readable `/proc` are prerequisites for configured sandboxes. Nothing
is installed. The receiver checks the size, SHA-256, JSON/base64 framing and rejects
links, nonregular/multiply-linked files, wrong ownership, unexpected entries and
permissive modes. Mountinfo validation rejects persistent/mounted `/tmp`, ancestors
or descendants of the selected runtime directory; there is no HOME/workspace/build
directory fallback.

Persistent runtime-only files under `/tmp/abox-https-<effective uid>/` are:

- `active.pfx`: unchanged encrypted PFX bytes, ordinary single-link 0600 file.
- `config.json`: protected 0600 configuration with the canonical path and generated
  password. It is not an appsettings file or a public shell profile.
- `.lock`: private nonblocking writer lock. The containing directory is 0700.

The **path and password remain stable for the lifetime of that sandbox's HTTPS
configuration**. Each export uses that same generated password. Refresh therefore
needs one atomic same-directory PFX replacement, not two independently renamed
files or stale inherited password/PFX pairs. No agent needs to read a new password
to adopt a renewed file. Successful cleanup retains one active PFX/config/lock,
not an unbounded generation history. Initial setup commits configuration before
the first PFX; an interruption there causes launches to fail closed, never fall
back to an unrelated development certificate. A retry reuses that configuration.

Separate bounded cleanup attempts remove the exact transfer's private temporary
file after failure/cancellation. A successful serialized retry removes validated
abandoned transfer files. Contended writers fail for retry rather than hang.
After a lost acknowledgement the new PFX may already be committed; no rollback,
power-loss durability or secure-erasure claim is made. The earlier private
`StageDevelopmentCertificateAsync` bundle adapter remains a transport-only seam;
production setup uses activation and removes a validated old transport bundle.

## Launch propagation and existing parents

All `SbxClient.ExecAsync` launches cross a quiet shared bootstrap, including shell
and explicit argv, hidden `abox exec` and byte-transparent `abox pipe` (Herdr's
transport). Without HTTPS state it directly executes the original argv without
requiring Python. With state, it validates protected runtime configuration, sets:

```text
ASPNETCORE_Kestrel__Certificates__Default__Path
ASPNETCORE_Kestrel__Certificates__Default__Password
```

and replaces itself with the original argv. It does not consume stdin, interpolate
user arguments into a shell script, print secrets, change workdir/TTY flags or
replace a child's exit code. This is a persistent file-backed exec contract, not
`BASH_ENV`/profile-only environment changes that disappear with abox.

**Unix cannot mutate another process's environment.** Existing shells, agents and
cores must be deliberately replaced by fresh parents initialized through abox.
Setup and subsequent configured launches inspect same-user process environments
for recognizable hc/herdr-core, Copilot, OpenCode, pi and Claude Code entrypoints,
including native `.exe` packaging and common Node/Bun entrypoints. A stale relevant
parent blocks full activation and launches with an actionable diagnostic (42).
Uninspectable relevant process state fails closed. Herdr's launcher checks this
before opening the desktop, rather than reporting success for a stale remote core.
Readiness is a snapshot, not an atomic process-lifetime lease. Renamed/custom
wrappers and arbitrary pre-existing shell descendants cannot be universally
identified: **fresh parent creation through abox remains required**.
Launching directly with raw `sbx exec` bypasses this supported bootstrap contract.

Save work and obtain consent before recovery; abox does **not** do this itself:

```sh
# If the installed hc supports --fresh:
abox run shell -- hc server restart --fresh

# Older hc (verified 0.1.17 has no --fresh): explicitly stop, then initialize anew.
# Stop can interrupt core-managed sessions.
abox run shell -- hc server stop
abox run shell -- hc server start

# Existing OpenCode's service, not just its client, must have a fresh environment:
abox run shell -- opencode service stop
# Then reopen OpenCode through abox's shell or properly initialized Herdr.
```

These exact recovery commands are allowed through a stale-parent gate; no silent
restart occurs. Exit other stale agents yourself, open fresh ones, then rerun
`abox ports`. An ordinary hc restart may preserve its old environment. Closing an
OpenCode UI does not necessarily stop its service. Existing apps require an
explicit restart for a reliable renewal contract; no universal hot-reload claim
is made. Custom endpoint certificates, TLS callbacks and tool-specific environment
filters can override/remove these defaults and are outside the default contract.

## Trust boundary and evidence

PFX plus password grants the sandbox application user/root the host's localhost
private identity. Do not snapshot a provisioned sandbox as a template. No material
is intentionally added to app sources, images, build data, publication or host trust
stores. Strings/process memory are not securely erasable. Ownership/link/mount
checks are not an authorization barrier against a malicious application user/root
editing its own files concurrently.

[The production-path verification record](verification/https/README.md) uses
synthetic export mocks, real disposable .NET 10 Kestrel activation and actual local
tool subprocesses. Separately, [the manual Windows-host verifier](agents/story-3-host-certificate-verification.md)
completed an actual CLI export of the existing trusted host identity and explicit
Deals activation after the earlier permission denial was resolved. The unchanged
relay served that exact leaf with default host TLS (Swagger HEAD 200), and sandbox
default TLS also passed (HEAD 404); host trust was unchanged. Its separately
authorized public sandbox trust entry is manual setup, not production behavior.
That proof is not a production `abox ports` real-host run, browser reload, Copilot
shell-tool check, host Herdr desktop connection or independent Linux-host evidence.
