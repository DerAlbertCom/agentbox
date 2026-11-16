# Story #3: Deals External API live baseline

Use this baseline before arranging the Deals HTTPS verification for
[#46](https://github.com/DerAlbertCom/agentbox/issues/46). It is separate from
[#43's synthetic publishing verification](../verification/application-ports.md).

## Current result — reviewed relay applied, 2026-11-16 12:01 CET

**Host TCP/TLS now reaches the unchanged Deals External API and receives its
actual sandbox leaf certificate. Trusted HTTPS still fails, as expected: host
curl returned 60 / HTTP 000; strict `SslStream` rejected `UntrustedRoot`.** No
certificate-validated HTTP response or Story #3/#46 completion is claimed.

This follow-up used the user-authorized existing sandbox and API, with #43's
reviewed relay implementation present at merge **`fb4658a`** (verification HEAD
`c9ceeb1`). It did not restart the API, agents or sandbox, change application
bindings, or resume the separately blocked host-certificate copy.

### Narrow production-adapter upgrade and repeat

- Rechecked exact selected identity **`rwe-ai-sbx-41bc30`**, already running;
  sbx client **0.51.0**. No broad `abox ports` discovery against `D:/dev/rwe` or
  other project was run.
- The evidence helper in `docs/agents/story-3-deals-relay-probe/` references
  the production Abox project. Every sbx invocation goes through public
  `SbxClient` methods with argv lists; the helper adds no sbx entry point or
  alternate relay implementation. Its observing `IProcessRunner` records runtime
  inventories and publish/unpublish counts without changing requests.
- Called production **`EnsureApplicationPortsAsync("rwe-ai-sbx-41bc30",
  [new(8203, 18203)], token)`**, with exactly this single requested mapping.
  Initial inventory contained only **`127.0.0.1:18203 -> 8203/tcp4`**. The
  adapter made the relay ready, removed only that exact full native spec, confirmed
  an empty inventory, published **`127.0.0.1:18203 -> 60719/tcp4`**, then confirmed
  the new inventory. There was exactly one unpublish and one publish call.
- Called the same production method a second time with the same single mapping.
  It returned successfully with identical inventory and **no further publish or
  unpublish**. No unrelated mappings existed; before/after comparison confirmed
  no unrelated changes or additional mappings. This is not a live test with a
  nonempty unrelated inventory; #43's disposable probes cover that case. No extra
  mapping was created in this user sandbox just to test preservation.
- Host listener inspection, including after the setup helper exited, found only
  **`127.0.0.1:18203`**, still runtime-owned PID **66712**, with no wildcard or
  IPv6 listener on that host port.

### Unchanged application and retained runtime state

Before and after the upgrade, `/proc` checks confirmed the same `dotnet run`
PID/process group **43130**, start ticks **1378797**, and apphost PID **43843**,
start ticks **1390583**. The apphost still owned exactly **127.0.0.1:8203 and
[::1]:8203** listeners, not wildcard listeners. No application log, effective
configuration or process environment was read for this follow-up.

The production sandbox relay is PID **46234**, start ticks **1509747**, forwarding
**60719 -> application 8203**. A separate helper process after setup exited
confirmed the same relay PID/start ticks through its control socket. Production
state at `/home/agent/.abox-ports-v1` was owned by the application user with mode
**0700**; `lock`, `8203.port` and `8203.sock` were owned, non-link entries with mode
**0600**, and the saved port agreed with control status. The API, relay and exact
host mapping are intentionally **left running**. Do not remove relay state while
the retained host mapping targets 60719; do not stop the runtime-owned host listener
as application cleanup. Existing task-owned application cleanup records below
remain valid, but no cleanup or stop is authorized/performed by this verification.

### Strict HTTPS retest after setup exited

The separate `--check` helper process ran at **2026-11-16T11:01:38Z**. Requests
remained bounded `HEAD /`, without credentials, redirects, response bodies,
alternate trust bundles or certificate-validation bypasses, using the curl form
already recorded below.

| Client / URL | Fresh result |
|---|---|
| Sandbox `https://localhost:8203/` | curl **60**, HTTP **000**: certificate validation failed |
| Windows host `https://localhost:18203/` | curl **60**, HTTP **000**: certificate validation failed, replacing the historical native-mapping TLS transport failure |

An independent host `TcpClient` connected to `localhost:18203`. Its `SslStream`
used `TargetHost = "localhost"` and the platform's normal validation, with a
diagnostic callback that observed public leaf metadata but returned **only
`errors == SslPolicyErrors.None`**. It did not accept the invalid certificate or
send an HTTP request over rejected TLS. Observed result:

- TCP connected; certificate-validation callback reached.
- Actual served leaf **CN=localhost**, SHA-1 thumbprint
  **`CA669D34D0FA19407DFD5788F3A639AA759A6953`**, exactly the previously observed
  sandbox identity, **not** host certificate `8E84EA4F2F4EC8548DAF359CCB1179008251C714`.
- Validation **`RemoteCertificateChainErrors`**, chain status **`UntrustedRoot`**;
  `AuthenticateAsClientAsync` failed with **`AuthenticationException`** and
  `tls_authenticated = false`.

This establishes post-helper-exit relay reachability and public server identity,
not a completed trusted TLS handshake, successful HTTPS, browser trust or
dependency health. The certificate bytes were observed only in memory; no
certificate/key was exported, copied or written to disk.

### Scope, remaining blocker and verification commands

This evidence commit contains this note and the helper's `Program.cs` and
`RelayProbe.csproj`; generated build outputs remain ignored. During the recorded
live verification, the helper Debug build passed with **zero warnings/errors**;
`--apply` and the separate post-exit `--check` both exited **0**. Recorded commands
(not rerun for documentation housekeeping):

```text
dotnet build docs/agents/story-3-deals-relay-probe/RelayProbe.csproj -c Debug
dotnet run --project docs/agents/story-3-deals-relay-probe/RelayProbe.csproj -c Debug --no-restore -- --apply
dotnet run --project docs/agents/story-3-deals-relay-probe/RelayProbe.csproj -c Debug --no-build -- --check
```

`--apply` is a one-shot native-upgrade verifier that requires the original native
mapping; do not rerun it now merely to inspect state. Repeat safety was exercised
by its two calls to the production adapter. `--check` is read-only apart from
normal TLS connection attempts. No full solution tests/builds were requested for
this live verification. `dotnet csharpier check
docs/agents/story-3-deals-relay-probe` and `git diff --check` both passed.

The remaining host-certificate transport/use gate is **still BLOCKED by
`external_directory`** pending the user's explicit access grant for the protected
staging directory in [the host-copy note](story-3-host-certificate-verification.md).
That denial was not bypassed. No certificate-store/trust change, private export,
app-source/config/environment change, application/core/agent restart, sandbox
recreation, runtime/global-settings change, dependency-service change, or business
endpoint request was performed. No push or issue mutation was performed; Git
publication and issue coordination remain with the coordinator.

## Historical authorized startup — 2026-11-16 11:42 CET

**The following startup evidence predates the relay upgrade above.** Its native
mapping, host TLS transport failure and remaining #43 gate describe that earlier
state, not the current mapping or a continuing relay blocker.

**The real Deals External API is running in the existing sandbox. Neither
sandbox-local nor host HTTPS passed certificate-validated HTTP verification.**

The user's explicit follow-up, **“yes, just start it and test,”** supersedes the
earlier startup gate below. This authorizes normal development-service connections
and startup effects, including configured SQS, SSM and telemetry. It does not
establish that startup is write-free or that every dependency is healthy.

### Startup and task ownership

- Rechecked exact sandbox `rwe-ai-sbx-41bc30`: running, no External API process,
  no listener on sandbox 8203/18203 and no existing mappings before this launch.
  The three Deals instruction/domain-pointer files matched the host copies by
  SHA-256; those instructions were read before launch.
- At `2026-11-16T10:39:39Z`, launched the following argv from
  `/home/agent/workspace/Deals/backend`, with normal inherited configuration and
  the unchanged launch profile; normal build/restore was allowed to run:

  ```text
  dotnet run --project /home/agent/workspace/Deals/backend/src/WebApi.External/WebApi.External.csproj --launch-profile Web
  ```

- Private log signals confirmed `Application started.` and
  `Now listening on: https://localhost:8203`. `/proc/net/tcp{,6}` independently
  confirmed **127.0.0.1:8203 and [::1]:8203**, not a wildcard bind. No compiler
  error codes or exception type names were found in the captured launch log at
  the final check. This is not exhaustive dependency-health evidence.
- Task supervisor PID **43129**, `dotnet run` PID/process group **43130**,
  apphost PID **43843**. Recorded `/proc` start ticks: **1378797** for 43130,
  **1390583** for 43843. **API intentionally left running** for subsequent
  integration verification; no unrelated process was stopped or restarted.
- Task files are outside app sources, under the application user's private
  **0700** directory
  `/home/agent/.local/state/abox-story3-live-20261116T103939Z`:
  `app.log`, `supervisor.log`, `app.pid`, `supervisor.pid`, `ownership.json`, and
  `exit.json` if the launch exits. Created files are **0600**. Logs remain private;
  diagnostics emitted only allowlisted status/public metadata. For later cleanup,
  verify the recorded PID start ticks and process-group membership before
  signaling **only task-owned process group 43130**. The supervisor records exit
  and then terminates; preserve unrelated processes and mappings.

### Exactly one targeted mapping

The current `abox ports` interface has only a location selector, not a
project/port filter. Running it against `D:/dev/rwe` would discover unrelated
applications, so that broad command was not run. After an empty runtime inventory
and a free host-port check, used only the supported argv sequence from
`SbxClient.ApplicationPorts.cs` for this single authorized mapping:

```text
sbx exec rwe-ai-sbx-41bc30 true
sbx ports rwe-ai-sbx-41bc30 --json
sbx ports rwe-ai-sbx-41bc30 --publish 127.0.0.1:18203:8203/tcp4 --json
sbx ports rwe-ai-sbx-41bc30 --json
```

Fresh inventory confirmed exactly **127.0.0.1:18203 -> 8203/tcp4**. Windows listener
inspection confirmed only `127.0.0.1:18203`, with runtime-owned PID 66712. This is
the task's only mapping mutation and is retained for follow-up verification.
No relay/fallback was installed in this real sandbox; #43's implementer is working
independently in a disposable sandbox. Do not stop the runtime-owned host listener
as app cleanup; any later removal must target only this exact mapping.

### Validated HTTPS checks and public served identity

Repeated bounded `HEAD /` checks used the request form below, no credentials,
redirects, response bodies, alternate CA bundle, curlrc or validation bypass:

```text
curl -q --noproxy '*' --proto '=https' --connect-timeout 3 --max-time 5 --head --silent --output <NUL-or-/dev/null> --write-out '%{http_code}' <URL>
```

| Client / URL | Observed result around 11:42 CET |
|---|---|
| Sandbox `https://localhost:8203/` | curl **60**, HTTP **000**: certificate validation failed; TLS server reached, no validated HTTP response |
| Windows host `https://localhost:18203/` | curl **35**, HTTP **000**: TLS connection failed, no validated HTTP response |

Independent sandbox `ssl.create_default_context()` with
`server_hostname='localhost'` failed verification with code **18**, self-signed
certificate. A dedicated public-certificate diagnostic also retained validation:

```text
openssl s_client -connect localhost:8203 -servername localhost -showcerts -verify_return_error -verify_hostname localhost
```

With empty stdin and privately captured output, it exited **1**, reporting
self-signed verification error **18**. Its returned public certificate was parsed
in memory with `openssl x509 -noout` for identity only; no certificate file or key
was exported/copied. Served certificate:

- Subject/issuer **CN=localhost**; SHA-1 thumbprint
  **CA669D34D0FA19407DFD5788F3A639AA759A6953** (the sandbox certificate previously
  inventoried below, not the host certificate).
- Valid **2026-11-12T07:15:51Z–2027-11-12T07:15:51Z**; SAN includes
  **DNS:localhost**, **127.0.0.1**, **::1** and development hostnames.
- The SAN/validity metadata is not a successful chain/hostname-validation test;
  the strict handshake failed trust validation.

On Windows, an independent `TcpClient` connected to `localhost:18203`, followed
by a default-validation `SslStream.AuthenticateAsClientAsync('localhost')`.
TCP connected; TLS failed with `IOException` / `SocketException`, with **no peer
certificate available**. Thus host failure is consistent with the previously
proven native publishing limitation for sandbox-loopback-only listeners, not
evidence of host certificate rejection or host trust success.

### Remaining gates and verification scope

The startup gate is cleared and actual listening/TLS identity is now observed.
Host reachability still needs #43's loopback-compatible fallback. HTTPS #46 still
needs its separately approved secure host-certificate transport/runtime setup:
no certificate export/copy, trust change, persistent environment/bootstrap change,
app bind override, app-source/config edit, dependency-service change or sandbox
lifecycle operation was performed here. Deals tracked-change count was **0** after
startup. The worker and business endpoints were not started/called.

Only this note changes in the integration repository. No implementation tests
were rerun for this live-verification/documentation task; the normal API launch
built sufficiently to start. Git whitespace checking applies to the note. No
browser trust, independent Linux-host verification, HTTPS provisioning success,
or Story #3/#46 completion is claimed. Both issues remain open; their full bodies
and empty comment arrays were reread.

## Historical baseline — before explicit startup approval

**Everything below describes the earlier 11:30 CET baseline**, not current
process/mapping state or a continuing request for startup permission. Its startup
gate and next-step instructions are superseded by the authorized live run above.

### Earlier result and scope

**2026-11-16, Windows host: no live Deals HTTP or HTTPS success.** All six
bounded requests below returned curl exit **7** (connection failed), HTTP status
**000**. Certificate and hostname validation remained enabled, but no TLS
handshake occurred: these request results prove neither certificate availability
nor trust. Store-only public metadata is recorded below; no served-certificate
metadata was obtained.

The user authorized using the existing sandbox and conditionally starting the
External API if startup was safe. The user also confirmed it was not running.
**The API was not started:** static inspection did not establish startup as free
of external writes. No task-owned app process exists to clean up or restart.

Only this note is added. No app-source/config edits, app-secret reads/copies,
certificate export/copy, trust changes, persistent environment/bootstrap changes,
publishing/unpublishing, sandbox lifecycle operations, or app/core/agent restarts
were performed. No raw application logs were read. Diagnostics emitted only
allowlisted metadata/status, not configuration values, process arguments,
environment contents or credentials. No build, restore, app execution or tests
were needed for this documentation-only baseline.

The integration branch was clean before this work. Git writes were withheld
during the merge; the user subsequently authorized committing only this note
after the clean integration merge at `03e43e3`. The #43 note became available
during the work and was read after the merge arrived. Its publishing evidence
does not resolve the localhost-only application gate; **#43 remains open** while
a separate implementer investigates a durable relay/fallback.

## Live observed identity and state

The shared resolver's exact naming rules were applied to `D:/dev/rwe`, followed
by read-only `sbx ls --json` inventory. No fuzzy name or workspace search selected
the sandbox. See `cli/src/Abox/Sandboxes/SandboxResolver.cs:11-18` and
`cli/src/Abox/Naming/{SandboxName,LegacySandboxName}.cs`.

| Item | Observed value |
|---|---|
| Host location / resolved historical real path | `D:\dev\rwe` / `D:\dev\rwe` |
| Current candidate | `rwe-abox-3C02C`, absent |
| Exact legacy candidate / selected actual identity | `rwe-ai-sbx-41bc30`, running |
| sbx client and server | `v0.51.0`, revision `7d55286de0c775148b985ed349f760f1eba06203`, server API `0.49.0` |
| Actual sandbox project | `/home/agent/workspace/Deals/backend/src/WebApi.External/WebApi.External.csproj`, exists |
| Existing mappings, `sbx ports rwe-ai-sbx-41bc30 --json` | Empty |
| Host TCP listeners on 8203 / 18203 | None / none |
| Sandbox TCP listeners on 8203 / 18203 | None / none |
| External API process, host / sandbox | None / none |

Process checks privately matched the External API executable/assembly/project
names, emitting only counts. Sandbox listener checks read `/proc/net/tcp{,6}`;
no process arguments/environment were emitted. Sandbox `curl`, `openssl` and
`dotnet` are available; `ss` is not. Installed sandbox SDKs are `10.0.404` and
`11.0.100`. Tool/SDK availability is not dependency readiness. Host curl is
`8.21.0`, using Schannel.

## Project/profile and startup safety — static evidence

Deals instructions read: root `AGENTS.md`, `docs/agents/domain.md` and
`CONTEXT-MAP.md`; the listed domain ADRs do not concern application startup.
Paths below are relative to `D:/dev/rwe/Deals` (sandbox `~/workspace/Deals`).

- Project: `backend/src/WebApi.External/WebApi.External.csproj`, assembly
  `Cape.Deals.WebApi.External`; target `net10.0` is inherited from
  `backend/Directory.Build.props:3`.
- `backend/src/WebApi.External/Properties/launchSettings.json`: the `Web`
  project profile declares **HTTPS localhost:8203 only**, with no HTTP URL.
  Independently parsed host and sandbox launch-port metadata agree. This is
  configured intent, not an observed running bind or certificate selection.
- `backend/src/WebApi.External/Program.cs:56-80,95-115`: startup builds the web
  host, calls `UseCsConfigurationFromAmazonSsm`, registers Deals MassTransit,
  shared/feature services, initializes telemetry, then runs the app.
- `backend/src/Infrastructure/Shared/MassTransit/MassTransitApplicationSetup.cs:11-23`:
  SQS diagnostics and Amazon SQS transport registration are not guarded by
  `IsDevelopment`; the transport skip is for unit tests. Whether the effective
  transport configuration performs queue/topology writes at startup was **not
  established**. Registration alone is not proof that such a write occurred.
- `backend/src/Infrastructure/Shared/OpenTelemetry/OpenTelemetrySetup.cs:20-40`:
  OTLP exporters are registered outside unit tests. Startup and requests can
  therefore have configured external telemetry effects; destinations were not
  inspected or contacted.
- `Program.cs:177-185,195-207`: non-development data-protection persistence uses
  AWS Systems Manager; shared registration includes Oracle contexts and S3.
  `backend/src/Infrastructure/Shared/Database/DatabaseServiceExtensions.cs:18-30,59-68`
  registers the application and Hangfire contexts; no migration/seed invocation
  appears there or in the inspected `Program.cs` startup body. This is **not**
  an exhaustive no-migration/no-seed guarantee for dependency packages or all
  transitive registrations. S3 registration is not itself proof of a startup write.

The four inspected startup/transport/telemetry/database source files were
byte-compared privately against their sandbox counterparts; all matched. No
effective application configuration, user-secret stores or process environment
were inspected to establish dependency isolation. Consequently safe startup and
dependency readiness remain **unverified**, rather than a failed startup attempt.

## Exact safe request and observed signals

Requests used `HEAD /`, no credentials, no response/header output and no redirect
following. The HTTP probe on 8203 is a negative connectivity check, **not** a
claim that the HTTPS-only launch profile supports HTTP. No business endpoint,
dependency health check, or deliberately throwing test endpoint was called.

Windows PowerShell, each URL in the table:

```powershell
$signal = & curl.exe -q --noproxy '*' --proto '=http,https' `
  --connect-timeout 3 --max-time 5 --head --silent --output NUL `
  --write-out '%{http_code}' $url 2>$null
$curlExit = $LASTEXITCODE
# Emit only $url, $curlExit and the three-digit $signal.
```

Inside the **already-running selected sandbox**, the request argv was:

```text
sbx exec rwe-ai-sbx-41bc30 curl -q --noproxy * --proto =http,https --connect-timeout 3 --max-time 5 --head --silent --output /dev/null --write-out %{http_code} <URL>
```

The sandbox calls were executed through `python3`/`subprocess.run` with stdout
captured, stderr discarded and a 10-second outer request timeout. Only URL,
curl exit and the numeric HTTP status were emitted. When typing the argv in a
shell, quote `'*'`, `'=http,https'` and `'%{http_code}'`. `-q` suppresses curlrc
loading; `--noproxy '*'` makes these direct loopback requests. Neither request
used an insecure option, a validation callback, or an alternate CA bundle.

| Client | URL | curl exit | HTTP status |
|---|---|---:|---:|
| Windows host | `http://localhost:8203/` | 7 | 000 |
| Windows host | `https://localhost:8203/` | 7 | 000 |
| Windows host | `http://localhost:18203/` | 7 | 000 |
| Windows host | `https://localhost:18203/` | 7 | 000 |
| Existing sandbox | `http://localhost:8203/` | 7 | 000 |
| Existing sandbox | `https://localhost:8203/` | 7 | 000 |

Connectivity snapshot completed around `2026-11-16T11:27:20+01:00`; it does not
guarantee listener/mapping state after other sessions make changes.

## Current public certificate/trust metadata — not a TLS test

Read-only metadata checks completed around `2026-11-16T11:30:05+01:00`.

| Metadata | Windows application user | Sandbox application user |
|---|---|---|
| ASP.NET development certificate inventory | One in `CurrentUser/My` | One reported by SDK 10 metadata check |
| Subject | `CN=localhost` | `CN=localhost` |
| Thumbprint | `8E84EA4F2F4EC8548DAF359CCB1179008251C714` | `CA669D34D0FA19407DFD5788F3A639AA759A6953` |
| Valid from (UTC) | `2026-01-14T14:47:39Z` | `2026-11-12T07:15:51Z` |
| Valid until (UTC) | `2027-01-14T14:47:39Z` | `2027-11-12T07:15:51Z` |
| Date-valid at inspection | Yes | Yes, from reported validity interval |
| Additional public/status metadata | Issuer `CN=localhost`; localhost DNS SAN present; private-key association present | Development-certificate version `6`; `IsHttpsDevelopmentCertificate=true` |
| Trust evidence | Same thumbprint in `CurrentUser/Root`, not `LocalMachine/Root`; offline system-chain build passed, no chain flags | Tool reports `TrustLevel=None` |

Host certificate stores were opened with `ReadOnly | OpenExistingOnly`, selecting
certificates by ASP.NET's development-certificate extension OID
`1.3.6.1.4.1.311.84.1.1`. The system-chain check disabled downloads and used offline
revocation checking. No private-key operation was requested, no trust entry was
changed, and no network certificate download was requested. This is system-store
evidence, not an actual browser visit or a server hostname-validation handshake.

The sandbox check captured and allowlisted JSON from this installed tool:

```text
/home/agent/.local/share/dotnetup/dotnet/dotnet exec /home/agent/.local/share/dotnetup/dotnet/sdk/10.0.404/DotnetTools/dotnet-dev-certs/10.0.15-servicing.26529.108/tools/net10.0/any/dotnet-dev-certs.dll https --check-trust-machine-readable
```

It ran through the selected sandbox's direct `python3` exec with captured stdout
and discarded stderr. SDK first-run behavior was avoided by directly executing
the located tool DLL. Its child-only environment set
`DOTNET_GENERATE_ASPNET_CERTIFICATE=false`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`,
`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1` and `DOTNET_NOLOGO=1`; none was persisted.
The machine-readable command exited 0 and returned one record; its exit code
alone was not interpreted as trust. It did not use `--trust`, `--import`,
`--clean` or export. No private bytes or passwords were requested or emitted.

**The two inventoried certificates differ.** The sandbox certificate's lack of
sandbox trust does not itself prevent Kestrel serving it, and does not establish
its trust status on Windows. The host certificate's trusted-store presence does
not establish that an app will select or serve it. Effective Kestrel defaults,
certificate overrides and private-key usability inside the sandbox remain
unverified; no app was launched to select any certificate.

## Unverified setup and next authorized step

1. **Startup safety gate:** the application owner must establish the intended
   isolated dependency configuration/no-write behavior, or explicitly authorize
   the identified external effects for specific non-production dependencies.
   Generic permission to start the API was not treated as authorization for
   unknown broker, database, SSM or telemetry writes. No dependencies were started.
2. **Binding gate:** preserve the normal `Web` profile's declared
   `https://localhost:8203` binding. #43 independently demonstrated that
   sandbox-loopback-only listeners are not reachable through native sbx
   publishing. Await its separately verified durable relay/fallback; **do not
   mask this by overriding the API to `0.0.0.0` or editing launch config**. No
   running bind was observed here, so no Deals-specific forwarding failure was
   demonstrated.
3. **Connectivity gate:** this actual sandbox has no existing mapping. Coordinate
   the separate authorized #43 flow to establish `127.0.0.1:18203 -> 8203/tcp4`;
   this baseline deliberately did not run `abox ports` or change publishing.
4. **HTTPS gate:** follow [the #46 research](story-3-https-research.md) for a
   separately approved export/transport/runtime-propagation design. Host trust,
   served-certificate identity/trust and launcher propagation remain unverified
   despite the store metadata above. Do not infer missing certificates from
   connection failures.
5. Once prerequisites are cleared, launch only the task-identifiable External API
   from the verified project using the `Web` profile, with safe output capture and
   an agreed cleanup procedure. Do not launch the Deals worker or stop unrelated
   processes. Repeat a sandbox-local validated HTTPS request and the independent
   host `https://localhost:18203/` request after provisioning exits. Record actual
   HTTP status and public certificate identity/trust evidence; do not turn off
   validation to obtain a pass. Browser trust and an independent Linux-host run
   remain separate evidence requirements.

## Acceptance context

Full bodies/comments of [#3](https://github.com/DerAlbertCom/agentbox/issues/3)
and [#46](https://github.com/DerAlbertCom/agentbox/issues/46) were reread; both
were open with empty comment arrays. They require trusted host access without
app-source edits, not merely a published port. #43's checked-in minimum-version
synthetic evidence establishes publishing mechanics and the bind limitation,
**not** successful Deals startup or HTTPS. This baseline does not close either
story or establish a working HTTPS mechanism.
