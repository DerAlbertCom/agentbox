# Angular application-port discovery (#44)

## Supported static scope

`abox ports` scans Angular and .NET sources together beneath the selected location.
It does not invoke Angular, Node, npm, MSBuild or package lifecycle scripts, and
never edits application sources. No `package.json` dependencies, project file,
`abox.json`, build manifest or Angular installation is required for discovery.

All formats share `AppPortDiscovery`'s filesystem walk and bounded JSON read:
exact case-sensitive filenames; no parent traversal; case-insensitive artifact
exclusions (`.git`, `bin`, `obj`, `node_modules`, `dist`, `coverage`, `.angular`,
`.abox`, `.vs`); skip every descendant directory link/reparse point, including
aliases and cycles; allow a linked selected root; reject matching file links,
unreadable files/directories, malformed JSON and duplicate properties. UTF-8 with
optional BOM, comments and trailing commas is accepted. Depth beyond 64 child
directories or a matching file larger than 1 MiB fails rather than truncating.
There is no breadth cap; cancellation is checked during walking/reading. See the
[shared runtime/security contract](application-ports.md).

### `angular.json`

- Inspect every `projects.<project>.architect.serve`, or the equivalent
  `projects.<project>.targets.serve`. Both container spellings are supported; a
  project declaring `serve` in **both** is ambiguous and rejected.
- Collect `options.port` plus every `configurations.<name>.port`. All named
  configurations contribute, even if not selected by `defaultConfiguration` or
  the start script. An override does not hide an options occurrence.
- Ports must be JSON integer literals in 1..65535. Strings (including numeric
  strings), environment expressions, fractions, exponents, nulls and booleans fail.
- Omitted projects/serve/options/configurations/port contribute nothing. Present
  containers/project/configuration values must have the expected object shape.
  Unknown unrelated fields and non-serve targets are ignored; build-target
  references, builders, defaults, inheritance and external workspace files are
  not resolved. A bare `ng serve` does **not** imply port 4200.
- Reports identify the project/container/options or named-configuration context.

### `package.json`

Only the exact `scripts.start` string is examined, not `start:*`, `prestart`,
`poststart` or referenced scripts. Supported commands start directly with `ng serve`
or the `ng s` / `ng dev` aliases; an optional literal project and other literal arguments may
follow. Recognized port forms (one flag per script):

```json
{"scripts":{"start":"ng serve web --port 4200"}}
{"scripts":{"start":"ng s --port=4200"}}
{"scripts":{"start":"ng serve --port '4200'"}}
{"scripts":{"start":"ng serve --port=\"4200\""}}
{"scripts":{"start":"ng serve \"--port=4200\""}}
```

The first command must be the literal token `ng`, `'ng'` or `"ng"`, followed by
whitespace (or a shell operator, which is then rejected). Non-`ng` commands such as
`vite --port 3000`, `next dev --port 3000`, `node custom.js --port 4200` and wrappers
such as `npx ng`, `cross-env ... ng`, or `npm run ...` are ignored, even with explicit
ports or malformed/quoted/dynamic arguments. They cannot block other discovered
.NET/Angular ports. Command indirection and concatenated/dynamic command names are
not interpreted; configure their Angular port in `angular.json` instead.

The value is an unsigned decimal integer 1..65535. Literal single/double quoting
is stripped; this is **not a shell parser**. When a recognized `ng` start script mentions an
explicit `--port` token, unsupported syntax fails the entire plan, naming the file
and giving supported forms, without echoing its script contents:

- Missing/noninteger/out-of-range values and repeated flags (even identical).
- Variables/substitutions (`$PORT`, `${PORT}`, `%PORT%`, delayed expansion),
  backticks, backslashes/escapes, unmatched quotes, control characters other than
  tabs, shell operators/pipelines/redirection/comments, and glob/group syntax.
  For safety this restriction applies to the **whole script**, including quoted
  arguments, not just the port value. Rejected punctuation is
  backslash, dollar, percent, exclamation, backtick, semicolon, pipe, ampersand,
  angle/round/square/curly brackets, hash, asterisk, question mark and tilde.
- Unsupported `ng` commands (`ng run`), multiple commands, and a `--` argument separator.

