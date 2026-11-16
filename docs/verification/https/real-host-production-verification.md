# Real-host production HTTPS verification — 2026-11-16

## Scope and acceptance boundary

This is **actual Windows-host production-flow evidence**, not the synthetic export
probe or the separately configured manual Deals sandbox. The user explicitly
authorized CLI-only export/use of the existing host ASP.NET development identity
and trust of its **public** leaf inside a new disposable sandbox. No host trust
installation, certificate creation/renewal, native private-key export fallback,
SDK/global-package installation or manual sandbox certificate configuration was
authorized or used.

**#46 remains incomplete.** Browser UI, actual Copilot shell-tool, actual Herdr
desktop, independent Linux host, real-host renewal and exact **18203** production
acceptance are separate gates. Passing the ordinary production path does not clear
the independent review findings on the older candidate.

## Latest PEM/bootstrap candidate: `5b40b570aff9fb76b9c6379e55569eef83dc2df9`

The verifier normally merged **exactly** `5b40b570aff9fb76b9c6379e55569eef83dc2df9`
from `feature/app-ports-46-pem-fixes` into its existing verification branch. This
was a fast-forward from evidence commit `8fa8e035570e3b88f2b54d3ccb66ceaab81ec519`;
all older results below remain historical, not relabelled. The new implementation
is **`fdd1916b1b538f84cd35795ff5b6dfd29b179df3`**. No integration branch/worktree
was written, no product edit was made, and no existing runtime was reused.

Fresh matching Release CLI/helper builds had **zero warnings/errors**. The
actual CLI reported `1.0.0+5b40b570aff9fb76b9c6379e55569eef83dc2df9`:

| Release artifact | SHA-256 |
|---|---|
| `abox.dll` | `F9212B501289380D188CB75C95665C539EFD0F52E784543E6EC6CEFCA3F1C5C8` |
| `ActivationProbe.dll` | `92731035D14DEEC17D68C7BAE904F2A67545960201A3272F1558449D59188F90` |

### Fresh actual real-host run — 13:59:01Z–14:01:03Z

```text
Workspace: D:\dev\rwe\agentbox-worktrees\app-ports-46-review-fixes\.abox\abox-issue46-activation-production-12d3c4b1
Sandbox:   abox-issue46-activation-production-12d3c4b1-abox-34A1B
```

This target was created by this candidate's Release `prepare-production` helper,
using the same cached approved template **685acd19ffe7** / sbx **0.51.0**. Host
**18443** was checked free, including a temporary loopback bind; the production
default **8443 → 18443**, offset **10000**, was used without freeing any listener.
Windows **10.0.26200**, PowerShell **7.6.10**, build SDK **11.0.100**, host target
runtime **10.0.15**, installed export tool SDK **10.0.404** / dev-certs **10.0.15**
were unchanged. Sandbox UID **1000**, SDKs **10.0.404 / 11.0.100** (default 11),
Python **3.13.18**, OpenSSL **3.5.5**, Node **v24.22.2** supplied the prerequisites;
nothing was installed.

Fresh preflight found no prior abox/manual HTTPS runtime/trust, canonical Kestrel
settings, host public leaf or localhost subject. The original system/Node input
again had **256** public certificates / **458886 bytes**, with Node and
`SSL_CERT_FILE` already selecting the canonical system bundle, not a test root.
Original local proxy/corporate CA sources were inventoried, not replaced.

Immediately before each full `ports`, fresh read-only installed dev-certs metadata
and public-store correlation required the same sole, date-valid, exportable,
format-6 **Full**-trust host identity with private-key association and localhost
SAN: SHA-1 **`8E84EA4F2F4EC8548DAF359CCB1179008251C714`**, full DER SHA-256
**`6A53FFA474740955141D2A0162D5D686A5B27851EF4FF62ED71F4EE465CE26FC`**.
Actual production `HostDevelopmentCertificate` CLI-only export, protected staging,
root/public-only trust and non-root activation supplied setup. No synthetic export,
manual PFX/configuration, profile environment or extra trust supplied this pass.

