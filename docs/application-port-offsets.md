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
- Before any publication, the receiver checks the validated
  TCP-binding snapshot against **all** its saved relay ports and live private control
  sockets. This includes apps no longer discovered, dead relays awaiting restoration,
  and cancellation after a listener started but before its port file was saved.
- A binding to a known relay at another offset causes an explicit **offset-change
  conflict**. Bindings directly targeting a requested app at another host port also fail
  conservatively with a **direct-native-mapping conflict**, without claiming ownership
  or a previous abox invocation. No new binding is added and no
  old binding is removed. abox never infers ownership from the host port alone.
- Unrelated targets are preserved. **All unproven direct native bindings at a requested
  host port are conflicts**, including exact `127.0.0.1:<host>:<app>/tcp4` mappings at
  offsets 0, 10000 and 20000. Matching numbers provide no ownership provenance. Only
  matching mappings to relays confirmed through abox's private state/control protocol
  can be reused. No native binding is automatically removed, migrated or taken over.
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

The same deliberate operator-consent rule applies to a direct native conflict. For
example, if inventory shows `127.0.0.1:18203:8203/tcp4`, abox will **not** infer that
the binding is its own, even at default offset 10000. Only if you have inspected it
and intend to replace it with a relay should you manually run:

```sh
sbx ports <actual-sandbox-name> --unpublish 127.0.0.1:18203:8203/tcp4 --json
abox ports --location /path/to/project --offset 10000
```

Substitute the **exact inspected** spec; do not remove another user's/tool's binding
to resolve a conflict without their consent. The product never executes this removal.
The former #43 automatic exact-native upgrade is retired as safety hardening of the
original unrelated-mapping preservation requirement, not a waiver of that requirement.
Already-converted abox relay bindings continue to work unchanged.

## Partial failure and cancellation

There is no cross-process transaction or rollback. Runtime failure or cancellation
can leave a subset published and relays
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

### Historical offset verification through `8cca672`

The runs below preceded the unproven-direct-binding safety hardening. They remain
valid evidence of that earlier SHA, not reruns of the current native-conflict contract.

Red/green tests covered missing command/folder precedence first. The new adapter
tests then failed on missing offset/inventory transport and mixed-offset validation.
Four real receiver checks failed against the pre-change receiver; after implementing
the guard all **16 receiver process/TCP checks passed** in disposable sandbox
`abox-issue45-green-66814670-abox-52842`, removed successfully.

