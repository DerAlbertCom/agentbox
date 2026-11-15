# Recognized split endpoint settings

The existing User Secrets copy flow also recognizes the explicit, case-insensitive
effective key pair **`Database_Config:Host` + `Database_Config:Port`**. This is a
bounded structured-setting contract, not inference from arbitrary neighboring
properties. `Other:Host`/`Other:Port`, nested `Something:Database_Config` sections,
`Server` aliases and custom port-property names are not paired.

For example, project-local appsettings containing:

```json
{
  "Database_Config": {
    "Host": "localhost",
    "Port": 8004,
    "Database": "development"
  }
}
```

produces only this sandbox User Secrets override when host port 8004 is declared
by kit policy and currently authorized:

```json
{ "Database_Config:Host": "host.docker.internal" }
```

No generated port override is needed: the consumer retains its original port.
Already-selected secret ports are still copied unchanged, including their JSON
type and string spelling. Host secrets, appsettings and project files never change.

## Resolution before eligibility

Resolve the host **and** port using project-local `appsettings.json`, lowercase
`appsettings.development.json`, then **selected** host User Secrets. Effective
keys are case-insensitive. A selected secret host remains authoritative, including
null or remote values; an eligible selected loopback host is still adapted.
A selected secret port can enable or prevent adaptation of an appsettings host.
Without an associated project, a selected secret pair can still be adapted.

The same store and key selections govern output. Selecting only
`Database_Config:Host` can use unchanged appsettings port metadata without emitting
the port. A filtered-out **secret** port never participates: use the appsettings
port instead, or leave the endpoint unresolved when no appsettings port exists.
Filtered-out secret hosts are likewise never used as effective configuration.
Excluding the host key prevents both generation and adaptation of that host.
Empty/no-match filters retain the existing `{}` destination replacement behavior.

## Supported and safe unchanged forms

- Adapt only literal `localhost` (case-insensitive), dotted IPv4 loopback or bare/
  bracketed IPv6 loopback, with one decimal port in 1..65535 that is both declared
  and authorized. Integer JSON numbers and decimal strings (including leading zeros)
  can supply the port. No default port or container-to-host port translation is used.
- Remote/already-adapted hosts and unmapped localhost destinations stay unchanged.
  Missing, null, nonnumeric or otherwise unresolved port values do not justify a
  guessed port or an override. Arrays/objects are not reinterpreted as endpoint pairs.
- A recognized **known mapped-local** pair with unsupported/ambiguous syntax fails
  before any selected destination is written. Examples include loopback host/port
  lists, malformed brackets, whitespace-padded hosts and inline URL/port syntax in
  the Host property. Use one literal host plus one separate decimal Port. Arbitrary
  text and unmapped references do not fail merely for mentioning localhost.
- Generated hosts use the same applicable original-value deny-list, all-store
  validation, destination, policy, lifecycle and stale-entry removal safeguards as
  other [appsettings overrides](user-secrets-appsettings.md). Diagnostics identify
  store/key/project and reasons, not endpoint values. `--force` bypasses matched
  deny policy only, never ambiguous adaptation or validation.

Shared-store compatibility must consider each consumer's resolved host **and** port,
not just the identical generated host text. A consumer with a remote or unmapped
destination must not be redirected by another consumer's generated host override.
Until compatible shared-store merging is integrated, the existing temporary
multi-consumer generation restriction remains fail-closed.

See the [synthetic verification record](verification/user-secrets-split-settings.md).
These configuration tests do not establish database authentication or readiness.