| Gate on exact `5b40b57` candidate | Result |
|---|---|
| Actual separate Release `abox ports --location ...` | **PASS**, exit 0 **before** the unchanged certificate-unaware fixture was started |
| Subsequent actual `abox run shell -- ... dotnet run --no-launch-profile --urls https://0.0.0.0:8443` | **PASS**, production file-backed exec bootstrap supplied defaults after setup exited |
| Host default curl TLS / exact marker at `https://localhost:18443/` | **PASS**, exact `abox-production-verifier` marker, no insecure flags |
| Host default chain/hostname and exact authorized leaf | **PASS**, callback-free `SslStream`, exact SHA-1 and complete DER SHA-256 |
| Fresh sandbox default system-store TLS at `https://localhost:8443/` | **PASS**, Python default context, `CERT_REQUIRED`, hostname checking, exact DER and marker through production `run shell` |
| Fresh Node default TLS through production `run shell` | **PASS**, **v24.22.2**, authorized socket, exact raw peer DER and marker; protected production Node bundle selected, no callback or test-only trust override |
| Original CA preservation | **PASS**, every original system/Node public root retained; protected source exactly matched the actual updated canonical bundle bytes; merged bundle **258** PEM entries, as explained in the older detailed record |
| Repeat actual full `ports` | **PASS**, separate exit 0, stable private path/password/config without exposing values or private hashes; same **7** runtime names, one active PFX and one current root-owned public trust source |
| Protected state | **PASS**, UID 1000 0700 runtime directory, ordinary single-link 0600 files; root-owned 0755 trust directory / ordinary single-link 0644 exact-leaf CRT |
| Export cleanup | **PASS**, no added product private-staging directory after either full setup; approved owner-only staging-parent ACL revalidated before execution |
| App/sandbox/workspace cleanup | **PASS**, verified PID/start-tick/argv-bound pidfd stop; helper fresh inventory confirmed exact sandbox absence and workspace removal; independently rechecked afterward |
| Host trust/key identity | **PASS**, unchanged CurrentUser/LocalMachine public DER and private-key-association inventories, including absent-store markers; exact eligible metadata rechecked after cleanup |
| Working manual Deals | **PASS preservation**, no sandbox exec/mutation or 18203 changes; callback-free exact-leaf TLS and Swagger default-TLS **HEAD 200** before and after |

There was **no failed production invocation or manual workaround** in this run.
As before, sandbox TLS clients produced only public success artifacts after their
assertions; empty captured TTY stdout was not treated as proof.

### Actual bootstrap-only production CLI refresh

After the successful full setup, app/TLS checks and repeat, the separate actual
production command was run against this genuine activation:

```powershell
dotnet $abox ports --location $workspace --refresh-https-bootstrap
```

**PASS, exit 0.** The success text explicitly said readiness was **not verified**;
no readiness was inferred from it. Before/after in-memory comparisons observed:

- Only `bootstrap.py` was atomically replaced: its inode changed and verified
  source hash remained the same. Every other runtime file, including encrypted
  PFX/config, retained its content and relevant inode/mtime/ctime/mode/ownership
  metadata. Private contents/hashes were not printed or preserved in evidence.
- The exact public trust source and canonical system bundle retained their hashes,
  inode/timestamps/modes/ownership; unrelated local sources and the actual
  `SbxClient` binding inventory were unchanged.
- A scoped `FileSystemWatcher` on the product's explicitly selected staging parent
  observed **zero** `abox-certificate-*` directory creation/deletion/rename events
  and **zero watcher errors**, with no before/after residue. Together with the
  unchanged activation, this found **no export/staging side effect**. This is
  bounded filesystem observation, not an OS-wide process/syscall trace; the
  composed automated tests separately assert absence of host export/root-trust/
  port-operation calls.
- Host callback-free exact-leaf TLS and a **fresh** default-TLS Node client passed
  afterward. This was an idempotent current-reader refresh, **not** a newly claimed
  live cross-version/corrupt-reader upgrade scenario; that remains separately
  covered by the guarded synthetic tests.

