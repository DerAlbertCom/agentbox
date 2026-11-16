# Application ports: shared discovery and HTTP publishing (#43, #44)

## Discovery and publishing contract

- Location is `--location`, then `defaultLocation`, then the current directory.
  The shared resolver selects current naming first, otherwise exact legacy naming;
  every operation retains its returned actual name. Missing sandboxes give an
  actionable create command. No sandbox creation/replacement, squad/build lookup,
  folder config or project evaluation/execution is part of discovery.
- Recursively inspect exactly named `Properties/launchSettings.json`, `angular.json`
  and `package.json` files beneath
  the selected root, never parents. Skip directories named `.git`, `bin`, `obj`,
  `node_modules`, `dist`, `coverage`, `.angular`, `.abox`, `.vs` (case-insensitive).
  Skip **all descendant directory links/reparse points**, including internal aliases,
  escaping links and cycles. A user-selected root may itself be linked. Matching
  file links/reparse points and unreadable matching files fail rather than being read.
  Directory enumeration failure aborts discovery; it is not an empty result.
- Depth beyond 64 child directories or a matching file larger than 1 MiB fails
  actionably, without publishing a truncated plan. Files are read with a bounded
  buffer. There is no separate breadth limit; cancellation is checked while walking.
- Parse UTF-8 JSON (optional BOM, comments and trailing commas allowed), rejecting
  duplicate properties and malformed root/profile/applicationUrl shapes. Omitted
  profiles/applicationUrl, empty strings/semicolon segments and URLs with no explicit
  port contribute no occurrences. `applicationUrl` must otherwise be a string of
  semicolon-separated absolute HTTP(S) URLs. Accept IPv4, bracketed IPv6 and Kestrel
  `*`/`+` hosts; ports must be literal decimal integers 1..65535. Explicit `:80`/`:443`
  count, implicit scheme defaults do not. Unknown unrelated JSON fields are ignored.
  Invalid inputs name the source, not its raw URL/configuration contents. Angular
  uses the same file/JSON/bounds policy; its format-specific scope and literal
  start-script grammar are [documented separately](angular-application-ports.md).
- Preserve every occurrence across URLs, profiles and files; report source/profile,
  sandbox port and host port. Warn for repeated ports and publish each distinct
  mapping once. The complete plan is validated before publishing: with default offset
  10000, the largest usable sandbox port is 55535. No ports is a successful no-op.
  #45 adds [folder/one-run offsets and safe offset-change handling](../application-port-offsets.md).
- The table is the requested plan, not confirmation of app availability. Host
  publishing uses explicit IPv4 loopback TCP bindings only. Sandbox localhost-only
  apps are supported without source/profile changes or per-app bind overrides.
  Publishing does not start applications. HTTPS ports are reported/published as TCP,
  but certificate export/wiring/trust is not included.
- Complete fixed `sbx exec <actual-name> true` with closed stdin, no TTY, before
  inventory/publishing. This starts stopped sandboxes without replacing them, then
  reads restored mappings. Ensure sandbox relays, then reuse exact matching
  host-to-relay mappings; reject conflicting local bindings and unknown/malformed
  inventory. Append missing mappings in one sbx call
  and confirm requested mappings via a fresh inventory. Upgrade only exact requested
  direct native mappings after relay readiness/full conflict validation, using their
  full original specs and confirming unrelated-inventory preservation. Never remove
  or overwrite unrelated mappings.
  Occupied ports/runtime failure return 1, cancellation 130. Some bindings may remain
  after failure/cancellation, as may sandbox relays; inspect and retry after correcting the cause. No
  transaction, rollback, stale-mapping cleanup or concurrent-writer guarantee.

This is sandbox-to-host application publishing, **not** host-to-sandbox service
access: it neither reads nor changes user-secrets' network-policy intent.

## Why a sandbox relay is necessary

