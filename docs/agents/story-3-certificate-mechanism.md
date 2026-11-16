# Story #3 / #46: certificate-store mechanism spike

Read before choosing #46's runtime certificate mechanism, alongside
[the HTTPS research](story-3-https-research.md) and
[the live Deals baseline](story-3-deals-verification.md).

## Decision: Candidate B does not satisfy the identity contract

**2026-11-16: directly verified fallback persistence, and directly refuted generic
`CurrentUser/My` import as a sufficient #46 mechanism.** Kestrel can serve an
unchanged imported synthetic development certificate after the importing process
exits, without a shell or certificate environment settings. However, an existing
eligible development certificate with a higher **certificate-format version**
wins, including after the imported identity is renewed with newer dates.

The contract remains **copy and serve the exact existing trusted host ASP.NET
Core certificate**, not generate a sandbox replacement or trust a new CA.
Import success, a working TLS endpoint, and the presence of the host certificate
in the store do not establish that contract. Do not select Candidate B for
arbitrary existing sandboxes while preserving unrelated certificates.

This is a verification spike, not implementation or an approved acceptance
deviation. Full #3/#46 bodies and empty comment arrays were read; the native
dependency API still reported #46 blocked by open #43. No product files changed.

## Live synthetic evidence

Windows host, sbx client/server baseline **0.51.0**, disposable Linux shell
template `docker.io/library/sandbox-kit-assembled:2cbc7aa649a63792`.
The probe publishes its own **self-contained .NET / ASP.NET Core 10.0.15** app;
it needs no SDK/runtime installation in the sandbox. Final run used task-owned
sandbox `abox-issue46-cert-20261116-51f083c2-abox-046EF`.

Each provisioning operation was a separate, completed process. Subsequently,
`SbxClient` launched the apphost with argv `[<apphost>, "serve"]`, with closed
stdin and no shell, profile/bootstrap, `--env`, or launcher changes. Runtime modes
refuse root, a sandbox name outside `abox-issue46-*`, and any Kestrel environment
variable. The synthetic web app has **no certificate configuration** and uses
ordinary `WebApplication`/Kestrel fallback. Only its listener is explicitly set:
`https://127.0.0.1:0`, an OS-selected sandbox-local port. No host port or forwarding
was involved, including ports 18203–18206.

| State before a fresh app launch | Strict TLS / actual served identity |
|---|---|
| Synthetic imported host-equivalent v4; unrelated non-development certificate also present | Imported v4 served, exact source DER bytes matched |
| Add a more recently issued synthetic sandbox v6, then re-add the original imported certificate | Host-only client anchor rejected the connection; v6-only anchor validated it and exact peer DER matched v6 |
| Remove only the previous probe-managed imported identity, import renewed v4 with **later dates than v6**, restart app | Same result: host-only anchor rejected; the unchanged competing v6 served |

Final-run public SHA-1 identifiers (diagnostic labels, not the byte-equality test):

- Initial imported v4: `EBB50546FD77E41C7336618AB80D03B81851FA95`.
- Competing v6: `F10C694085FD2F2B6524A54E6892273360510A05`.
- Renewed imported v4: `E1D767BD87328CC1372AFA796235340C91EA7A57`.
- Unrelated non-development certificate: `A8F22C82E3C6765ECCED8BE5E8142543419C8CFB`.

Final inventory asserted exactly three certificates: renewed imported identity,
unchanged competitor, unchanged unrelated certificate; all retained private keys.
The earlier managed identity was gone and repeated addition had not accumulated
copies. Every app/server/helper exited successfully. Task-owned sandbox removal
returned 0; a fresh inventory confirmed both task-owned sandboxes absent. Both
exact synthetic host workspaces were subsequently removed. An earlier complete
run independently reproduced the same selection
and renewal failure; the final run strengthened identity checks to full DER
equality and added the sandbox-name guard.

### Validation and secret boundary

- Synthetic RSA-2048, self-signed `CN=localhost`, localhost DNS SAN, server-auth
  EKU, key usage/basic constraints and ASP.NET dev OID
  `1.3.6.1.4.1.311.84.1.1` with raw version byte 4 or 6. These are minimal synthetic
  test certificates, not exports from any host or assertions of all SDK-generated
  version-specific extensions.
- Each synthetic source certificate was exported to **in-memory** PFX with a
  random password, loaded ephemerally, checked for unchanged DER/private key,
  then added through public `X509Store.Add`. Subsequent store reads and successful
  TLS compared full source/peer DER bytes, not just subject or thumbprint.
  PFX buffers were cleared. Passwords/private bytes were never printed.
- Source generation and PFX round-trip happened **inside the disposable sandbox**.
  This verifies installation/selection, **not host export or secure cross-boundary
  transport**. No real host certificate/private key was read or exported.
- `SslStream` used `TargetHost="localhost"`, its normal hostname checks, and an
  `X509ChainPolicy` with `CustomRootTrust` containing only the expected synthetic
  self-signed public certificate. This is a per-client, in-memory trust anchor,
  not store installation. Chain/hostname validation remained on; there was no
  validation callback or insecure option. Revocation checks/downloads were off
  for these disposable self-signed certificates. Successful checks also required
  the synthetic HTTP marker.
- Only application-user `CurrentUser/My` in the newly created sandbox was changed.
  No root/trust store, existing user sandbox, Deals app, profile, daemon, global
  setting, registry publication, or existing mapping was touched. Private keys
  lived in the sandbox's .NET-managed store, never the mounted workspace.

## Why import cannot guarantee the copied identity

Pinned source was read for the repository's .NET 9/10 workload families and the
installed .NET 11 generation. **Only .NET 10.0.15 was run live here.**