### Supplemental UTF-8 caller-CA check — not another full CLI export flow

Only inside this owned disposable, a public caller bundle was made by prepending
`# Überprüfung — öffentliche CA roots` plus newline to its already-existing public
canonical system bundle. No host CA or system trust file was edited. Via
`SbxClient.CertificateProbeExecAsync`, that file was selected **before** running
the exact embedded production exec shell and its hash-checked installed reader;
this verification-only seam was needed to supply the caller environment before
bootstrap, rather than overriding Node's variable afterward.

**PASS:** actual Node default TLS validated the authorized leaf and marker, using
the protected merged bundle produced by that reader. The `.source` bytes exactly
equalled the complete original UTF-8 input; merged bytes equalled that input,
newline and current public leaf, without reserialization. There were exactly
**9** protected runtime files / **2** Node variants, still one private PFX/config
and unchanged system public trust. This supplements the full production CLI
default-input run; it is not mislabelled as another full CLI setup or an agent UI.

### Fresh checks and remaining gates

- Release solution/helper builds: **zero warnings/errors**.
- Full direct Release xUnit suite: **1994 total, 1973 passed, 21 explicit skips,
  zero failures/errors**. The guarded composed-CLI Linux upgrade case is a Windows
  skip, not a claimed native pass.
- Guarded isolated receiver runner: **38 passed**, no skips/failures; actual mounted
  runtime/public-trust rejection also passed. Synthetic identities and fresh
  network-disabled/no-host-mount Docker containers remain separate from this
  real-host flow and from independent Linux-host sbx evidence.
- CSharpier: **246** CLI/helper files checked; `git diff --check` passed. No Debug
  artifacts were rebuilt. Only public Markdown evidence/procedure status is
  committed by this verifier; no product edits, private output or host settings.
- **Still missing:** production browser UI observation (no browser UI tool in the
  available tool catalog), actual Copilot shell-tool/no-inference authenticated
  seam, Herdr desktop connection (`abox` again absent from host PATH), independent
  Linux host, authorized real-host renewal and exact **18203** production fixture.
  The user's manual Deals browser observation is not this production instance.
  No billed prompt, authentication, user-session restart, global install, host
  renewal, push, issue/PR mutation or integration Git write was performed.

## Earlier recovery-fix candidate: `4f5719e09e8946a311c93e0cc77a20bbd20052bc`

After the pinned run had finished and its evidence was committed as `9ed3aa0`,
the verifier **normally merged** integration
`13a5f4024eca18e1fe8cafbfaed989cad2f61b2f` into its own branch. The resulting
tested merge SHA was **`4f5719e09e8946a311c93e0cc77a20bbd20052bc`**. Its `cli/`
tree is identical to that integration tip, including production recovery fix
**`cc41935596e96ba782120e6507b8ac7c7364cd73`**. No reset/discard, product edit,
host store patch or manual certificate setup was used. This new result, not the
older smoke alone, is the current production-flow evidence.

Fresh matching Release CLI and helper builds both had **zero warnings/errors**.
The actual CLI reported
`1.0.0+4f5719e09e8946a311c93e0cc77a20bbd20052bc`:

| Release artifact | SHA-256 |
|---|---|
| `abox.dll` | `C6777B3ED224A92E451C5C85B59C945761543FB51AB6F4421F352F3EE1D18174` |
| `ActivationProbe.dll` | `0D51CFF3F37B31E79A6777C623A5A3853330F4BE45993451AD7B5D63F8C5021B` |

### Fresh real-host production rerun

**2026-11-16T13:36:04Z–13:37:53Z**, in a **different freshly prepared target**:

```text
Workspace: D:\dev\rwe\agentbox-worktrees\app-ports-46-review-fixes\.abox\abox-issue46-activation-production-f26ae4db
Sandbox:   abox-issue46-activation-production-f26ae4db-abox-55F7F
```