sbx 0.51.0 native publishing reaches a sandbox IPv4 interface, **not sandbox
localhost**. The original negative probe below was an acceptance failure, not a
scope waiver. Requiring developers to override application binds was not approved.
The implementation keeps sbx for host-loopback publication and adds only the
missing sandbox-to-localhost TCP hop.

The embedded `Sbx/application_ports.py` receiver requires **existing Python 3.8+ as
`python3` in the Linux sandbox**, with its standard library and a non-root default
application user. This was weighed against installing a forwarding tool or shipping
an additional platform-specific binary: the standard-library receiver needs no
download, image replacement, host runtime or global settings. `python3 -I` ignores
Python environment/user customizations. A missing/incompatible prerequisite fails
actionably before any new host publication; abox never installs dependencies.

- One detached relay per distinct app port listens on an available sandbox port in
  55536..65535. This is above every usable discovered app port with default offset 10000;
  custom offsets can use higher app ports, which are excluded from relay allocation.
  Allocation also excludes all discovered ports, saved relay ports and existing runtime mapping targets,
  even when no app currently listens there. It never binds wildcard on the **app's**
  port, so already-running and subsequently-started wildcard apps do not collide.
- The relay connects to explicit `127.0.0.1`, `::1`, then the accepted connection's
  sandbox IPv4 ingress address. No DNS-derived remote destination is used. It forwards
  bytes, including TCP half-closes; it does not terminate HTTP/TLS or rewrite headers.
- `~/.abox-ports-v1` in the sandbox is owned/private (0700); saved port and lock files
  are owned regular single-link files (0600), control sockets owned/private (0600).
  Links, wrong ownership, unsafe permissions and malformed state fail closed.
  The sandbox HOME itself must be owned and not group/other writable. No user source,
  launch profile, startup environment or host trust store is modified.
- A bounded file lock serializes setup. Listener/control descriptors are passed
  explicitly to a new Python session with closed inherited streams, not a background
  shell job. A bounded readiness handshake completes before publication. A failed
  startup only terminates the child just created; there is no adoption/killing by a
  persisted PID. Control status and saved ports support repeat reuse and restart.
- Relays survive abox exiting, but end with the sandbox. After a stop, `abox ports`
  starts the sandbox and restores relays on their saved ports; sbx restores the host
  bindings. Starting the sandbox some other way does not automatically restart the
  relays/apps: rerun `abox ports` and start apps normally. This is not #47 lifecycle
  integration. Removing/replacing a sandbox removes its relay processes/state.
- Each relay bounds active connections at 128 and each target-connect attempt at
  three seconds. Existing streams have no artificial idle timeout. A missing app or
  exhausted connection limit closes that connection; publication is not an app
  health check. An occupied saved relay port fails without taking over/killing its
  listener or silently selecting a new target for an existing host mapping.

An older **exact requested** `127.0.0.1:<host>:<application>/tcp4` native mapping can
be upgraded to its localhost-capable relay. All requested conflicts and relay
readiness are validated first; removal uses the complete original spec, never just
a host port. Fresh inventory must confirm removal and preservation of unrelated
bindings before publication continues. Different targets, wildcard/non-loopback
bindings and non-exact protocols are conflicts, never removed. There is no atomic
replace/rollback: failure or cancellation can leave a requested binding absent;
retry uses the saved relay and republishes it. No unrelated mapping is migrated.

Do not delete relay state while runtime mappings still target its ports. No stale-port
cleanup, host/sandbox reboot durability or hostile same-user/root writer guarantee
is claimed.

## Runtime evidence (Windows, 2026-11-16)

Both installed client and running server were **v0.51.0**, revision
`7d55286de0c775148b985ed349f760f1eba06203`, server API `0.49.0`.
This is abox's unchanged minimum supported version, so compatibility was exercised
directly, not inferred from installed help or a newer release. Diagnose had zero
failed checks; update/optional Claude SSH warnings did not require any changes.