The actual Debug abox executable subsequently passed the then-checked-in
[`OffsetProbe`](https://github.com/DerAlbertCom/agentbox/tree/4ad3b459368789e72400769017153cc1269b49f3/docs/verification/application-ports/OffsetProbe) in new task-owned sandbox
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

A final post-integration-merge run in fresh
`abox-issue45-final-b2de4ad5-abox-67BBA` repeated the entire HTTP/default/override/
conflict/recovery flow, reusing relay **56270** across offsets. All **17 receiver
checks** passed, now also explicitly checking that a different-offset direct native
binding is not assumed owned or upgraded. The unrelated binding and configuration
again survived unchanged; task-owned sandbox cleanup returned 0.

The probe checks chosen host ports are free before creation, refuses an existing
workspace/sandbox, uses an already available local template with `--pull never`,
and removes only its task-owned sandbox in `finally`. All sbx calls are argv lists
through `SbxClient`. No install, registry publication, runtime restart/upgrade,
global settings, certificate/trust mutation, real secrets, user sandbox or Deals
application was involved. This is not an independent Unix-host sbx verification.

PR #48 removed the offset/runtime probe projects and their folders. The linked
archive is historical, not runnable from the current checkout; the recorded live
runs were not repeated for removal. The independent 17 process/TCP regressions
were [retained outside the projects](verification/application-ports.md#retained-receiver-checks).
They do not replace the removed actual-CLI/sbx host-publication harness. Run the
solution tests, CSharpier checks and `git diff --check` for current automated coverage.

Final post-merge automated Debug verification for this slice: **1810 passed, 15 platform
skips, zero failures** (1825 total). Focused ports/configuration/adapter/user-secret
snapshot checks: **113 passed, one platform skip** (114 total). The skips are the existing Windows file/link privilege
and case-distinct-directory limitations; they are not counted as passes. The composed
help output exposes `--offset` without loading runtime configuration. The solution
and offset-probe builds each had **zero warnings/errors**. CSharpier checks for
`cli` and the offset probe, plus `git diff --check`, passed. No Release verification
is claimed.

### Native-binding safety hardening (current contract)

The three-offset adapter regression first failed against `8cca672`: offset 10000
silently succeeded after takeover; offsets 0/20000 attempted takeover before failing
confirmation. After removing the entire native-upgrade path, all three cases pass:
the full conflicting spec and explicit exact-removal remediation are reported, with
**no `--publish` or `--unpublish` call** and unchanged unrelated inventory. Composed
CLI tests use the real adapter at the same three offsets, checking exit 1, no success
claim, offset-preserving retry guidance and no binding-mutation commands.

The real receiver diagnostic regression first failed in disposable
`abox-issue45-safety-red-60ec3c50-abox-A620B`: a first-use direct binding at another
host port was mislabeled as an offset change. All **17 receiver process/TCP checks**
then passed in `abox-issue45-safety-green-ea6808a7-abox-6E62F`, including the accurate
direct-native conflict and unchanged saved-relay offset/ownership/recovery checks.

That green sandbox also passed the updated full **actual Debug executable** offset
probe on minimum sbx **0.51.0**:

- Direct native **8345 -> 8345**, **18345 -> 8345**, and **28345 -> 8345** were each
  refused at offsets **0, 10000, 20000** with conflict/remediation diagnostics and
  byte-for-byte unchanged inventories, including unrelated **48345 -> 9345**.
- Only after each rejection did the probe operator explicitly remove its own exact
  direct binding. This is authorized disposable-fixture cleanup, **not product cleanup**.
- Folder offset 20000 reached the localhost-only app at **28345 after abox exited**;
  repeat reuse passed. Override 30000 refused while that old relay binding existed;
  the old URL still responded and inventory was unchanged.
- Operator removal of only that task-owned old relay binding enabled **38345** at
  offset 30000, reusing relay **60956**. HTTP after abox exit and another repeat
  passed. Unrelated binding and folder configuration remained unchanged; tested
  host listeners were IPv4 loopback only. Task-owned sandbox removal exited **0**.

The updated `RuntimeProbe --sandbox-localhost --port=8353` also passed in fresh
`abox-issue43-safety-bd8599fd-abox-550C4` on sbx 0.51.0. All **17 receiver checks**
passed again. The current executable rejected direct **18353 -> 8353** without
changing either binding; after explicit operator removal of that task-owned spec,
localhost-only .NET HTTP succeeded after abox exit via relay **63321**. Repeat reuse,
IPv4-loopback-only host listeners, unrelated **18354 -> 8354** preservation, saved
relay restoration after sandbox stop/start (with separately restarted unchanged app),
new **18355 -> 8355** publication and occupied **18356** failure all passed. Cleanup
removed only the task-owned sandbox with exit **0**. These chosen host ports were
checked free before creation; the existing Deals/API port **18203 was not used**.

Final hardening Debug verification: **1814 passed, 15 platform skips, zero failures**
(1829 total). Focused ports-command/adapter/folder-offset checks: **109 passed,
one platform skip** (110 total). The solution, offset probe and runtime probe builds
had **zero warnings/errors**. CSharpier passed for `cli` and the checked-in application
port probes; `git diff --check` passed. No Release or independent Unix-host runtime
verification is claimed. Configuration snapshot code was not changed; the unused
three-argument `AppPorts.PublishAsync` overload was removed so callers must pass the
explicit offset selected from their entry-time folder snapshot.

No live user sandbox/API, certificate, trust store, global setting or registry
publication was touched. Retained workspaces are under the approved temporary
directory; no broad prune or cleanup was run. Historical automatic-native-upgrade
checks above remain evidence of their old SHAs only.