The environment, certificate-unaware fixture, precise commands and trust/TLS
assertions described in the pinned record below were rerun, not inherited from
that result. Fresh `SbxClient` reads verified no prior runtime/manual trust or
host leaf. The initial system/Node input again had **256** certificates /
**458886 bytes**. The unchanged cached template, host/sandbox SDKs, actual UID,
Python/OpenSSL and Node versions are recorded below. Host **18443** was again
checked free; no existing listener was removed.

Fresh eligible metadata immediately preceded real production export, and the
same authorized **`8E84EA4F2F4EC8548DAF359CCB1179008251C714`** identity and full DER
SHA-256 were independently correlated. Export and activation were supplied only
by actual Release `abox ports`; the app started through actual Release `abox run
shell` **after the first `ports` process exited 0**.

| Gate on latest candidate | Result |
|---|---|
| Actual real-host production CLI export / public root trust / non-root activation / readiness / forwarding | **PASS**, separate `ports` process exit 0, no manual fallback |
| Certificate-unaware app starts after setup exits | **PASS**, canonical Kestrel defaults supplied by production file-backed exec bootstrap |
| Host default HTTPS marker at `https://localhost:18443/` | **PASS**, exact `abox-production-verifier` marker with `curl.exe --fail` |
| Host default chain/hostname and exact authorized leaf | **PASS**, callback-free `SslStream`; exact SHA-1 and complete DER SHA-256 |
| Fresh sandbox default system-store TLS at `https://localhost:8443/` | **PASS**, new Python default context through production `run shell`; hostname, exact DER and marker |
| Fresh Node default TLS at the same sandbox URL | **PASS**, **v24.22.2**, authorized socket, exact DER and marker through production `run shell`; no callback/test-only root/insecure flag |
| Preserve original Node/system trust | **PASS**, every original root retained; protected source equals the actual updated canonical system bundle bytes; merged bundle **258** PEM entries, as explained below |
| Repeat actual production `ports` | **PASS**, fresh eligible metadata, separate exit 0; stable path/password proven privately by byte-identical config, not exposed in output |
| Bounded/protected runtime and public trust | **PASS**, same **7** runtime files before/after repeat, UID 1000 0700 directory / ordinary single-link 0600 files, one PFX; one exact root-owned 0644 public CRT in its 0755 directory |
| Post-repeat existing app | **PASS**, strict host exact-leaf TLS retained; no renewal or universal hot-reload claim |
| Product host private staging | **PASS**, no added staging directory after each `ports`; private export removed |
| Exact task-owned cleanup | **PASS**, pidfd signal only after runner PID/start-tick/argv checks; helper's fresh inventory confirmed exact sandbox absence and workspace removal |
| Host public trust/key identity | **PASS**, unchanged before/after inventories and fresh exact eligible metadata after cleanup |
| Existing manual Deals setup | **PASS preservation**, no sandbox exec/mutation; callback-free exact-leaf TLS plus default-TLS Swagger HEAD **200** before and after |
| Browser, Copilot shell-tool, Herdr desktop, independent Linux host, real-host renewal, exact 18203 production fixture | **STILL MISSING**, prerequisites/boundaries in the table below were not waived |

There was **no failed production invocation in this latest-candidate rerun** and
no manual certificate/environment/trust workaround. This is a serial, ordinary
input real-host smoke, not an assertion that the F1/F2/F3 regression scenarios
were exercised live with this private identity or that independent review passed.

### Fresh automated verification on the latest candidate

- Release solution and activation-probe builds: **zero warnings/errors**.
- Full direct Release xUnit run: **1988 total, 1968 passed, 20 explicit native
  platform skips, zero failures/errors** (`-noLogo -noColor -maxThreads 4`).
- Guarded `RunReceiverChecks.ps1`: **34 passed**, no skips/failures; separate
  actual mounted-runtime/public-trust rejection checks passed. These use
  **synthetic identities** in new network-disabled, no-host-mount Docker
  containers and are **not independent Linux-host sbx evidence**. The live
  real-host results above do not rely on a synthetic exporter.
- Debug builds/tests were not rerun by this verifier, to avoid the independent
  reviewer's Debug artifacts. The recovery implementer's separately recorded
  Debug results remain in [recovery-fixes.md](recovery-fixes.md).