Early disposable probe `abox-issue43-probe-a6f280c1` established:

1. Runtime publish `127.0.0.1:18203:8203/tcp4` on an existing running sandbox succeeded.
   A synthetic HTTP response remained reachable after that publishing process exited.
2. Blindly republishing the identical spec returned **409 already published**.
   Therefore the adapter inventories/reuses rather than blindly republishing.
3. Direct publishing while stopped printed "started" but returned **409 cannot
   publish ports while sandbox is stopped**. A completed `exec ... true`, followed
   by inventory and publishing, succeeded and restored prior bindings. No daemon
   restart, retry loop, upgrade or repair was used to mask this observed race.
4. An occupied loopback port returned `host 127.0.0.1:18206 is already in use`.
   Existing unrelated mappings survived. Host listener inspection showed only
   `127.0.0.1` for the tested mappings. The task-owned sandbox was removed.

The checked-in [runtime probe](application-ports/RuntimeProbe) then exercised the
**actual Debug abox executable** against disposable sandbox
`abox-issue43-live-72d48a01-abox-F74D5`:

- A self-contained Debug [synthetic .NET HTTP application](application-ports/SyntheticHttp)
  served `abox-issue43-dotnet-http-marker` from sandbox port 8203.
- `abox ports --location ...` exited 0; a **subsequent** host request to
  `http://localhost:18203` returned that marker, after abox had exited.
- Repeating the command exited 0 with exactly one 18203 mapping.
- Pre-existing unrelated `18204 -> 8204` was retained.
- Stopping the task-owned sandbox, adding discovered port 8205, then running the
  command exited 0 and left all three loopback mappings present. It did not
  restart the HTTP app (starting applications is not the ports contract).
- Holding host `127.0.0.1:18206` with a synthetic TCP listener made the command exit
  1, print an occupied-port diagnostic and partial-state/retry guidance, with no
  success claim; the prior three bindings remained intact.
- `finally` cleanup removed only the sandbox created by the probe (exit 0).

A second complete run with fresh task-owned sandbox
`abox-issue43-live-4b9d8102-abox-64021` repeated those checks and additionally used
the host's TCP-listener inventory to assert **only `127.0.0.1:18203` and
`127.0.0.1:18204`**, with no wildcard or IPv6 listener for those mappings. Its
task-owned sandbox cleanup also exited 0.

**Original failed localhost-only application check:** the probe mode
`--sandbox-localhost` was also run. In fresh sandbox
`abox-issue43-localhost-09ad21c3-abox-E356B`, the .NET app listened only on sandbox
localhost. After abox successfully established the mapping, a separate .NET HTTP
client **inside** that sandbox returned the expected marker from localhost:8203,
but the host request to localhost:18203 failed with a connection-aborted error.
This establishes an actual sbx 0.51.0 interface limitation, not merely a failed app
startup: **sandbox-loopback-only applications are not reached by runtime publishing**.
The original native-only probe failed and removed its task-owned sandbox (exit 0
cleanup). An earlier run had the same failure. This was not accepted and is the
reason for the relay fix; there was no source for the previous document's claimed
"scope clarification" or requirement for application bind overrides.

### Positive localhost-only relay evidence

A prerequisite experiment in fresh task-owned sandbox
`abox-issue43-relay-e1b30-abox-F5B77` first established an actually viable mechanism:
the available `/usr/bin/python3` was 3.14.10; a stdlib TCP relay started with
`subprocess.Popen(start_new_session=True)` and explicit descriptor/stream ownership
survived its bootstrap exec exiting. Native `127.0.0.1:18203:28203/tcp4` then reached
the .NET app listening only on sandbox localhost:8203. Both sandbox-local and host
responses returned `abox-issue43-dotnet-http-marker`. Cleanup removed only that sandbox.
No shell-lifetime, tool-presence or runtime-version assumption substituted for this check.

