# Fresh-verifier gate: real-host production flow

**Historical procedure:** PR #48 removed the activation probe and its target
preparation/cleanup helper. The
[pre-removal procedure and sources](https://github.com/DerAlbertCom/agentic-box/tree/4ad3b459368789e72400769017153cc1269b49f3/docs/verification/https)
remain archived, not runnable as a complete procedure from this checkout. The
steps below explain the earlier gate; a future run needs a separately prepared
disposable target and explicit authorization. Probe removal performed no live run.

**Windows Release production/default-TLS gate executed by a fresh verifier.**
[The real-host evidence](real-host-production-verification.md) records the exact
latest tested candidate `5b40b570aff9fb76b9c6379e55569eef83dc2df9` (PEM/bootstrap
implementation `fdd1916`), strict host/sandbox system/Node TLS, repeat safety,
bootstrap-only refresh, supplemental UTF-8 caller-CA checks and confirmed cleanup.
Older candidate results remain separately pinned. **#46 remains incomplete:** browser UI,
Copilot shell-tool, Herdr desktop, independent Linux host, real-host renewal and
exact 18203 production proof were not established. The older pinned run is
recorded separately; a default-path pass does not clear independent review.

Synthetic checks are not a substitute for this gate. A verifier needs the user's prior
authorization to export the existing host localhost identity into a new disposable
sandbox. This procedure never uses the manually configured Deals sandbox,
runner/API/relay, host key export helper or manual trust entries.

## Historical target preparation

1. The verifier built the exact candidate CLI and helper, recording Git SHA, OS,
   SDK and sbx versions, with an already cached, approved clean template containing Python 3.8+,
   `sh`, .NET 10 SDK and the Debian-style `ca-certificates` store/updater. Do not
   install prerequisites into a user sandbox to make this pass.

   The retired helper's `prepare-production` mode created a new random `abox-issue46-activation-production-*`
   workspace/sandbox, a trivial certificate-unaware web app and launch settings
   for sandbox port **8443**. It refused existing names and verified no prior abox
   HTTPS runtime/trust directory. It did **not** export/configure a certificate.
   Copy its exact workspace and sandbox identity into the verification record.
   Review the template's public CA inventory: no preexisting localhost/host leaf,
   manual runtime/environment or trust import may supply the success condition.

2. Set `$workspace` to that printed path; `$abox` to the absolute candidate
   `abox-cli/src/Abox/bin/Release/net10.0/abox.dll`. Confirm host **18443** is free.
   Never remove an existing binding/listener or touch Deals' **18203** to free it.
   If necessary select a free host port and compute `offset = hostPort - 8443`.
   Record the changed port; exact 18203 remains a separate required gate on an
   isolated host where it is already free.

3. Before export, record the **public thumbprint** of the single existing eligible
   Full-trust localhost identity, using the read-only machine-readable report from
   an already-installed compatible dev-certs DLL:

   ```powershell
   dotnet exec "$DevCertsDll" https --check-trust-machine-readable
   ```

   Select `$DevCertsDll` according to the installed-format contract in
   [the HTTPS documentation](../../https-development-certificates.md). Ambiguous,
   untrusted or ineligible identity is a failed prerequisite, not authorization to
   create/replace/trust a different certificate. Record public host trust inventory
   before/after; never use `--trust`, `--clean`, native private-key export or a
   synthetic exporter for this gate. Avoid concurrent host renewal/deletion.

## Exercise production, then start a new app

4. With export approval confirmed, invoke the **actual production command** as a
   separate process and require exit zero before starting any app:

   ```powershell
   & dotnet $abox ports --location $workspace
   if ($LASTEXITCODE) { throw 'Production setup failed; no acceptance claimed.' }
   ```

   This must exercise the real `HostDevelopmentCertificate`, its CLI-only export,
   private staging/cleanup, root **public-only** system trust, non-root activation,
   readiness check and forwarding. Do not substitute `setup` from the synthetic
   probe, a manual PFX/password/environment, a user shell profile or extra trust.
   Inspect only file ownership/modes/names and public fingerprints, not passwords
   or private bytes. Check host private staging was removed. A failure can leave
   partial sandbox trust/forwarding/runtime state; record and retry only this
   disposable, never call it complete based on a later manual workaround.

5. The verifier launched the helper-generated certificate-unaware app **after**
   setup exited, through actual `abox run shell`. That generated app/launch command
   is part of the retired harness, not a fixture supplied by this checkout.

6. Require the marker `abox-production-verifier` at
   `https://localhost:18443/` with **default host TLS validation** (e.g.
   `curl.exe --fail https://localhost:18443/`). Also use a default `SslStream`
   without a validation callback to authenticate `localhost` and compare the
   remote certificate's public thumbprint with step 3. No `-k`, custom root or
   hostname bypass is acceptable. Open/reload a host browser and record its result
   separately; command-line success is not a browser observation.

7. Via `abox run shell --location ... -- <argv>`, validate the same URL at
   `https://localhost:8443/` from a new sandbox system-store TLS client and from a
   Node TLS client using default validation. Confirm `NODE_EXTRA_CA_CERTS` still
   includes the template/user's original CA roots as well as this exact leaf.
   Repeat production `ports`; verify stable password/path without printing them,
   one current root-owned public certificate and no obsolete runtime PFX copies.
   Renewal requires a **separately approved** host renewal; do not renew for this
   test without it. Synthetic renewal coverage is not real-host renewal evidence.

8. Use fresh actual Copilot **shell tool** and Herdr **desktop** launch paths and
   record exact-leaf/default TLS outcomes independently. No inference/authentication
   or session restart is authorized merely by this procedure. If unavailable,
   record the missing permission/prerequisite, not a pass from help/version/core.
   Repeat the production gate on an independent Linux host. Windows-host Linux
   containers do not satisfy that requirement.

## Cleanup and report

The retired helper's `cleanup` mode stopped only verifier-owned work and removed
its exact target, confirming sandbox absence and workspace removal. It is no longer
available in this checkout; any future verification must plan equally targeted
cleanup in advance. Preserve only public source and sanitized results. Report each gate
separately: production real-host export/activation, default host TLS/exact leaf,
browser, sandbox system TLS, Node, repeat/renewal, Copilot shell, Herdr desktop,
independent Linux host, Debug/Release, cleanup and unchanged host trust. Any missing
gate leaves #46 incomplete; no issue/PR closure is part of verification.