- Final CSharpier check covered **245** CLI/helper files; `git diff --check`
  passed. Only sanitized Markdown evidence/procedure status is committed by this
  verifier; no product source, private logs, PFX, passwords or environment dumps.

## Pinned candidate: `d600a117e47f08cb09ba5098a2d10bb9d525d3b8`

### Artifacts and environment

- Worktree `D:\dev\rwe\agentbox-worktrees\app-ports-46-review-fixes`, branch
  `feature/app-ports-46-review-fixes`; clean tracked state before execution.
- Actual Release CLI version:
  `1.0.0+d600a117e47f08cb09ba5098a2d10bb9d525d3b8`.
- Release `abox.dll` SHA-256:
  `4993B9EAB0ED418E5D6135B9C2BFD8F2596D045D089F1E24AE12A264B386FB63`.
- Release `ActivationProbe.dll` SHA-256:
  `C55B444B9A3FCC08961CA1C74BC570CB6E4BCE6076181DBB52CE0472831E2172`.
- Windows x64, OS version **10.0.26200**; PowerShell **7.6.10**.
  Default build SDK **11.0.100**, CLI/test target **net10.0**, host runtime
  **10.0.15**. Installed compatible metadata/export tool: SDK **10.0.404**,
  dev-certs **10.0.15-servicing.26529.108**. No SDK selection/install workaround.
- `SbxClient.GetVersionAsync`: **0.51.0**. Cached approved template
  `docker.io/library/cao-docker-ai-sandbox:latest`, inventory ID **685acd19ffe7**;
  helper uses `--pull never`, no state/credential copies and no static MCP servers.
- Sandbox default user **UID 1000**; installed SDKs **10.0.404 / 11.0.100**,
  default **11.0.100**; Python **3.13.18**, OpenSSL **3.5.5**, Node **v24.22.2**.

### Successful fresh run

**2026-11-16T13:31:42Z–13:33:29Z**:

```text
Workspace: D:\dev\rwe\agentbox-worktrees\app-ports-46-review-fixes\.abox\abox-issue46-activation-production-58ca11ee
Sandbox:   abox-issue46-activation-production-58ca11ee-abox-F18BB
```

Created only by the documented Release helper's `prepare-production` mode. Host
**18443** was checked free, including a temporary loopback bind; **8443 → 18443**
used the actual command's default offset **10000**. No binding was removed to make
room. The fixture was the helper's unchanged, certificate-unaware Web SDK app:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/", () => "abox-production-verifier");
app.Run();
```

The fresh target had no prior abox `/tmp` runtime, abox trust directory, manual
Deals runtime or canonical Kestrel path/password settings. Its system bundle
contained **256** public certificates: no authorized host DER and no localhost
subject. Original `NODE_EXTRA_CA_CERTS` and `SSL_CERT_FILE` already named the
canonical **`/etc/ssl/certs/ca-certificates.crt`**, not a verifier-supplied root
bundle; those settings were not changed. The Node input contained the same
**256** certificates / **458886 bytes**. Existing local proxy/corporate CA sources
were inventoried and preserved. No existing manual runtime or trust supplied the
success condition.

Immediately before actual export, installed dev-certs' read-only
`https --check-trust-machine-readable` returned exactly one date-valid,
exportable, **Full**-trust `CN=localhost` development identity, format **6**, with
localhost DNS SAN. A separate read-only `CurrentUser/My` check correlated the
sole development identity and private-key association with the known public DER:

| Public identity | Value |
|---|---|
| SHA-1 thumbprint | `8E84EA4F2F4EC8548DAF359CCB1179008251C714` |
| Complete certificate DER SHA-256 | `6A53FFA474740955141D2A0162D5D686A5B27851EF4FF62ED71F4EE465CE26FC` |
| UTC validity | `2026-01-14T14:47:39Z`–`2027-01-14T14:47:39Z` |

The actual Release production `dotnet <abox.dll> ports --location <workspace>`
ran as a **separate process and exited 0 before the app was started**. Its genuine
`HostDevelopmentCertificate` discovery, CLI-only export, protected staging,
root/public-only trust receiver, non-root activation, readiness and forwarding
supplied the setup; no synthetic exporter or manual receiver/configuration was
used. Child-only temp settings selected the previously approved owner-only
staging parent. The product created/removed its own narrow random staging child;
before/after inventory found no additional `abox-certificate-*` directory.

The app was subsequently launched by actual production
`abox run shell --location <workspace> -- sh -c '... exec dotnet run --project
"$WORKSPACE_DIR/Verifier.csproj" --no-launch-profile --urls https://0.0.0.0:8443'`.
The shell recorded only its public PID before `exec`; it supplied **no certificate
path/password/environment**. Canonical configuration came from production exec
bootstrap after the setup process had gone.

