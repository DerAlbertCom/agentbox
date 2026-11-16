# Development HTTPS: exact identity and persistent activation

`abox ports` now configures a protected, explicit Kestrel default certificate for
statically detected ASP.NET Core Web SDK / `Microsoft.AspNetCore.App` projects, in addition to publishing ports.
It does not start/restart applications. Successful creation/replacement also uses
this same shared setup; see [lifecycle ordering and recovery](application-ports-lifecycle.md).
**#46 was accepted for the current Windows-host scope**, per the
[tracker clarification](https://github.com/DerAlbertCom/agentbox/issues/46#issuecomment-6918879462),
which supersedes the older outstanding-gates status, not its evidence boundaries.
[Windows real-host production evidence](verification/https/real-host-production-verification.md)
establishes strict host/system/Node TLS, repeat safety and bootstrap-only refresh on
exact candidate `5b40b570aff9fb76b9c6379e55569eef83dc2df9` (implementation `fdd1916`).
Its supplemental UTF-8 caller-CA check and synthetic regression checks remain
distinct from the full production CLI run and historical manual evidence.
The user confirmed the existing Deals browser lock at 18203 and working Herdr SSH;
that manual runtime is not relabelled as the disposable production fixture. Copilot
is excluded, independent Linux-host verification is deferred, and no real-host
renewal was authorized or performed. These are not reinstated lifecycle gates.

## Prerequisites and host export

- An existing, currently valid, exportable ASP.NET Core development identity:
  `CN=localhost`, localhost DNS SAN, usable private key and **Full** host trust.
  Resolve missing/untrusted certificates yourself; abox never automatically runs
  standalone `--trust`, `--clean`, `--import`, installs tools or substitutes a new
  identity. Host trust never changes. The exact **public** leaf is installed in the
  selected sandbox's existing system trust store; root never receives the private
  PFX/password. This requires root exec and an already-installed Debian-style
  `/usr/sbin/update-ca-certificates`; unsupported stores fail actionably, without
  package installation or a different trust mechanism being silently substituted.
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
literal version suffixes and `Sdk Name` elements) or an explicit
`FrameworkReference Include="Microsoft.AspNetCore.App"`. It never evaluates MSBuild,
imports, targets, scripts or package tasks. Standard artifact/link exclusions still
apply. Computed/import-only custom SDKs are not inferred. Such projects need an
explicit supported declaration to opt into this static detection contract.
Unreadable or linked project files fail with file-access guidance: without reading
their bytes, discovery cannot safely establish that HTTPS is unrelated. Readable
unrelated malformed/DTD-bearing project files are unsupported and ignored, not new
blockers for existing port files. Malformed bounded project text
explicitly mentioning either ASP.NET declaration fails closed (conservatively,
even in a comment); DTDs/entities are never evaluated. Oversize unrelated projects
are ignored; an ASP.NET marker in the bounded prefix retains the 1 MiB error.

Non-Web/non-.NET folders perform **no host dotnet certificate operation**. An
explicit Web SDK project requires certificate setup even without discovered launch
ports. Shared `DevelopmentHttps` orchestration lives outside command slices, so
creation/replacement reuses its prepared `AppPorts` flow with the immutable entry-time
folder snapshot's **explicitly selected offset**, without a second command implementation.

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
- `.lock`: private exclusive runtime lock. The containing directory is 0700.
- `bootstrap.py`: installed configuration reader, checked against this CLI's exact
  SHA-256 before execution; it is not copied into every exec argument list.
- `public.pem`, bounded `node-<digest>.source` / `.pem` pairs: protected public
  Node trust material. Each bundle preserves the original extra-CA bytes plus the
  current leaf. All these runtime files are ordinary single-link 0600 files.

System trust uses one narrowly named root-owned 0755 directory
`/usr/local/share/ca-certificates/abox-development-https-<uid>/` with one root-owned,
ordinary single-link 0644 `abox-development-https-<uid>.crt` and private lock.
Root-owned ancestors, links, modes and mount layouts are checked, including the
system bundle/updater paths; mounted trust/runtime destinations fail rather than
possibly writing through a host bind mount. Only exact public PEM is accepted.
Refresh replaces that single leaf and runs `update-ca-certificates` even on an
identical retry, recovering a lost/failed update acknowledgement. Validated owned
temporary files are cleaned; unrelated certificates are never removed. There is
no cross-store/private-activation transaction: interrupted trust/setup can be
partial and must be retried; success is printed only after trust, activation and
readiness all succeed. Existing images without the store/updater are unsupported.

The **path and password remain stable for the lifetime of that sandbox's HTTPS
configuration**. Each export uses that same generated password. Refresh therefore
needs one atomic same-directory PFX replacement, not two independently renamed
files or stale inherited password/PFX pairs. No agent needs to read a new password
to adopt a renewed file. Successful cleanup retains one active PFX/config/lock plus the protected bootstrap/public trust files,
not an unbounded generation history. Initial setup commits configuration before
the first PFX; an interruption there causes launches to fail closed, never fall
back to an unrelated development certificate. A retry reuses that configuration.