Scripts with no explicit `--port` token are ignored, including unrelated server
scripts and `--portable`; no environment or default port is inferred. A present
non-string `start`, non-object scripts/root, or any malformed JSON fails rather
than pretending to discover no ports. To resolve unsupported dynamic flags,
configure a literal serve port in `angular.json` and remove the dynamic flag from
`start`; adding configuration alone does not bypass invalid explicit script input.
Static discovery does not inspect the runtime environment: Angular's `PORT`
environment variable takes precedence over the serve `port` option (including an
explicit `--port`) at startup, so the
static table can differ from the actual listener. No environment expression is
evaluated and no runtime/default port is inferred.
The aliases, default localhost binding and environment precedence follow the
[Angular CLI serve reference](https://angular.dev/cli/serve) (checked 2026-11-16).

## Shared publishing/reporting

Every occurrence remains visible: repeated options/configurations/start flags and
cross-.NET/Angular duplicates share the same warning and host mapping. Duplicate
ports alone are not errors. Each distinct mapping is sent once to the unchanged
sbx relay adapter, with host IPv4-loopback-only publication and repeat reuse.
All files, literal ports and resulting host ranges are validated before any
relay setup/publishing. With default offset 10000, port 55535 maps to 65535;
55536 fails. The table is a plan, not an app health check; abox does not start apps.
HTTPS certificate setup, custom offsets and lifecycle integration are separate
stories, not implied by Angular discovery.

## Automated evidence

Tests observe the composed command and real `SbxClient` -> fake-process seam,
never private parser methods:

- Mixed .NET/options/named-config/start sources, all projects, both target container
  names, retained duplicate occurrences and one distinct forwarding plan.
- Literal split/equals/quoted script arguments; rejection of dynamic, compound,
  duplicate, malformed and unsupported forms before forwarding any valid .NET port.
- No default-port inference, ignored other scripts/build targets, malformed
  containers/ports/JSON/encoding, duplicate properties, file-size and inherited
  depth bounds, descendant directory links/cycles/artifact exclusions.
- At the isolated adapter seam: only `sbx` argv calls, a distinct application port
  passed to the Python relay, one loopback publish call, repeat reuse, and no
  relay/runtime-publish call for invalid mixed input. No Angular process is launched.

Red/green evidence: mixed-project reporting initially found only the .NET row;
quoted scripts initially failed/vanished; compound/dynamic scripts initially
published; null `start` initially threw; `--portable` initially failed; an unsupported
port-like argument alongside a valid flag initially published. Each was made green
before proceeding. Existing .NET discovery tests remained green after moving the
shared walk/read policy out of its parser.

Debug full-suite verification: **1840 passed, 17 platform skips, zero failures**
(1857 total). The two new skipped cases are Angular matching-file symbolic links
that this Windows host cannot create; descendant directory-link tests ran using
native junctions. These skips are not claimed as passing.
Focused command/adapter verification: **136 passed, 3 platform skips, zero
failures** (139 total). Debug solution build had **zero warnings/errors**;
CSharpier passed for `cli` and the checked-in application-port verification
programs, and `git diff --check` passed. No Release verification is claimed.

Run:

```powershell
dotnet test cli/agentbox.slnx -c Debug
dotnet csharpier check cli
dotnet csharpier check docs/verification/application-ports
git diff --check
```

## Representative live verification (Windows, 2026-11-16)

The checked-in [AngularRuntimeProbe](application-ports/AngularRuntimeProbe) was run
against minimum **sbx 0.51.0** and the actual Debug `abox.dll`, in newly created
task-owned sandbox `abox-issue44-angular-7e541-abox-B89F1`, using an already cached
local template `docker.io/library/sandbox-kit-assembled:2cbc7aa649a63792` with
`--pull never --static-mcp none`. Host ports **18344/18345** were checked free
first; protected/competing 18203–18206, 18213 and 18223 were not used.

- Synthetic `angular.json` options and one named configuration declared 8344;
  another named configuration declared 8345. Synthetic `scripts.start` declared
  `ng serve --port=8344`. The command reported all four rows and warned about
  **three occurrences** of 8344.
- A detached **synthetic Python HTTP listener**, bound only to sandbox
  `127.0.0.1:8344`, served `abox-issue44-synthetic-http-not-angular`.
- `abox ports` exited 0; a **subsequent** host request to localhost:18344 returned
  that marker. A second command also exited 0 and a later HTTP request succeeded.
- Inventories after both runs showed exactly two mappings: host 18344 -> relay
  58380 and host 18345 -> relay 65339, both `127.0.0.1`, `tcp4`. No duplicate mapping
  was added. Host listener inventory confirmed exclusively IPv4 loopback.
- `finally` removed only that task-owned sandbox, exit 0. The synthetic workspace
  was retained in the approved temporary directory for inspection.

**This is real discovery/abox/sbx/relay/command-exit HTTP evidence, not a real
Angular `ng serve` run.** The disposable template had Node and Python 3.14.10 but
no `ng` command. The existing host project dependencies contain only a Windows
esbuild native package, not the Linux tooling required by this sandbox. No package
installation or dependency publication was justified or performed. Actual Angular
dev-server/WebSocket/HMR behavior remains unverified; forwarding is raw TCP and
does not interpret HTTP or WebSockets. Do not count this synthetic marker as
independent real-Angular acceptance evidence.

Reproduce from the repo root, choosing two free alternate host ports:

```powershell
dotnet build cli/agentbox.slnx -c Debug
dotnet run --project docs/verification/application-ports/AngularRuntimeProbe -- --inventory
dotnet run --project docs/verification/application-ports/AngularRuntimeProbe -- `
  "C:/path/to/approved/temp/abox-issue44-<fresh-id>" `
  "docker.io/library/sandbox-kit-assembled:2cbc7aa649a63792" `
  "cli/src/Abox/bin/Debug/net10.0/abox.dll" 8344
```

The workspace must not exist. The last argument selects the synthetic sandbox
port; its host port is +10000 and the next host port is also used. The probe refuses
protected/occupied host ports and existing sandbox identities. All sbx calls use
`SbxClient` argv lists (including verification-only helpers); abox host config/state
is isolated via child-process XDG variables. No user sandbox/Deals API, host trust
or certificates, global settings/restarts/upgrades, app sources outside new
synthetic fixtures, real secrets or registry publication were touched.
