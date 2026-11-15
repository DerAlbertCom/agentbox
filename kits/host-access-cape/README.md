# CAPE host service access

`host-access-cape` grants runtime network access to the **host-side published TCP
ports** in `Carmen.Shared/scripts/containers/docker-compose.yaml` (inventory read
2026-11-15). It does not start services or carry credentials. Every `mti-ca` squad
inherits it from `cape.abox`.

| Compose service | Host port | Container port |
| --- | ---: | ---: |
| postgres | 8004 | 5432 |
| oracle | 1521 | 1521 |
| database-mcp | 8005 | 8080 |
| localstack | 8006 | 4566 |
| s3manager | 8007 | 8080 |
| jaeger | 13133 | 13133 |
| jaeger | 16686 | 16686 |
| jaeger | 4317 | 4317 |
| jaeger | 4318 | 4318 |

## Connection addresses

Inside the sandbox, connect to `host.docker.internal:<host port>`, for example
`http://host.docker.internal:8006` for LocalStack,
`host.docker.internal:8004` for PostgreSQL, and `host.docker.internal:1521` for
Oracle. Do not use sandbox loopback, Compose service names, or container-only ports.

Docker's [host-service workflow][host-services] specifies that the sandbox proxy
translates `host.docker.internal` to `localhost` before forwarding and checking
policy. The kit therefore declares individual `localhost:<port>` entries with
`network-policy@1`, [supported for local sandboxes since sbx v0.45.0][support].
The host's existing network policy still applies; a deny takes precedence.
No wildcard host, unrestricted port grant, global policy change, custom relay,
or `host-port@1` is needed. `port@1` / `sbx ports` instead publishes a sandbox
service to the host and is not used here.

Validation and inspection check the descriptor, not service availability or
successful forwarding. A healthy local runtime and running host services are
required. Rebuild and recreate locally to apply this kit to an existing sandbox;
do not replace a sandbox until its location is confirmed and the build succeeds.

A harmless HTTP check from the sandbox is:

```sh
curl --fail --silent --show-error --max-time 5 \
  http://host.docker.internal:8006/_localstack/health --output /dev/null
```

Use the sandbox's normal proxy environment for this documented access path.
No database authentication is necessary for a TCP-connect-only check of ports
8004 and 1521. A TCP connection alone does not prove database authentication or
query readiness.

[host-services]: https://docs.docker.com/ai/sandboxes/workflows/development/#accessing-host-services-from-a-sandbox
[support]: https://docs.docker.com/ai/sandboxes/customize/author/capabilities/
