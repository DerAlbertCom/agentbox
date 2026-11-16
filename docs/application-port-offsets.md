# Folder and one-run application-port offsets (#45)

`abox ports [--location <path>] [--offset <n>]` uses this precedence:

1. `--offset` for this invocation.
2. `portOffset` in the selected sandbox folder's optional `abox.json`.
3. **10000**, including when there is no folder file.

There is no global setting. No invocation writes configuration. The shared
`Configuration/FolderConfig` loader validates the complete folder file once at
entry; an override does not bypass invalid folder configuration. Existing squad
selection/conflict rules and user-secret selections/filtering/snapshot semantics
are unchanged. For example:

```json
{
  "squad": "AC",
  "portOffset": 20000,
  "userSecrets": ["sample", { "id": "filtered", "keys": ["Database:*"] }]
}
```

For an app on sandbox port 8203, the folder default requests host 28203:

```sh
abox ports --location /path/to/project
# http://localhost:28203
```

On a folder without old application bindings, this one-run override requests 38203:

```sh
abox ports --location /path/to/project --offset 30000
# http://localhost:38203; abox.json is unchanged
```

## Accepted numbers and validation

Offsets are decimal integers in **-65534..65534 inclusive**, the useful range for
ports 1..65535. CLI integers can carry a sign; JSON must contain a numeric integer,
not a quoted string, null, fraction or exponent notation. Zero preserves the port
number; negative offsets subtract. For example, 8203 with offset -8202 maps to host
1, and 65535 with offset -65534 also maps to host 1. Low host ports can still require
OS privileges; valid arithmetic is not proof a port can be bound.

The entire discovered plan is checked with widened arithmetic before starting the
sandbox, relays or publishing. Every resulting host port must be **1..65535**.
8203 + 57333 and 8203 - 8203 are errors; no valid subset is published. Integer
overflow, nonnumeric forms, duplicate/unknown folder keys and malformed shared
configuration fail actionably. The offset is applied once to the shared occurrence
list, so all discovered application types and duplicate rows use the same rule.

## Persistent mapping and ownership contract

An override changes configuration selection for one invocation, **not the lifetime
of its runtime bindings**. Bindings persist after abox exits; returning to a folder
default is another offset change, not automatic rollback.

- An unchanged invocation reuses exact mappings and owned sandbox relays; it only
  publishes missing bindings. Duplicate source occurrences still share one binding.
- Before any publication or exact native upgrade, the receiver checks the validated
  TCP-binding snapshot against **all** its saved relay ports and live private control
  sockets. This includes apps no longer discovered, dead relays awaiting restoration,
  and cancellation after a listener started but before its port file was saved.
- A binding to a known relay at another offset causes an explicit **offset-change
  conflict**. Different-offset bindings directly targeting a requested app also fail
  conservatively, without claiming their ownership. No new binding is added and no
  old binding is removed. abox never infers ownership from the host port alone.
- Unrelated targets are preserved. Existing exact requested direct native mappings
  retain #43's explicitly requested localhost-relay upgrade, with full-spec removal
  and preservation checks; other native bindings are not migrated.