The **actual Debug abox executable** then passed `--sandbox-localhost` in fresh
`abox-issue43-localrelay-e1b31-abox-B4B0A` and again in
`abox-issue43-localrelay-e1b33-abox-53536`:

- The synthetic .NET app's normal localhost-only binding was unchanged.
- abox exited 0; a subsequent host HTTP request to **localhost:18203** returned the
  marker. Repeating exited 0 and reused the same host/relay mapping exactly once.
- Host listener inventory showed only **127.0.0.1:18203 and 127.0.0.1:18204**, not
  wildcard/IPv6 host listeners. An unrelated mapping was retained.
- Stopping the task-owned sandbox and rerunning abox restored the same relay target
  ports and existing bindings while adding discovered 8205 -> host 18205.
- Occupied host 18206 failed with exit 1 and partial-state/retry guidance, retaining
  earlier mappings. Each task-owned sandbox cleanup exited 0.
- The second run also passed 11 checked-in receiver process/TCP checks, not mocks:
  1 MiB binary/half-close forwarding after bootstrap exit, concurrent/repeated setup,
  wildcard app coexistence before/after setup, dead-relay restoration, occupied saved
  port, malformed state, private ownership/permissions and link/foreign-file rejection.

A further process-seam red/green test reserves **all** high-port candidates as
unrelated runtime targets; the receiver must fail allocation rather than use any.
It first failed on the missing reserved-target protocol, then all **12** checks
passed in `abox-issue43-localrelay-e1b35-abox-D5F44`. That later full HTTP run could
not publish 18203 because another sbx process already owned that host port. It
returned failure, did not change the existing mapping, and removed only its own
sandbox. This external verification collision is not counted as an HTTP pass.

With 18203 reserved for the separately authorized actual Deals API verifier, the
final executable/probe was run with verification-only `--port=8213` in fresh
`abox-issue43-final-localrelay-e1b36-abox-A0196` (still offset **10000**, no product
offset feature). All **12** receiver checks and the complete localhost-only HTTP
flow passed. In addition to the earlier 8203 -> 18203 evidence, this run proved:

- An existing direct native `127.0.0.1:18213:8213/tcp4` was upgraded to relay 59986,
  and HTTP to localhost:18213 returned the marker **after abox exited**.
- Unrelated direct native `127.0.0.1:18214:8214/tcp4` remained unchanged; repeat runs
  reused the relay mapping without another upgrade.
- After sandbox stop/start, the saved relay 59986 and unrelated mapping remained;
  a separately restarted synthetic .NET app with the identical localhost bind was
  again reached through the restored relay after abox exited.
- A newly discovered 8215 mapping and occupied host 18216 failure behaved correctly;
  only host IPv4 loopback listeners were present. Task-owned cleanup exited 0.

A final wildcard-app run in fresh `abox-issue43-final-wildcard-e1b37-abox-EEF7C`
with verification-only `--port=8223` repeated all 12 checks and the complete flow,
including direct native upgrade to relay 63526, host HTTP after abox exit, unchanged
unrelated 18224 -> 8224, and HTTP after stop/start with the same wildcard binding.
It also exited 0 and removed only its task-owned sandbox. This guards against fixing
localhost by occupying an app's existing/future wildcard listener.

No call was made to mutate the user sandbox, its running Deals API or its 18203
mapping. Its live native-only failure is separately recorded in the integration
branch's `docs/agents/story-3-deals-verification.md`; HTTPS certificates remain #46.

### Reproduce

From the repository root, with .NET 10 SDK, sbx 0.51.0+ healthy/signed in and an
**already available local Linux x64 shell template** (replace the example with one
from your local inventory; this probe does not publish an image or change trust):

```powershell
dotnet build cli/agentbox.slnx -c Debug
dotnet run --project docs/verification/application-ports/RuntimeProbe -- `
  "D:/path/to/task-owned/abox-issue43-<fresh-id>" `
  "docker.io/library/sandbox-kit-assembled:2cbc7aa649a63792" `
  "cli/src/Abox/bin/Debug/net10.0/abox.dll"
