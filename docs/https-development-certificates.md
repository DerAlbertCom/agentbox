# Development HTTPS: staging is not activation

**#46 is not complete. `abox ports` does not export a host certificate or configure
Kestrel yet.** Forwarding a TLS port does not establish host trust. A new sandbox
certificate, generic `CurrentUser/My` import, an insecure client or a shell-only
profile is not a substitute for serving the exact existing trusted host identity.

## Implemented groundwork

`ProtectedTemporaryDirectory` establishes an owner-only directory **before** an
exporter could write: a protected, inheritable current-user-only DACL on Windows,
0700 on Unix. Linked ancestors are rejected. Scoped disposal removes partial
exports after success, exceptions or cancellation, and reports cleanup failures
without replacing the primary error or exposing raw child diagnostics. It does
not itself export a certificate or validate a host certificate's trust.

`SbxClient.StageDevelopmentCertificateAsync` accepts an **already authorized and
validated** PFX/password. The caller must establish the actual exported identity,
private-key usability, dates, localhost SAN and current host trust first. This
technical adapter deliberately does not infer trust from arbitrary PFX bytes.

- Every runtime operation uses `SbxClient` argv, with the actual default exec user,
  not root. Linux and Python 3.8+ are prerequisites; nothing is installed.
- PFX and password travel only through sensitive, closed-after-write UTF-8 stdin.
  Framing checks a version marker, bounded byte length and SHA-256 digest; the
  receiver additionally validates JSON/base64 structure. The checksum detects
  damaged framing, **not** a malicious transport peer.
- The destination is `/tmp/abox-https-<effective uid>/active.json`: a 0700 owned
  directory, with 0600 ordinary single-link owned files. Root execution, symlinks,
  wrong ownership, permissive permissions and unexpected entries fail closed.
  Ancestor/descendant mounts other than the container root filesystem are
  rejected using `/proc/self/mountinfo`. A mounted/persistent `/tmp` is unsupported;
  there is no fallback into HOME, APPDATA, a workspace or a build directory.
- PFX bytes are unchanged base64 in a versioned private bundle together with their
  password. One same-directory atomic rename activates **the whole bundle**;
  concurrent readers cannot see a new PFX paired with an old password. The bundle
  is **not a PFX path Kestrel can consume directly**, and is not launch configuration.
- An owned 0600 lock prevents simultaneous writes; contention fails for retry,
  rather than hanging. A later successful writer removes only validated abandoned
  `.transfer-<id>.tmp` files while holding the lock. Refresh keeps one bundle and
  one lock, not a series of obsolete certificate generations.
- Failed/canceled local calls attempt separate bounded cleanup of their exact
  transfer ID. Cleanup uncertainty is reported. Lost acknowledgements may mean
  the whole new bundle committed: there is no rollback or secure-erasure claim.

No command, launcher, create/replace flow, build or publication path invokes these
helpers yet. A successful staging call must not be reported as “HTTPS configured.”
Future shared HTTPS orchestration must stay outside command slices, and only be
connected after the gates below are resolved; #47 lifecycle work is not included.

## Prerequisites and unresolved decisions

1. **Existing-only host export.** The contract is the host's existing trusted ASP.NET
   Core certificate, not a newly generated alternative. The inspected `dotnet
   dev-certs` export path can create a certificate if no current-format candidate
   qualifies, and has no identity selector or existing-only switch. Pre/post
   checks can reject the resulting identity but cannot prevent host creation during
   a renewal/deletion race. A decision is needed on an exact, read-only-store export
   mechanism versus the ticket's requested CLI-export mechanism; this work has not
   assumed approval for that change. No production exporter is implemented.
2. **Trust correlation.** `--check --trust` succeeding does not prove that the
   exported certificate is the trusted candidate. Validate fresh machine-readable
   metadata against the actual loaded PFX before transport, including multiple
   candidates, changed identities and empty/malformed successful output. This is
   not implemented by the staging adapter. Missing/unusable/untrusted certificates
   need user-operated recovery, never automatic host `--trust`, `--clean`, `--import`
   or creation. `dotnet dev-certs https --check --trust` is a read-only prerequisite
   check, not export-identity or browser-validation evidence.
3. **Persistent exact selection across launch paths.** Canonical Kestrel default
   Path/Password configuration remains preferred. `/etc/sandbox-persistent.sh`
   through BASH_ENV misses direct argv and noninteractive `sh`. A shared exec
   bootstrap would cover abox shell/direct argv/exec/pipe, but a new transport's
   environment cannot initialize an already-running Herdr core or agent parent.
   A supported explicit fresh-parent procedure plus stale-parent detection and
   real tool-path evidence is still required. No bootstrap, profile, core or
   launcher modification has been made; there is no approved shell-only acceptance.

CLI PFX export's `--password` would unavoidably expose its generated password to
process-argument inspection for the export process's lifetime. Sensitive output
policies do not eliminate that exposure. The staging adapter adds **no** password
arguments or environment settings. Its transient .NET strings and Python memory
are not securely erasable; scoping/cleanup is not a memory-forensics guarantee.

## Restart, recovery and trust boundary

Staging alone changes no app's certificate. Even after an activation mechanism is
implemented, assume running applications must restart to adopt a renewed identity;
new settings may additionally require fresh shells, agent parents and a correctly
initialized Herdr core. An ordinary core restart may preserve the old environment.
No automatic restart or disruption of existing applications is authorized here.
Explicit application endpoint certificates/custom TLS callbacks may override the
default; there is no universal forced-TLS configuration promise.

For staging failures, correct ownership/type/mode/mount issues and retry. Preserve
unrelated entries; do not recursively delete an unfamiliar runtime directory. If
cleanup is unconfirmed, identify the task-owned transfer under the protected runtime
directory before removing its `.transfer-<id>.tmp`; a normal successful retry also
cleans validated abandoned transfers. After an abnormal host export failure, inspect
the task-owned `abox-certificate-*` directory identified by its caller and remove
remaining private files. Crash/power loss cleanup is best effort, not guaranteed.

Giving a sandbox PFX plus password grants its application user and root access to
the host's localhost private identity. It is not a general CA trust grant. Do not
snapshot a provisioned sandbox as a template: manual snapshots can capture runtime
secrets. The adapter does not install a certificate or trust anchor in either host
or sandbox certificate stores. Host Windows, Linux system/curl and browser trust
are separate verification surfaces.

## Evidence scope

[The task status and reproduction record](agents/story-3-transfer-status.md) separate
synthetic transport verification from host certificate export/use and launch-path
verification. No Windows host browser success, real Deals host-certificate identity,
independent Linux-host run or complete #46 acceptance is claimed by this groundwork.