| ASP.NET Core source | Generated / minimum accepted dev-format version | CertificateManager / Kestrel source |
|---|---|---|
| 9.0.23, `3abdd8f554e6e0cba7dd08704f923969762d98c0` | 2 / 2 | [manager](https://github.com/dotnet/aspnetcore/blob/3abdd8f554e6e0cba7dd08704f923969762d98c0/src/Shared/CertificateGeneration/CertificateManager.cs#L81-L174), [Kestrel](https://github.com/dotnet/aspnetcore/blob/3abdd8f554e6e0cba7dd08704f923969762d98c0/src/Servers/Kestrel/Core/src/KestrelServerOptions.cs#L394-L443) |
| 10.0.15, `547fc16b976e18911f9df7630f1f75758ab248f2` | 6 / 4 | [manager](https://github.com/dotnet/aspnetcore/blob/547fc16b976e18911f9df7630f1f75758ab248f2/src/Shared/CertificateGeneration/CertificateManager.cs#L23-L35), [selection](https://github.com/dotnet/aspnetcore/blob/547fc16b976e18911f9df7630f1f75758ab248f2/src/Shared/CertificateGeneration/CertificateManager.cs#L133-L250), [Kestrel](https://github.com/dotnet/aspnetcore/blob/547fc16b976e18911f9df7630f1f75758ab248f2/src/Servers/Kestrel/Core/src/KestrelServerOptions.cs#L378-L427) |
| 11.0.0, `1def4cdc07dc10fc267136060e3ef5c1a96d4a99` | 6 / 4 | [manager](https://github.com/dotnet/aspnetcore/blob/1def4cdc07dc10fc267136060e3ef5c1a96d4a99/src/Shared/CertificateGeneration/CertificateManager.cs#L23-L250), [Kestrel](https://github.com/dotnet/aspnetcore/blob/1def4cdc07dc10fc267136060e3ef5c1a96d4a99/src/Servers/Kestrel/Core/src/KestrelServerOptions.cs#L381-L430) |

All three call `ListCertificates(CurrentUser/My, isValid:true,
requireExportable:false)` and take **element zero**. Candidates with the dev OID
are date/minimum-version filtered and sorted by dev-format version descending.
There is **no preference for an imported thumbprint, copied file, latest import,
newest NotBefore/NotAfter, or host trust**. Equal versions have no explicit
identity/date tie-breaker; their source-store enumeration order is not a supported
import-selection contract. Equal-version competing selection was not live tested.
Older host v2 identities are also below the .NET 10/11 minimum even in an empty
store. Original .NET 10.0.0 generated/minimum versions were 4/4
([pinned source](https://github.com/dotnet/aspnetcore/blob/7387de91234d3ef751fa50b3d1bfede4130213ff/src/Shared/CertificateGeneration/CertificateManager.cs#L20-L27));
SDK/runtime generation and certificate-format version are different concepts.

Changing a copied certificate's version extension changes its signed DER and
invalidates the original signature/identity; reissuing it would violate the
host-certificate contract. Removing/reclassifying competing certificates would
violate preservation. Importing into a different HOME/user requires launcher-wide
identity/environment changes, not an environment-independent solution.

[Kestrel's explicit/default certificate takes precedence and fallback lookup is
cached](https://github.com/dotnet/aspnetcore/blob/547fc16b976e18911f9df7630f1f75758ab248f2/src/Servers/Kestrel/Core/src/KestrelServerOptions.cs#L286-L322).
The fallback logs lack of trust but can still return the certificate. No hot
renewal guarantee follows: these probe apps were restarted for every selection.
The probe's own managed-certificate replacement is not an atomic production
refresh implementation.

Export also needs identity correlation: the inspected 10.0.15 tool's
[interactive generation/export path filters to its current format version and
creates when none qualifies](https://github.com/dotnet/aspnetcore/blob/547fc16b976e18911f9df7630f1f75758ab248f2/src/Shared/CertificateGeneration/CertificateManager.cs#L289-L395).
An existing older trusted identity must not silently become a newly generated,
untrusted identity merely because the newer export command succeeds.

## Retired helper / historical remaining gate

PR #48 removed this probe project and its folder. The
[archived source](https://github.com/DerAlbertCom/agentbox/tree/4ad3b459368789e72400769017153cc1269b49f3/docs/agents/story-3-certificate-probe)
is retained in Git history, not runnable from the current checkout. The observations
and verification counts below describe the original spike, not a cleanup rerun.

**Still unresolved:** persistent **explicit** Kestrel configuration selecting the
exact protected copied PFX for new apps across direct argv, Herdr/long-lived core
parents and agent tools. Profile-only bootstrap and a new process's transient
environment remain insufficient. This spike found no supported store-only way to
override competing identities while preserving exact source bytes/unrelated
certificates; it did not re-investigate sbx environment mutation. The canonical
Path/Password route remains preferred but is not verified by this work.

No actual abox certificate provisioner exists here: the exited processes were
synthetic setup helpers. No real Deals TLS success, host browser trust, independent
Linux-host run, host-to-sandbox transport security, complete launcher matrix or
#46 completion is claimed. Verify those separately after #43 and the mechanism
gate are resolved.

Verification: two full live probe runs; final probe Debug build **zero warnings /
errors**; CSharpier check passed for all three probe project/source files;
whitespace checks passed for the note and probe sources. Published dependency
metadata confirmed both runtime packs were 10.0.15. Product solution tests were
not rerun because no product code changed. This note remains as research/evidence;
the helper sources are archived above. Unrelated work was preserved.
