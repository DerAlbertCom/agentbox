# Creation/replacement app-port setup (#47)

## Scope

Implementation builds on `7274f59a5d6c3d94db922f86c53f616b493f6fa6` and reuses
the existing `ApplicationPorts`, development HTTPS and sbx modules. The agreed test
seams are composed create/run commands through real shared lifecycle/orchestration,
with synthetic host files/certificate CLI output and substituted external adapters;
one transport test also uses the real `SbxClient` with a substituted process runner.

This is not evidence of registry publication, a new Linux host, real-host renewal,
or a new browser/Herdr run. #46's [Windows acceptance clarification](https://github.com/DerAlbertCom/agentbox/issues/46#issuecomment-6918879462)
remains in force: Copilot is excluded, independent Linux-host proof is deferred,
and the retained Deals 18203/manual browser result is distinct from disposable
production-flow evidence. Existing Deals and the user's root script must not change.

## Behavioral matrix

`cli/test/Abox.Tests/ApplicationPorts/AppPortsLifecycle*` currently covers 53 cases:

- Local and published creation without folder configuration; automatic table and
  default offset after record save; no host certificate access for Angular-only,
  non-Web .NET or empty folders.
- Mixed .NET/Angular mappings, folder offset and selected user-secret copying before
  ports, activation and verification; no sensitive certificate child diagnostics.
- Explicit local/published replacement and accepted newer-local-build replacement
  from run; certificate export/activation requests the current **synthetic** identity
  again on replacement. This does not renew or access the real host identity.
- Entry-time offset snapshots, including missing configuration, survive later file
  edits. Legacy actual identity is retained for creation, records, ports and launch
  despite a new candidate appearing after selection.
- Malformed discovery and mapped overflow fail before destructive work or registry
  source-setting changes. Existing user-secret lifecycle/squad tests remain intact.
- Failed/cancelled create starts no setup; failed required copy blocks ports/HTTPS;
  failed publishing, missing host dotnet, activation and verification retain the
  created record, report partial state/retry, and prevent full success/launch.
- Cancellation immediately after successful creation, publishing and activation
  starts no following setup stage. Recovery through `ports` works without recreation.
- Real sbx adapter with mocked processes starts/restores the selected stopped target,
  reuses an existing owned relay on on-demand retry and publishes once. Ordinary
  launch and declined replacement add no implicit discovery, setup or unpublish.

## Red → green observations

Observed failures before their fixes (not merely proposed tests):

1. Both creation modes returned success without invoking application-port setup.
2. Six malformed/overflow replacement cases removed/recreated the existing sandbox
   before discovery failed.
3. Six publishing failure/cancellation cases lacked truthful retained-record recovery.
4. Cancellation after publishing still started certificate lookup; cancellation
   after activation still started readiness verification.

Each slice was followed by a passing focused run. Final pinned builds, complete
test results, maintainability checks and any disposable-live evidence are recorded
below after verification. The initial `dotnet test --filter` invocation was not a
valid runner invocation and ran zero tests; verification uses the direct xUnit v3
executable instead. One intermediate rebuild collided with the still-running
task-owned test executable; the subsequent serial build passed.