- To change offsets, inspect the selected sandbox's inventory and explicitly remove
  only its exact old host-to-relay bindings. Leave relay state intact: the new offset
  reuses those same relays. This slice intentionally has no automatic cleanup/remap
  command and does not implement create/recreate lifecycle setup (#47).

For **operator maintenance**, if inspection confirms a task-owned binding
`127.0.0.1:28203 -> sandbox relay 59646/tcp4`, remove that exact binding only:

```sh
sbx ports <actual-sandbox-name> --json
sbx ports <actual-sandbox-name> --unpublish 127.0.0.1:28203:59646/tcp4 --json
abox ports --location /path/to/project --offset 30000
```

Use the **actual inspected** sandbox name, host port, relay target and protocol,
not these example values, application port 8203, or a broad host-only removal. If
several old abox bindings remain, each must be explicitly removed before the new
offset can proceed. Never delete `~/.abox-ports-v1` while bindings still reference it.
Normal usage remains through abox; these sbx commands are explicit recovery, not
an automatically executed cleanup plan.

## Partial failure and cancellation

There is no cross-process transaction or rollback. Runtime failure or cancellation
can leave a subset published, exact requested native upgrades absent, and relays
running. abox reports failure (1) or cancellation (130), with a folder-qualified retry
that preserves the selected `--offset`. Retry with **that same offset** after fixing
the cause; confirmed bindings are reused and only missing bindings are published.
A different offset conflicts with any surviving old bindings until explicit removal.

Relay setup is locked, but sbx publication is not atomic with the inventory snapshot.
Do not run concurrent publishers or manually modify bindings/state during setup.
Malicious same-user/root state edits and host/daemon reboot durability remain outside
the #43 contract. Unknown or unsafe relay state fails closed; no process is killed
or unrelated binding adopted as a repair.

## Verification (Windows, sbx 0.51.0, 2026-11-16)

Red/green tests covered missing command/folder precedence first. The new adapter
tests then failed on missing offset/inventory transport and mixed-offset validation.
Four real receiver checks failed against the pre-change receiver; after implementing
the guard all **16 receiver process/TCP checks passed** in disposable sandbox
`abox-issue45-green-66814670-abox-52842`, removed successfully.

The actual Debug abox executable subsequently passed the full checked-in
[`OffsetProbe`](verification/application-ports/OffsetProbe) in new task-owned sandbox
`abox-issue45-live-be43bf4f-abox-22EA0`:

- A synthetic localhost-only HTTP server on sandbox 8345 was reached at host **28345**
  with folder `portOffset: 20000`, after abox exited. Repeating reused the same mapping.
- `--offset 30000` returned 1 with an explicit conflict and unchanged inventory; the
  previous 28345 URL remained reachable.
- Explicit full-spec removal of that task-owned old binding allowed `--offset 30000`
  to publish host **38345**, reusing relay **59646**. A host HTTP request after abox
  exited returned the same synthetic marker, and another invocation reused it.
- The folder file remained byte-for-byte unchanged, unrelated native binding
  **48345 -> 9345** survived throughout, and host listeners were IPv4 loopback only.
- All **16 receiver process/TCP checks** passed again, including removed-app conflicts,
  dead saved relays, partial-publication/lost-save acknowledgement recovery and
  explicit-removal/new-offset reuse. Task-owned sandbox cleanup returned 0.

The probe checks chosen host ports are free before creation, refuses an existing
workspace/sandbox, uses an already available local template with `--pull never`,
and removes only its task-owned sandbox in `finally`. All sbx calls are argv lists
through `SbxClient`. No install, registry publication, runtime restart/upgrade,
global settings, certificate/trust mutation, real secrets, user sandbox or Deals
application was involved. This is not an independent Unix-host sbx verification.

From the repository root, with the .NET 10 SDK and healthy sbx 0.51.0+:

```powershell
dotnet build cli/agentbox.slnx -c Debug
dotnet run --project docs/verification/application-ports/OffsetProbe -- `
  "C:/path/to/NEW/abox-issue45-<fresh-id>" `
  "docker.io/library/sandbox-kit-assembled:<existing-local-tag>" `
  "cli/src/Abox/bin/Debug/net10.0/abox.dll"
```

The three host ports **28345, 38345, 48345** must be free. `--receiver-only` skips
the host publication checks. `--templates` lists local templates read-only. Retained
synthetic workspaces are named in output; remove only those exact task-owned folders.
Run the Debug solution tests, CSharpier checks and `git diff --check` as well.

Automated Debug verification for this slice: **1808 passed, 15 platform skips,
zero failures** (1823 total). The skips are the existing Windows file/link privilege
and case-distinct-directory limitations; they are not counted as passes. Focused
ports/configuration/adapter/user-secret snapshot checks passed, and the composed
help output exposes `--offset` without loading runtime configuration. The solution
and offset-probe builds each had **zero warnings/errors**. CSharpier checks for
`cli` and the offset probe, plus `git diff --check`, passed. No Release verification
is claimed.
