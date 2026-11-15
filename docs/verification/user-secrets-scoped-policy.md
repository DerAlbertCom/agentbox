# Explicit sandbox-scoped policy mapping

## Clarified contract

The original [#2 story](https://github.com/DerAlbertCom/agentbox/issues/2) already
said: **"The user has no kit files: a published squad is pulled from the registry"**
and **"Works for published and local sandboxes; no host-side kit files needed."**
Its subsequent approved policy-discovery clarification explicitly excluded manual
rules. #12 and children #13–#16 retained that kit-only eligibility model.

The current user clarified both **"he will just get the images and layers from
jfrog"** and **"you (in another session) told me that this is also possible over
policies"**, while requesting a missing 1521 mapping without kits/recreation.
They then authorized adding the host-access-cape policy equivalents to the
`D:\dev\rwe` sandbox for mapping QA. This extends rewrite-intent eligibility to
explicit, persistent sandbox-scoped TCP localhost-port rules; it does not make
arbitrary effective network permission a mapping.
The user further confirmed that mapped ports must **always** be looked up from
the sandbox. Product discovery therefore performs only sandbox policy inventory
and effective checks, with no kit-file, build-record, image/layer or registry
lookup. The CAPE descriptor below was only the one-time authorized QA reference,
not an input to production mapping discovery.

Source independence and kit provenance are separate: the old implementation
already queried runtime policy rather than host kit files, but unnecessarily
excluded explicitly added rules for this requested workflow. Published sets retain
processed v3 declarations in OCI descriptor annotations (see
[artifact distribution](../artifact-format.md)); creation provisions runtime kit
rules from declarations, not from a consumer's source checkout. Published/nonempty
kit-policy rewriting remains historically unverified; this change does not claim
to fill that gap.

## Isolation and regression

Started from open PR #19's fetched head
`8e8447166be3e6ed9337c8b49125db6bb0435bba`, in isolated worktree
`D:/dev/rwe/agentbox-scoped-policy-20261115-e`, branch
`fix/scoped-user-secret-policy-20261115-e`. No push or PR update was performed.
The original checkout's modified `run-abox.ps1` and the existing diagnostic
worktree's untracked regression test were left untouched. The launcher retained
SHA256 `F303DBA9D64E8B51D6BE9CC7F4F4ECCE59C0D1DD97A4CE71E292F71557E0D967`.

Confirmed test interfaces: production `SbxClient.GetUserSecretsHostPortsAsync`
with a substituted process runner, then the existing CLI harness with production
policy discovery substituted into `ISbxClient`'s port-discovery method. All copied
configuration in tests is synthetic; no private parser interface was exposed.

**Red before green:**

```powershell
$sdk10 = 'C:/Program Files/dotnet/sdk/10.0.404/dotnet.dll'
dotnet $sdk10 build cli/agentbox.slnx -c Debug -m:1 --nologo --verbosity minimal
dotnet cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll -method '*ExplicitSandboxTcpPolicySuppliesRewriteIntentWithoutKitSources' -noLogo -maxThreads 2
```

The exact observed manually added rule shape failed with the value-free metadata
error (**1 failed**), not port 1521. The inventory filter and parser both assumed
kit provenance. That regression is now named
`SandboxRuntimePolicyAloneSuppliesRewriteIntentWithoutLocalKitFiles`; it supplies
only sandbox runtime policy and asserts that discovery makes policy calls only.
After the narrow implementation the existing policy suite passed.
A second red loop demonstrated that unknown expiry/constraint metadata was
otherwise accepted (**3 failed**); only the observed persistent-rule schema is now
eligible. Unknown lifetime/conditional semantics are excluded, not interpreted.

Coverage includes source-kit compatibility; sorted/deduplicated mixed inventory;
actual global/fallback rule shapes lacking sandbox/provenance fields; global,
wildcard, all-port, remote, other-sandbox, UDP/both-protocol, inactive/expired,
mismatched provenance/identity and malformed manual records; effective denial and
malformed metadata with `--force`; safe diagnostics and unchanged synthetic inputs.
CLI tests adapt synthetic Oracle descriptors from both appsettings and selected
secrets using real policy extraction. Remote/unmapped copied values remain intact;
appsettings-only unchanged values correctly generate no overrides.

## Explicitly authorized QA policy changes

Runtime: **sbx v0.51.0**, build `7d55286de0c775148b985ed349f760f1eba06203`.
Before each mutation, inventory was checked for an existing exact declaration,
not merely effective `allowed: true`. Current folder name `rwe-abox-3C02C` was
absent; runtime inspection confirmed legacy `rwe-ai-sbx-41bc30`'s workspace is
exactly `D:\dev\rwe`. Production `SandboxResolver.FindAsync` independently
confirmed that identity afterward.

Read `kits/host-access-cape/host-access-cape.yaml` unchanged: it contains only nine
exact TCP runtime allow destinations. Its network policy has no deny, wildcard,
nonnetwork or port-translation semantics to approximate.

These commands were executed, one rule per exact resource:

```sh
sbx --no-input policy allow network --sandbox rwe-ai-sbx-41bc30 --protocol tcp localhost:1521
sbx --no-input policy allow network --sandbox rwe-ai-sbx-41bc30 --protocol tcp localhost:8004
sbx --no-input policy allow network --sandbox rwe-ai-sbx-41bc30 --protocol tcp localhost:8005
sbx --no-input policy allow network --sandbox rwe-ai-sbx-41bc30 --protocol tcp localhost:8007
```

| CAPE destination | Result / rule ID |
| --- | --- |
| `localhost:1521` | Added `193e2d6d-00f2-4400-83f0-b68a8e0659d6` |
| `localhost:8004` | Added `e5488e13-1b43-4810-ad9f-998d08b91137` |
| `localhost:8005` | Added `cb0e234b-84ab-4444-9e1d-d72b24f50c68` |
| `localhost:8006` | Already explicitly kit-declared; unchanged |
| `localhost:8007` | Added `9f9886ac-f92c-4bdc-8c3c-4e27515bc76e` |
| `localhost:13133` | Already explicitly kit-declared; unchanged |
| `localhost:16686` | Already explicitly kit-declared; unchanged |
| `localhost:4317` | Already explicitly kit-declared; unchanged |
| `localhost:4318` | Already explicitly kit-declared; unchanged |

Each new rule has exactly one resource, sole action `net:connect:tcp`, decision
`allow`, `scope` and `applies_to` equal to `sandbox:rwe-ai-sbx-41bc30`, matching
`sandbox_id`, `origin: scoped`, `layer: local`, `status: active`, `editable: true`,
and `provenance: {created_via: added}`. Rule name equals its UUID; the local
policy ID is `f69b7d5b-161a-4f16-9db8-aa114afdcac4`. No kit provenance was fabricated.
Before/after structured comparisons confirmed every pre-existing rule unchanged
and exactly the requested new resources/actions/scope. All four effective TCP
checks returned allowed for the matching sandbox and exact target. Broad existing
allows were neither removed nor treated as declarations.

## Read-only production verification

A temporary console probe referenced the newly built application assembly and
called only production `SandboxResolver.FindAsync` and
`SbxClient.GetUserSecretsHostPortsAsync` with the real process runner. The probe
and generated build outputs were removed afterward; no public QA command was
added. It asserted this exact sorted inventory:

```text
1521, 4317, 4318, 8004, 8005, 8006, 8007, 8804, 13133, 16686, 65520, 65521, 65522, 65523
```

All nine CAPE ports were recognized. Existing legacy declarations for 8804 and
65520–65523 were preserved. The production method ran effective checks for every
candidate; the global `**` rules supplied no extra candidates.

No application configuration or source secret values were read or copied. No sandbox files,
kit sources, image/layer contents or global settings were changed; no kit
operation, recreation, restart, publication, service connection, authentication
or query was performed. These are policy-discovery and synthetic configuration
checks, not a live-service readiness claim. Real secret-copy QA remains a separate
explicit user operation after the code is integrated.

## Verification commands

Use the installed SDK explicitly and the direct xUnit executable (the existing
story record documents this host's `dotnet test` handshake limitation):

```powershell
dotnet tool restore
dotnet csharpier format cli
dotnet $sdk10 build cli/agentbox.slnx -c Debug --no-restore -m:1 --nologo --verbosity minimal
dotnet cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll -class Abox.Tests.UserSecretsPolicyTests -class Abox.Tests.UserSecretsRewriteTests -noLogo -maxThreads 2
dotnet cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll -noLogo -maxThreads 2
dotnet $sdk10 build cli/agentbox.slnx -c Release --no-restore -m:1 --nologo --verbosity minimal
dotnet cli/test/Abox.Tests/bin/Release/net10.0/Abox.Tests.dll -noLogo -maxThreads 2
dotnet csharpier check cli
git diff --check
```

Final verification on 2026-11-15, after the sandbox-only interface wording update:

| Check | Result |
| --- | --- |
| Focused policy + rewrite suites | **129 passed, 0 failed, 0 skipped** |
| Debug solution build | 0 warnings, 0 errors |
| Full Debug executable | **1217 passed, 0 failed, 7 platform skips** |
| Release solution build | 0 warnings, 0 errors |
| Full Release executable | **1217 passed, 0 failed, 7 platform skips** |
| CSharpier | Passed, 168 files checked |
| Whitespace | `git diff --check` passed |

The seven skips are not passes: four receiver symlink cases, two project-file
symlink cases, and one case-distinct host-directory case unsupported on this host.
No independent review or PR update is claimed by this implementation record.