Separate bounded cleanup attempts remove the exact transfer's private temporary
file after failure/cancellation. A successful serialized retry removes validated
abandoned transfer files. Launch, prepare, status, activation, staging and cleanup
all use the **same exclusive lock**, including process scans and atomic Node-bundle
writes; this is not a shared-reader/writer scheme. Launches retry contention for up
to **five seconds**; other operations, including cleanup, wait up to three seconds
(cleanup remains inside its separate five-second host budget). Signal cancellation interrupts the wait. Persistent
contention returns a distinct busy/retry result (75), not invalid configuration
(78); readiness is not claimed and unconfirmed cleanup is still reported honestly.
After a lost acknowledgement the new PFX may already be committed; no rollback,
power-loss durability or secure-erasure claim is made. The earlier private
`StageDevelopmentCertificateAsync` bundle adapter remains a transport-only seam;
production setup uses activation and removes a validated old transport bundle.

## Launch propagation and existing parents

All `SbxClient.ExecAsync` launches cross a quiet shared bootstrap, including shell
and explicit argv, hidden `abox exec` and byte-transparent `abox pipe` (Herdr's
transport). Without HTTPS state it directly executes the original argv without
requiring Python. With state, a short hash-checked installed Python reader validates
protected configuration **under the setup lock**, then returns only fixed-format
values to the shell exec parent. It sets:

```text
ASPNETCORE_Kestrel__Certificates__Default__Path
ASPNETCORE_Kestrel__Certificates__Default__Password
NODE_EXTRA_CA_CERTS
```

Kestrel settings remain available for ordinary/recovery launches when only Node
bundle preparation fails, as described below. Invalid protected runtime state
still fails closed. **After a CLI upgrade**, an old installed bootstrap has a
different exact hash: all configured launches, including ordinary/recovery
commands, return 78 rather than executing unverified installed code or launching
without the protected environment. Use the explicit reader-only recovery command:

```sh
abox ports --location "<folder>" --refresh-https-bootstrap
```

This mode uses the new CLI's embedded receiver directly through `SbxClient`, never
the unverified installed reader. Under the runtime lock it checks existing protected
paths, ownership/modes, configuration and the public PEM, then atomically replaces
only `bootstrap.py`. Private PFX/configuration, password/path and existing Node roots
are retained. It needs sbx preflight/discovery, the application user, Python and valid
existing protected activation; it does **not** read project/folder configuration,
publish ports, export host identity or require root trust work. It cannot be combined
with `--offset`. Caller `NODE_EXTRA_CA_CERTS` is not read. Successful refresh is not
HTTPS/Node/agent readiness: retry the original launch, or ordinary `abox ports` for
full setup/status. Missing/unsafe activation fails without creating or replacing it.
Cancellation may occur after the atomic reader replacement; explicitly retry.

If protected state is damaged, correct its ownership/integrity or run full
folder-qualified `abox ports` (which requires readable discovery inputs, an exportable
trusted host certificate and sandbox root trust prerequisites). Explicitly recreating
the sandbox is a last resort, **destructive to nonpersisted sandbox files/worktrees
and running sessions**; save work first and review the create confirmation. abox never
deletes/recreates a sandbox automatically to recover a bootstrap mismatch.

**Known storage-write limitation:** disk-full or read-only runtime write failures
currently fail closed for all configured launches, including ordinary save/recovery
commands; Node bundle preparation writes even when a bundle already exists.
Bootstrap-only refresh also requires writable runtime storage. A trusted operator
can deliberately use raw `sbx exec` outside the abox wrapper to save work or repair
storage, or free storage externally. This escape bypasses the protected abox launch
contract; it is not HTTPS readiness or an automatic fail-open fallback. abox does
not automatically delete user data or recreate a sandbox to recover storage.

The shell replaces itself with the original argv. Python is never the app's
exec parent: CPython's ignored SIGPIPE/SIGXFSZ and locale initialization cannot
leak into the child. Inherited user-ignored signals remain ignored; native child
termination and signal cancellation are not mislabeled configuration error 78.
It does not consume stdin, interpolate
user arguments into a shell script, print secrets, change workdir/TTY flags or
replace a child's exit code. This is a persistent file-backed exec contract, not
`BASH_ENV`/profile-only environment changes that disappear with abox.

