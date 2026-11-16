# Application ports: first HTTP slice (#43)

## Discovery and publishing contract

- Location is `--location`, then `defaultLocation`, then the current directory.
  The shared resolver selects current naming first, otherwise exact legacy naming;
  every operation retains its returned actual name. Missing sandboxes give an
  actionable create command. No sandbox creation/replacement, squad/build lookup,
  folder config or project evaluation/execution is part of discovery.
- Recursively inspect exactly named `Properties/launchSettings.json` files beneath
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
  Invalid inputs name the source, not its raw URL/configuration contents.
- Preserve every occurrence across URLs, profiles and files; report source/profile,
  sandbox port and host port. Warn for repeated ports and publish each distinct
  mapping once. The complete plan is validated before publishing: with offset 10000,
  the largest usable sandbox port is 55535. No ports is a successful no-op.
- The table is the requested plan, not confirmation of app availability. Runtime
  publishing uses explicit IPv4 loopback TCP bindings only. Applications must listen
  on a sandbox-reachable IPv4 interface (for example `0.0.0.0`); publishing does not
  change app bindings or start app processes. HTTPS ports are reported/published as
  TCP, but certificate export/wiring/trust is not included.
- Complete fixed `sbx exec <actual-name> true` with closed stdin, no TTY, before
  inventory/publishing. This starts stopped sandboxes without replacing them, then
  reads restored mappings. Reuse exact matching mappings; reject conflicting local
  bindings and unknown/malformed inventory. Append missing mappings in one sbx call
  and confirm requested mappings via a fresh inventory. Never unpublish anything.
  Occupied ports/runtime failure return 1, cancellation 130. Some bindings may remain
  after failure/cancellation; inspect and retry after correcting the cause. No
  transaction, rollback, stale-mapping cleanup or concurrent-writer guarantee.

This is sandbox-to-host application publishing, **not** host-to-sandbox service
access: it neither reads nor changes user-secrets' network-policy intent.

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

**Negative localhost-only application check:** the optional probe mode
`--sandbox-localhost` was also run. In fresh sandbox
`abox-issue43-localhost-09ad21c3-abox-E356B`, the .NET app listened only on sandbox
localhost. After abox successfully established the mapping, a separate .NET HTTP
client **inside** that sandbox returned the expected marker from localhost:8203,
but the host request to localhost:18203 failed with a connection-aborted error.
This establishes an actual sbx 0.51.0 interface limitation, not merely a failed app
startup: **sandbox-loopback-only applications are not reached by runtime publishing**.
The probe failed as expected and removed its task-owned sandbox (exit 0 cleanup).
An earlier localhost-only run had the same host failure and successful cleanup.
Application execution is separate future work per the scope clarification; this
slice neither starts apps nor changes their bind settings. Users must start apps
with a sandbox-reachable IPv4 bind, as documented above. No custom forwarder or
automatic source/startup mutation was introduced to hide this limitation.

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
Append `--sandbox-localhost` to reproduce the **negative** loopback-only-app check;
it should return nonzero after confirming the internal response and still clean up
the task-owned sandbox.

## Automated verification and limitations

Red/green command tests first failed on absent `ports`, then on duplicate-warning,
malformed/implicit-port, discovery-bound and failure/cancellation behavior before
the corresponding implementation. Adapter tests first failed on unimplemented
publishing, then on invalid mapping/inventory/conflict protection. Tests observe
the composed CLI seam and `SbxClient` process seam, not private parser methods.

Run `dotnet test cli/agentbox.slnx -c Debug`, `dotnet csharpier check cli` and
`git diff --check`. No Release verification is needed for this slice.

Final Debug verification: **1752 passed, 15 platform skips, zero failed** (1767
total); solution build **zero warnings/errors**. The skips are 14 existing
Windows filesystem/link-privilege cases plus the new launchSettings file-symlink
case; they are not claimed as passing. Descendant/root directory-link coverage
ran successfully using native Windows junctions. CSharpier passed for `cli` and
the checked-in verification programs; `git diff --check` passed.

Live evidence is **Windows host + minimum sbx 0.51.0**, not an independent Unix-host
runtime run. No claim is made for daemon/host restart persistence, app auto-restart,
IPv6-only app forwarding or hostile concurrent filesystem/runtime writers. The
contract proven is command-exit persistence, repeated invocation and stopped-sandbox
startup/restoration. No host forwarder was needed. No runtime upgrades/restarts,
global settings/trust changes, registry publication, real secrets or existing user
sandbox mutations were performed. Angular #44, offsets #45, HTTPS #46 and automatic
create/recreate lifecycle #47 remain intentionally unimplemented.
