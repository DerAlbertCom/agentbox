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
