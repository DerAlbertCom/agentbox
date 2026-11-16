# Angular application-port discovery (#44)

PR #48 removed the Angular runtime probe and its npm fixture folder. The live
results below are historical, with source links pinned before removal. There is
no ready-made Angular live harness in the current checkout; no Angular run was
repeated for cleanup. Composed CLI/transport tests remain in `cli/test/Abox.Tests`.

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
The shared [offset selection](../application-port-offsets.md) applies identically
to .NET, Angular options/configurations and start-script occurrences: command
override, folder `portOffset`, then 10000. Mixed-source regression tests cover
folder/CLI selection, duplicate reuse and invalid custom-offset rejection before
any publishing. HTTPS certificate setup and lifecycle integration remain separate
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
git diff --check
```

## Earlier synthetic transport check (not Angular acceptance)

The now-retired [AngularRuntimeProbe](https://github.com/DerAlbertCom/agentbox/tree/4ad3b459368789e72400769017153cc1269b49f3/docs/verification/application-ports/AngularRuntimeProbe) was run
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
esbuild native package, not the Linux tooling required by this sandbox. At that
point no package installation had been authorized/performed. Do not count this
synthetic marker as independent real-Angular acceptance evidence. The following
separate run closes the actual-dev-server gap, not by relabelling that listener.

## Actual Angular end-to-end verification (Windows, 2026-11-16)

The probe copied the then-checked-in [minimal Angular app](https://github.com/DerAlbertCom/agentbox/tree/4ad3b459368789e72400769017153cc1269b49f3/docs/verification/application-ports/SyntheticAngular),
including its npm lockfile, into a **new disposable workspace**. Its framework
**22.2.4**, CLI/build **22.2.6**, TypeScript **6.0.3**, RxJS **7.8.2** and tslib
**2.9.1** are exact pins, matching the inspected Deals frontend's Angular 22
scope; the existing Deals app and dependencies are never changed or reused.

Red/green at the live HTTP seam: requiring Angular/Vite HTML first failed against
the previous Python listener in `abox-issue44-real-red-e1c01-abox-7D5F4`. This was
an expected failure, with task-owned sandbox cleanup exit 0. Next, sandbox-local
`npm ci` in `abox-issue44-real-angular-e1c03-abox-DF1AD` failed with
`UNABLE_TO_GET_ISSUER_CERT_LOCALLY` fetching an npm tarball. It was cleaned up,
not counted as a pass. No TLS verification bypass or trust/settings change was used.

The authorized alternative is host-side **local npm installation into the new
mounted fixture only**, selecting Linux x64/glibc optional dependencies and using
its lockfile (`npm ci --os=linux --cpu=x64 --libc=glibc --ignore-scripts`). The
cache is inside that disposable workspace. Existing host Node **26.13.1** / npm
**11.20.0** downloaded 237 packages from the public npm registry; no global
installation or package/image publication occurred. The actual Angular app then
ran inside the sandbox with existing Node **24.22.2**, npm **11.21.1** and Python
**3.14.10** (Python launches/checks Node; it does not serve the application).

Successful real-dev-server run: **`abox-issue44-real-angular-e1c04-abox-6EE9A`**,
minimum sbx **0.51.0**, the same cached local template above, actual Debug abox:

- Before dependency installation, `abox ports` exited 0, reporting options,
  named `same` configuration and `scripts.start` on **8354 -> 18354**, plus named
  `preview` on **8355 -> 18355**. Three occurrences of 8354 warned, with one mapping.
- A separate sandbox check found **no `node_modules`, npm `prestart` sentinel or
  application listener** after discovery exited. Scanning neither installed nor
  ran the app/scripts. The sentinel remained absent throughout the verification.
- The probe explicitly started local `node node_modules/@angular/cli/bin/ng.js
  serve --port=8354`, with **no `--host` override**. CLI version output confirmed
  Angular/CLI versions above; its log showed an Angular application build and
  `Local: http://localhost:8354/`. Node PID **286** remained alive after bootstrap
  exec exited. `/proc/net/tcp6` showed only **`::1:8354`** for the app, no wildcard
  or non-loopback listener. Application JSON remained byte-for-byte unchanged.