| Gate on this pinned candidate | Result |
|---|---|
| Real production CLI export / trust / activation / forwarding | **PASS**, separate `ports` exit 0 |
| Host default HTTPS `https://localhost:18443/` | **PASS**, `curl.exe --fail` returned exact `abox-production-verifier` marker |
| Host default chain/hostname and exact leaf | **PASS**, callback-free `SslStream.AuthenticateAsClientAsync("localhost")`; both public fingerprints matched |
| Fresh sandbox system-store TLS through `abox run shell -- ...` | **PASS**, Python `ssl.create_default_context()`, `CERT_REQUIRED`, hostname check; exact DER SHA-256 and marker |
| Fresh Node default TLS through `abox run shell -- ...` | **PASS**, Node **v24.22.2** `https.get`, authorized socket, exact raw peer DER SHA-256 and marker; no TLS bypass/custom callback |
| Original Node/system roots | **PASS**, all original certificate DER hashes retained; protected `.source` bytes exactly matched the canonical system bundle after production's public trust update |
| Repeat actual production `ports` | **PASS**, another process exit 0 after fresh eligible metadata; byte-identical private config proves stable path/password without printing either password or its hash |
| Bounded runtime/trust after repeat | **PASS**, identical **7** runtime names, one active PFX, one exact root-owned public trust source; no obsolete copies/transfer files |
| Existing verifier app after repeat | **PASS**, host callback-free exact-leaf TLS still valid; no restart/hot-renewal claim |
| Cleanup | **PASS**, exact app runner signaled through pidfd only after PID/start-tick/argv checks; helper freshly confirmed sandbox absent and workspace removed |
| Host trust/key identity and existing Deals | **PASS**, details below |

Runtime `/tmp/abox-https-1000` was owner **1000**, **0700**; all seven entries were
ordinary, single-link, owner **1000**, **0600** files: config, active PFX, lock,
bootstrap, public PEM and one Node source/bundle pair. Public system trust was one
root-owned **0755** narrow directory with its private lock and one ordinary,
single-link, root-owned **0644** exact-leaf CRT. No secret file contents were
printed. Node's canonical system input becomes **257** certificates when the
public host leaf is installed; its merged protected bundle contains **258** PEM
certificates (the same current leaf also appended by the production Node merge),
not a replaced/discarded set of original roots. No renewed or obsolete leaf was
introduced.

Because the shell launcher uses a TTY, captured host stdout was not treated as a
reliable structured-result transport. The sandbox clients wrote **public success
JSON only** after their TLS/marker assertions, and the verifier required both
production command exit 0 and those fresh helper-workspace artifacts. No PFX,
password, config contents, environment dump, private log or business response was
saved in evidence.

### Earlier attempts and harness limitations

No failed/partial attempt is retrospectively called a complete pass:

- Three pre-export attempts were cleaned after an overstrict verifier preflight
  rejected existing canonical `SSL_CERT_FILE` or assumed the default SDK had to be
  10. The documented prerequisite is an **installed** .NET 10 SDK. Read-only
  inspection established canonical system-store selection and installed
  **10.0.404**, alongside default 11.0.100. Only verifier assertions changed; no
  sandbox environment, trust, packages or SDK settings were changed.
