# Manual Story #3 host-certificate verifier

Public, one-sandbox evidence helper for
[the verification note](../story-3-host-certificate-verification.md), not a product
provisioner or a general certificate-management tool. Private bytes never belong
in this directory, its build outputs, the app workspace, or the Git repository.

The host is Windows; the fixed sandbox is `rwe-ai-sbx-41bc30`. The only eligible
certificate is the existing host v6 identity recorded in the note. Export uses
the already-installed .NET 10.0.15 dev-certs CLI, after existing-candidate,
exportability, trust, date, SAN and owner-only staging checks. There is no native
store-key export fallback or certificate/trust creation on the host. The CLI's
password argument remains visible to sufficiently privileged process inspection;
its output and exceptions are suppressed. Passwords otherwise travel only in
memory/framed stdin and protected sandbox runtime configuration.

All sbx calls go through `SbxClient`, with a verification-only source-linked
partial. The receiver writes one actual PFX plus canonical Kestrel runtime
configuration under the application user's private, mount-checked runtime path.
It does not stage an opaque bundle and pretend Kestrel can consume it.

## Modes and retained state

```text
dotnet build docs/agents/story-3-host-certificate-probe/HostCertificateProbe.csproj -c Debug
dotnet run --project docs/agents/story-3-host-certificate-probe/HostCertificateProbe.csproj -c Debug --no-build -- --check
```

`--check` inspects the retained owned runtime/processes and existing mapping,
then performs independent sandbox and Windows-host default-validation TLS/HEAD
checks. It neither restarts anything nor changes trust or forwarding. The API
and relay intentionally remain running afterward. `--selftest` tests framing in
memory without changing sandbox filesystem state.

The completed setup used separate, exited processes for `--stage`, `--trust`,
`--activate`, then `--check`. **Do not rerun setup as a check.** `--preflight` and
`--stage` deliberately require the original, now-stopped task PIDs and an absent
runtime destination; `--activate` is also one-shot. They cannot refresh or restart
the currently retained API. Follow the note's current ownership record for any
separately authorized cleanup, rather than weakening the old-PID guards.

`--trust-preflight` and `--trust-audit` inspect the existing sandbox trust tool
and exact public target. The separately authorized `--trust` uses the supported
root exec user only for adding the exact public host certificate through the
already-installed `update-ca-certificates`, never `--fresh` or unrelated source
replacement. Generated trust bundles and installed hooks may change; a whole-file
preservation check can flag these legitimate derived outputs. See the note for
the first update's failed assertion, subsequent audit and idempotent repeat.

This manual explicit-bootstrap launch demonstrates neither automatic propagation
through existing agent/core parents nor the production #46 launcher/renewal
contract. It also does not establish a browser rerun or independent Linux-host
verification; a Linux sandbox client is not a Linux host.
