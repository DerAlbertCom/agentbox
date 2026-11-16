# App ports and HTTPS during creation and replacement

Local and published `abox create`, explicit replacement (including `--yes`), and
accepted newer-local-build replacement from `abox run` use the same shared
`SandboxCreator` and `ApplicationPorts` flow. No additional command, opt-in file,
global setting or app-source edit is required. Apps themselves are not started.

## Ordering and identity

1. The command loads and validates the resolved folder's `abox.json` once. The
   immutable snapshot, including absence, selects squad, user secrets and
   `portOffset`; an absent offset is **10000**. `--root` selects local team sources,
   not the folder to scan. Lifecycle commands add no `--offset` option.
2. Before runtime mutations, prepare all selected host user secrets and statically
   discover/validate the complete .NET/Angular port plan with its explicit offset.
   Malformed inputs and out-of-range mapped ports cannot remove an existing sandbox,
   register MCP servers, create state directories or change registry-source settings.
   The validated discovery plan is retained; there is no second scan after deletion.
3. Validate squad/build/state/MCP inputs, resolve the actual sandbox identity and
   obtain replacement consent. Current-first/exact-legacy fallback is unchanged.
   Accepted `run` replacement carries its already-selected sandbox into creation;
   a later candidate cannot redirect removal, the saved record or setup.
4. Prepare state/MCP changes and create or replace the sandbox. Only successful
   creation saves the new record under that actual name. Failed creation, declined
   replacement and cancellation start no subsequent setup.
5. After record save, finish required [user-secret copying](user-secrets-lifecycle.md).
   Its existing deny-list, destination, policy and all-store safeguards are unchanged.
6. Publish the shared table/warnings and distinct offset mappings, then configure
   [development HTTPS](https-development-certificates.md) for statically detected
   ASP.NET Core projects, including Web projects without explicit launch ports.
   Angular-only, non-Web .NET and empty folders perform no host certificate operation.
7. Only complete required setup permits full creation success and a replacement
   `run` launcher. Cancellation is checked between stages and before full success.

The mapping table is a plan, not an app-health or browser-trust claim. Python 3.8+
must already exist for the non-root sandbox application user; nothing is installed.
The [mapping ownership/offset contract](application-port-offsets.md) is unchanged:
existing proven abox relay mappings are reused, but **all unproven native direct
bindings are conflicts at every offset**. Nothing is automatically unpublished.

## Certificates and replacement

Every applicable successful create/replacement requests a new CLI-only export of
the **current existing trusted host identity**, using the same protected export,
Kestrel/default-user activation and public sandbox/Node trust module as `abox ports`.
No alternate identity or host trust change is allowed. Replacement therefore gets
the host certificate current at setup time. Do not renew/delete host certificates
concurrently with export. Existing apps and agent/core parents require the explicit
restart/fresh-parent recovery documented in the HTTPS contract; abox never restarts
unrelated apps to satisfy setup.

## Failure, partial state and retry

A required post-create failure keeps the created sandbox and saved record, returns
**1** (or **130** on cancellation), prints no full-success/open message, and prevents
the accepted replacement `run` from launching. Completed copies, bindings, relays,
public trust or private certificate activation may remain; no rollback is promised.

- If user-secret copying fails, fix and retry the printed folder-qualified
  `abox user-secrets --location "<folder>"` first. App-port/HTTPS setup has not started;
  then run the separately printed ports command.
- If app-port/HTTPS setup fails, fix the reported input, conflict or prerequisite
  and retry **without recreation**, using the printed snapshot offset:

  ```sh
  abox ports --location "/path/to/folder" --offset 10000
  ```

The explicit retry offset matters if `abox.json` changed after command entry.
On-demand retry rescans current app configuration and does not recopy user secrets.
Changing offset while old relay bindings remain requires deliberate exact-binding
removal as documented; retry does not silently remap or take over other bindings.

Ordinary `abox run`, including declining a newer build, performs **no new implicit
setup or refresh**. This preserves existing launch/recovery access and squad behavior;
it is not proof that an earlier failed setup has since completed. Use `abox ports`
explicitly to finish it, or to refresh a certificate without replacing a sandbox.

## Verification

Command/lifecycle tests use the real shared orchestration with synthetic files and
certificate CLI output, substituted external adapters, and a process-substituted
real `SbxClient` relay/reuse flow. They cover local/published creation, explicit and
accepted-run replacement, snapshots, legacy identity, mixtures, failure, cancellation
and recovery. See [the scoped verification record](verification/application-ports-lifecycle.md).
