# Disposable actual Angular application (#44)

This is a minimal, real standalone Angular 22 application for the
[AngularRuntimeProbe](../AngularRuntimeProbe), not a substitute HTTP server.
The exact framework/CLI/build pins follow the inspected Deals frontend's Angular
22 scope without reusing or modifying that repository. `package-lock.json`
records transitive versions and integrity hashes; no dependencies/build outputs
are checked in.

Run the probe from the repository root as documented in
[the verification record](../../angular-application-ports.md#reproduce). It copies
this source into a new task-owned workspace, rewrites only the disposable copy's
port literals when requested, demonstrates static discovery before installing
dependencies, installs locked Linux packages locally through existing host
Node/npm, then runs the real local Angular CLI inside the disposable sandbox.
The app's default `localhost` binding is not overridden. HTTP assertions run
after each actual Debug abox process exits; they check Angular/Vite HTML and the
compiled component, duplicate mappings, rerun reuse and listener addresses.
Browser rendering/WebSocket/HMR behavior is not covered.

The `prestart` script creates a sentinel if anyone invokes `npm start`; discovery
must never create it. The probe intentionally starts the local CLI directly only
after the no-execution assertions, so the sentinel stays absent.

The lockfile was generated using existing host Node 26.13.1/npm 11.20.0 in a new
temporary copy, not by installing into this source tree:

```powershell
# In a NEW disposable copy of this fixture only:
npm install --package-lock-only --ignore-scripts --no-audit --no-fund `
  --registry https://registry.npmjs.org --cache .abox/npm-cache
# Actual probe install into its new mounted copy (Linux x64 sandbox):
npm ci --os=linux --cpu=x64 --libc=glibc --ignore-scripts --no-audit --no-fund `
  --registry https://registry.npmjs.org --cache .abox/npm-cache
```

No global installs, TLS bypasses, trust/settings changes, image publication or
existing-sandbox mutations are needed. Successful probe cleanup removes its
exact workspace including dependencies/cache, after removing its own sandbox.