**Unix cannot mutate another process's environment.** Existing shells, agents and
cores must be deliberately replaced by fresh parents initialized through abox.
Setup and subsequent configured launches inspect same-user process environments
for recognizable hc/herdr-core, Copilot, OpenCode, pi and Claude Code entrypoints,
including native `.exe` packaging and common Node/Bun entrypoints. A stale relevant
parent blocks full activation and **new recognized agent launches** with an
actionable diagnostic (42). Ordinary shell/exec/pipe save/exit and the explicit
recovery commands remain available, with an accurate stderr warning; old agents
are never claimed to have acquired new settings. Uninspectable process state is
reported separately (43), not invalid configuration 78, and also leaves ordinary
transport available. Herdr's launcher checks readiness
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

These recovery commands and ordinary save/exit transport remain available subject
to the protected-state/storage requirements above; no silent restart occurs.
Exit other stale agents yourself, open fresh ones, then rerun
`abox ports`. An ordinary hc restart may preserve its old environment. Closing an
OpenCode UI does not necessarily stop its service. Existing apps require an
explicit restart for a reliable renewal contract; no universal hot-reload claim
is made. Custom endpoint certificates, TLS callbacks and tool-specific environment
filters can override/remove these defaults and are outside the default contract.

Node often ignores the system store. The reader merges the exact public leaf with
the **bytes** of the caller's existing `NODE_EXTRA_CA_CERTS`, retaining Node's
built-in roots and original extra roots. Unset/empty means no original extra
roots. A propagated abox bundle reuses its protected original source, not a
previous merged bundle (so renewals do not accumulate old leaves). The original
must resolve to an accessible regular PEM file of at most 1 MiB (an empty file
adds no original roots). Caller-selected symlinks, including system/corporate CA
links, are followed **only as the unprivileged application user**. Reads are bounded
and nonblocking; directories/devices/FIFOs, unreadable/oversize inputs and invalid
PEM fail Node readiness. For a nonempty input, at least one complete `CERTIFICATE`
PEM block is required and each block is validated as ASCII using SSL in memory.
Only the `CERTIFICATE` label is supported; legacy `X509 CERTIFICATE` labels remain
an unsupported compatibility case, even where Node/OpenSSL accepts them.
Non-ASCII comments, Bag Attributes and other bytes **outside** these blocks are
allowed, matching Node/OpenSSL; original bytes still determine the digest and are
stored/merged unchanged, not decoded or reserialized. Malformed/incomplete certificate
blocks are not silently accepted. Abox-owned runtime paths/sources retain strict no-follow,
ownership, mode and single-link checks. Up to 16 distinct source-content variants are retained per
sandbox, without evicting trust files potentially needed by running Node clients;
a 17th fails Node readiness with recovery guidance. Caller-input/capacity failure blocks
setup/status success (44) and recognized new agent launches (78), but **not ordinary
commands or the explicit recovery argv above**. Those commands receive the verified
Kestrel settings, retain the caller's original `NODE_EXTRA_CA_CERTS` unchanged and
print a warning on stderr that Node trust is **NOT configured**. A missing input is
not treated as empty or silently replaced, matching Node's warning semantics;
ordinary child exit status is preserved. Recovery access is not Node-trust success.
Protected-state failures are different: missing/invalid `public.pem`, unsafe owned
files, or missing/corrupt inherited `.source` data fail closed for **all** configured
launches, including recovery (78), and status fails as protected configuration, not
caller-input status 44. An existing source for the selected digest must still match
the original bytes. These failures never fall back to a potentially incomplete
owned trust configuration or blame the caller's independent CA file. Correct the
protected state before retrying; no raw exception, password or file contents are logged.
Prefer stable extra-CA inputs; recreate the
disposable or deliberately inspect/remove unused owned node pairs when changing
many configurations. Existing Node processes can cache roots and must be explicitly
restarted for renewal, even with a stable inherited environment path.

## Trust boundary and evidence

PFX plus password grants the sandbox application user/root the host's localhost
private identity. Do not snapshot a provisioned sandbox as a template. No material
is intentionally added to app sources, images, build data, publication or host trust
stores. Strings/process memory are not securely erasable. Ownership/link/mount
checks are not an authorization barrier against a malicious application user/root
editing its own files concurrently.

[The production-path verification record](verification/https/README.md) uses
synthetic export mocks, real disposable .NET 10 Kestrel activation and actual local
tool subprocesses. Separately, [the manual Deals preservation checks](verification/https/real-host-production-verification.md#preservation-and-still-missing-gates)
confirmed default host TLS (Swagger HEAD 200) without changing that manually
configured sandbox. Manual setup remains historical context, not proof of
production certificate provisioning. Production installs public sandbox trust
itself; the review-fixes tests use only disposable synthetic certificates.
A fresh verifier must follow
[the clean production-verification gate](verification/https/production-verification.md).
[The separate real-host production record](verification/https/real-host-production-verification.md)
does demonstrate the production flow on its exact recorded candidate SHA only;
it must not be relabelled as real-host verification of subsequent code changes.
Its historical outstanding-gates text is superseded by the scoped #46 closure
clarification above; the recorded candidate, manual/production distinction and
unauthorized-renewal boundary remain unchanged.