- **13:26:15Z–13:27:10Z**, target `...production-9bd76f2b-abox-88CDC`: actual
  `ports` exited 0, staging cleaned, then a verifier audit incorrectly expected
  the original canonical system bundle's whole-file hash not to change when the
  product installed its public leaf. No app started. Subsequent assertions instead
  required preservation of every original public root plus byte-preservation of
  the actual updated Node input. No product/store workaround was used.
- **13:28:50Z–13:30:18Z**, target `...production-54d438c1-abox-F03C2`: production
  setup and default host marker/exact-leaf TLS passed. The host verifier then threw
  `PropertyNotFoundException` while consuming empty captured TTY stdout as JSON;
  this was not a demonstrated TLS failure. That app was stopped by verified pidfd,
  and its sandbox/workspace removed. The final run used the public success artifacts
  described above rather than assuming anything from empty stdout/exit status.
- Separate clean-template read-only audits also used helper-owned fresh sandboxes
  and confirmed exact cleanup. They exported no certificate and changed no trust.

Every production attempt checked unchanged host public store inventory, sole
identity/private-key association, no added staging child and the live Deals URL
in its final cleanup path. No product edit was made to force this run to pass.

## Preservation and still-missing gates

Before/after public DER plus private-key-association inventories were compared for
existing **CurrentUser and LocalMachine My, Root, CA, AuthRoot, TrustedPeople,
TrustedPublisher and Disallowed** stores, recording absent stores as absent.
They were unchanged. Fresh eligible dev-certs metadata and the sole known host
identity were rechecked after cleanup. No host `--trust`, `--clean`, `--import`,
native private-key export, certificate creation or renewal was run. The ordinary
dev-certs export race documented in the product remains a limitation; no absolute
concurrent-store-mutation guarantee is asserted.

The separately configured `rwe-ai-sbx-41bc30` was **never exec'd into, stopped,
removed or reconfigured**. Its app/runner **55039 / 54711**, relay **60719**, host
mapping **18203**, manual runtime/trust and root `run-abox.ps1` were not touched.
Before and after every run, callback-free exact-leaf host TLS and a default-TLS
Swagger **HEAD 200** confirmed its working URL. No business request/authentication
was sent. The user's earlier browser close/reopen confirmation concerned **that
manual Deals URL**, not this disposable production instance.

| Remaining gate | Status / missing prerequisite |
|---|---|
| Production host browser open/reload | **NOT OBSERVED**: no browser/desktop UI automation tool available; CLI TLS is not browser evidence |
| Actual Copilot shell-tool | **NOT RUN**: installed binary/help is not proof; no authenticated no-inference tool seam established, no authorization to introduce credentials or send a billed/model prompt |
| Actual host Herdr desktop connection | **NOT RUN**: desktop `status` was readable, exit 0 / zero reported errors, but **`abox` is not on host PATH**, a production launcher requirement; no global installation/PATH change or existing desktop/core restart was attempted |
| Independent Linux **host** | **UNAVAILABLE**: no configured independent host/tool access; Windows-host Linux sandbox is not that evidence; no guessed SSH credentials |
| Exact host 18203 production fixture | **NOT RUN**: preserved the working Deals listener; 18443 is not claimed to satisfy the exact-port criterion |
| Real-host certificate renewal | **NOT RUN / NOT AUTHORIZED**: repeat of the same identity is not renewal proof; synthetic renewal remains separate |
| Debug live production flow | **NOT RUN**: Release used to avoid the independent read-only reviewer's Debug artifacts |
| Review acceptance on `d600a11` | **NOT CLEARED**: F1 Node bad-input recovery, F2 unreadable/link project handling, F3 lock contention findings remain distinct from this serial/default-path smoke |

## Pinned-candidate automated checks

Fresh Release solution and activation-probe builds: **zero warnings/errors**.
Direct Release xUnit executable run: **1981 total, 1964 passed, 17 explicit native
platform skips, zero failures/errors**. The previously recorded Debug and receiver
checks were not relabelled as fresh runs here. Formatter/diff checks and subsequent
integration verification are recorded with the final candidate below.
