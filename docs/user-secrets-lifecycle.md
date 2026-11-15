# User secrets during create and replacement

`abox.json` is runtime-only configuration in the **resolved sandbox folder**
(`--location`, then `defaultLocation`, then the current directory), not the local
team source selected with `--root`:

```json
{
  "squad": "ac",
  "userSecrets": ["my-local-development-store"]
}
```

Only trust this file from a trusted folder: its explicitly selected stores authorize
copying those host user secrets into that folder's sandbox. Review changes to its
squad, IDs and key patterns before running create, replacement or `user-secrets`.
This is the existing folder opt-in boundary, not a separate approval mechanism.
Store IDs also support safe literal prefixes such as `Cape.*`, using the same
[expansion and key-union rules](user-secrets-filtering.md#store-id-prefix-selections)
on demand and during creation/replacement.

## Squad selection

- `squad` selects the local or published squad when `--squad` is omitted.
- An explicit `--squad` may match case-insensitively (`AC` and `ac` match). A
  conflict fails before preparing or replacing a sandbox; neither selection
  silently overrides the other. Local creation retains the team's canonical name.
- With no folder squad, existing selection remains: `--squad`, the only local
  squad, or local interactive selection. Published creation still requires a
  squad when neither the option nor the folder specifies one.
- Accepted newer-build replacement from `run` checks a folder squad against the
  sandbox's record. A different or unknown recorded squad fails safely; create the
  intended sandbox explicitly first. No folder squad means
  there is no extra identity constraint. Ordinary `run`, including declining
  replacement, can open the existing sandbox despite a different or unknown squad
  record, because it neither copies secrets nor replaces the sandbox. A
  missing-sandbox run hint includes the folder squad, without requiring local team sources.
- On-demand `user-secrets` does not verify squad identity or read/require a local
  sandbox record. Missing, matching, unknown or mismatched records do not block
  copying into the current-first, exact-legacy-fallback sandbox for the folder.
  `abox.json` still undergoes normal validation, including the optional `squad` field.

The team still comes from `--team` or `defaultTeam`; `abox.json` does not select a
team. Changing its squad does not silently replace an existing sandbox on `run`.

## When copying runs

Local and published `abox create`, explicit replacement (including `--yes`), and
accepted newer-local-build replacement from `abox run` use `SandboxCreator`:

1. Load and validate `abox.json` once at command entry and resolve a matching squad.
   Pass that same folder configuration snapshot (including absence) to the shared
   lifecycle; do not reload it between squad selection, preparation and replacement.
2. Call shared `UserSecretsCopy.PrepareAsync(folderConfig, false, token)` to
    snapshot **all** selected host stores and associated projects' standard
    appsettings, and check selected host originals against the global deny list.
    Missing sources, unreadable/malformed files or denied host originals fail
    before any existing sandbox is removed. A missing host file is allowed when
    an associated project's appsettings supplies configuration. No sandbox
    metadata or destination access is needed for this preparation.
3. Validate squad/build/state/MCP inputs and obtain replacement consent. Prepare
   state/MCP changes and then remove the old sandbox, if replacing.
4. Create the sandbox. Only a successful creation saves its actual new record.
5. Call shared `CopyAsync(prepared, sandbox, token)` **after saving that record**.
   This is exactly the on-demand copy path, including key selection and supported
    policy-based rewriting and appsettings generation; there is no lifecycle-only
    transport. Generation needs runtime policy, so generated-original deny checks,
    unsafe adaptation and temporary shared-store checks occur here. All stores'
    target/destination validation finishes before the first destination write.
6. Only a successful required copy reports full creation success. `run` launches
   only after that succeeds.

A folder without `abox.json` retains its existing create/run behavior, with no
user-secret host-store reads, target discovery or writes. A config with no
selected stores copies nothing. Ordinary `run`, including declining a newer-build
replacement, never resynchronizes secrets or reads selected host stores. Creation
failure, declined explicit replacement, or cancellation never starts a subsequent
copy. Cancellation is checked again before destructive replacement/creation.

Secret payloads are passed only at runtime through the shared sensitive transfer
path. They are never added to Docker build contexts, workload images, published
kit sets, sandbox records, process arguments or ordinary/verbose diagnostics.
Host stores are unchanged. The separate host-side `abox secrets` command is
unchanged.

Every copy rebuilds each selected destination from current selected host secrets
and generated overrides, removing stale generated values and destination-only
edits. Ordinary `run` performs no discovery or refresh. See the bounded discovery,
precedence, filters and temporary limitations in [appsettings overrides](user-secrets-appsettings.md).

## Failure, cancellation and recovery

An automatic copy failure after creation returns **1**, keeps the actual sandbox
and its new record, prints no full-success/open message, and prevents `run` from
launching. The error includes the folder-qualified retry command:

```sh
abox user-secrets --location "/path/to/folder"
```

Fix host configuration, selected stores or the sandbox destination/runtime and
retry that command; do **not** recreate a successfully created sandbox just to
retry copying. Cancellation returns **130** and prints the same recovery route
when creation has already succeeded. Completed stores may already be replaced;
atomic replacement is per destination file, not a transaction across stores.
Lost acknowledgements may leave a file's final result unconfirmed. There is no
cross-store rollback. On-demand retry replaces selected destination files again.

Automatic copying always enforces the deny list. `--yes` and replacement consent
are **not** force. Neither `create` nor `run` adds a lifecycle `--force` switch.
For an intentional safety override, use the existing explicit command:

```sh
abox user-secrets --location "/path/to/folder" --force
```

This bypasses only matched deny-list policy, not malformed inputs, invalid
configuration, destination/policy metadata checks or transport failures. An
override is reported only when used by a successful copy. If pre-create validation
blocks a store, prefer correcting the values/selection. If an intentional override
is necessary for a new/replacement sandbox, explicitly deselect those stores in
`abox.json`, create with no required copy of them, restore the intended selection,
then run the on-demand `--force` command. Restoring selection alone does not make
ordinary `run` copy anything. The user must deliberately perform this override;
replacement consent never substitutes for it.

## Verification scope

`UserSecretsLifecycleTests` uses the real CLI, shared lifecycle and copy flow,
disposable synthetic host files, and fake external sbx/process boundaries. It
covers local/published creation, both replacement entry paths, squad constraints,
missing configuration, ordinary run, all-store preparation, cancellation and
post-create failures. It does not replace any user sandbox, read real user-secret
stores, publish a registry artifact or change host/global sbx settings. Live
transport/policy evidence and its accepted published/nonempty-policy limitation
remain in [the verification record](verification/user-secrets/README.md).