```

The workspace must not exist and its leaf must start `abox-issue43-`. The probe
refuses an already-existing sandbox identity, creates one disposable sandbox with
`--pull never --static-mcp none`, and removes it in `finally`. All sbx calls go
through `SbxClient` with argv lists; the verification-only partial adds no shipped
entry point. Host abox settings/state are isolated with per-process XDG variables.
Only synthetic files are used. The .NET app is published Debug/self-contained into
that new workspace; no application/runtime installation in an existing sandbox is
needed. Ports 18203–18206 must initially be free. The synthetic workspace is retained
for inspection; remove **that exact task-owned directory** when finished. If cleanup
fails, the probe prints the sandbox name and cleanup warning; do not prune broadly.
Append `--sandbox-localhost` for the **required positive** localhost-only-app check;
it must return zero, including the host response after abox exits. Without the flag,
the app uses a wildcard bind, which must also work without relay/app-port collision.
Both modes run the 12 real receiver checks first, preserve an unrelated direct native
mapping, upgrade an exact requested direct mapping, and verify HTTP again after
stop/start and restarting the synthetic app. Verification-only `--port=8213` chooses
a different synthetic app port and its next three ports, still using the shipped
fixed offset 10000; it does not add a product option or change user app startup settings.
The probe refuses initially occupied test ports rather than modifying other mappings;
do not run it concurrently with another verifier using its chosen four host ports.

## Automated verification and limitations

Red/green command tests first failed on absent `ports`, then on duplicate-warning,
malformed/implicit-port, discovery-bound and failure/cancellation behavior before
the corresponding implementation. Adapter tests first failed on unimplemented
publishing, then on invalid mapping/inventory/conflict protection. Tests observe
the composed CLI seam and `SbxClient` process seam, not private parser methods.

Run `dotnet test cli/agentbox.slnx -c Debug`, `dotnet csharpier check cli` and
`git diff --check`. No Release verification is needed for this slice.

Original native-only Debug verification: **1752 passed, 15 platform skips, zero
failed** (1767 total); solution build **zero warnings/errors**. The skips are 14 existing
Windows filesystem/link-privilege cases plus the new launchSettings file-symlink
case; they are not claimed as passing. Descendant/root directory-link coverage
ran successfully using native Windows junctions. CSharpier passed for `cli` and
the checked-in verification programs; `git diff --check` passed.

Relay-fix Debug verification: **1765 passed, 15 platform skips, zero failed** (1780
total). Focused ports command/adapter tests: **61 passed, one platform skip** (62
total). Debug solution build: **zero warnings/errors**. CSharpier passed for `cli`
and the checked-in verification programs; `git diff --check` passed. New tests
cover relay prerequisite/confirmation failures, cancellation, separate listener
targets, exact native upgrade and full-plan conflict checking. Receiver process/TCP
tests run inside the task-owned Linux sandbox, so the Windows link skips are not
used to claim receiver ownership/link coverage.

Live evidence is **Windows host + minimum sbx 0.51.0**, not an independent Unix-host
runtime run. No claim is made for daemon/host restart persistence, app auto-restart,
non-loopback IPv6 app forwarding or hostile concurrent filesystem/runtime writers. The
contract proven is command-exit persistence, repeated invocation and stopped-sandbox
startup/restoration. No host forwarder is needed; the sandbox stdlib relay is required
for localhost apps. No runtime upgrades/restarts,
global settings/trust changes, registry publication, real secrets or existing user
sandbox mutations were performed. These #43 runs predate Angular;
[the #44 verification record](angular-application-ports.md) adds Angular discovery
and actual dev-server evidence. Offsets #45 are now covered by the linked offset
contract/evidence. HTTPS #46 and automatic create/recreate lifecycle #47 remain
intentionally unimplemented.