- Two subsequent actual `abox ports` commands each exited 0. **After each abox
  process exited**, host `http://localhost:18354/` returned the app's HTML title
  `abox-issue44-angular-index` and Angular/Vite client reference. A separate request
  to `/main.js` returned the compiled `abox-issue44-real-angular-component` marker.
  This was the real Angular dev server, not a Python/.NET stand-in.
- All three inventories were identical: `127.0.0.1:18354 -> 56697/tcp4` and
  `127.0.0.1:18355 -> 62267/tcp4`. No direct mapping was prepublished or upgraded.
  Duplicate/rerun reuse retained precisely two relay mappings. Host listener
  inspection found exactly these **two IPv4 loopback-only listeners**, no wildcard
  or IPv6 host publication. Protected/competing ports were not used.
- `finally` removed only the fresh task-owned sandbox (exit 0), ending its Node
  process/relays, then removed that **exact** synthetic workspace, dependencies
  and npm cache. Source/lockfile are now archived in Git history, not generated outputs.

A second complete real-Angular run after merging offset integration `8cca672`
at **`eaa74ec`** passed in **`abox-issue44-real-angular-e1c05-abox-04E2C`**:
same actual CLI/framework/runtime, unchanged localhost-only app bind (`::1:8354`,
Node PID 416), HTML and compiled-component HTTP after each abox exit, no script
execution during discovery, all duplicates reported, and exact rerun reuse of
**18354 -> 56302** / **18355 -> 62901**. Host listeners again were only IPv4
loopback. Sandbox cleanup exited 0 and its exact workspace/cache was removed.

The representative real-Angular HTTP/compiled-app check is now verified.
**Browser DOM rendering, WebSocket/HMR/live reload, HTTPS and an independent Unix
host run are not verified here.** Raw TCP forwarding does not itself prove those
browser behaviors. This is not a claim about running the existing Deals frontend.

### Review-fix and merged automated verification

The composed-command regression for unrelated `vite`, `next`, custom `node` and
wrapper scripts first failed **10 cases** (exit 1 instead of success/.NET-only
publishing), then passed after recognizing Angular before parsing its flags.
Two `ng dev` split/quoted-port cases first failed, then passed after adding the
documented alias. Quoted literal `ng` commands remain supported; recognized
Angular compound/dynamic commands still fail before any mixed-plan publishing.

Before offset integration, the full Debug suite passed **1853**, with **17 skips**,
zero failures. After merging #45 at `eaa74ec`, full Debug verification passed
**1901**, with the same **17 platform skips** (1918 total), zero failures. Focused
ports/Angular transport/adapter/folder-offset tests passed **196**, with **3 file
symlink privilege skips** (199 total). New merge regressions exercise .NET, Angular
options, named configuration and `ng dev` start occurrences under folder and CLI
offset selection, retaining duplicate rows and rejecting a whole mixed plan for
an invalid custom-offset Angular mapping. The Angular transport test's expected
argv was updated for the #45 offset/binding-snapshot protocol; it still asserts
only sbx calls, distinct relays and no duplicate publish/unpublish operation.
Debug solution build had **zero warnings/errors**; CSharpier passed for `cli`
and all checked-in application-port verification programs, and `git diff --check`
passed. Probe host ports 18354/18355 had no remaining listener after cleanup.
The exact task-owned red-test and lock-generation temporary directories were
also removed. No Release verification is claimed.

### Retired reproduction instructions

The pinned probe/fixture links above preserve the previous implementation and
lockfile. Its project commands no longer apply to this checkout. A new live
Angular verification would need a separately prepared disposable app/target;
unit/transport tests and the historical HTTP results do not prove a new live run.
