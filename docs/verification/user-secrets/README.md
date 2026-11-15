# User-secrets verification spike (#5)

For [#5](https://github.com/DerAlbertCom/agentbox/issues/5), gating
[#2](https://github.com/DerAlbertCom/agentbox/issues/2). Observed 2026-11-15.
**Not complete: do not start feature implementation on the assumption that
the proposed relay/discovery contract works on the supported runtime.**

Only synthetic values were used. No registry writes, runtime upgrades, global
settings changes, daemon restarts, or changes to existing user sandboxes were
performed. The executable fixtures below are verification tools, not an abox
feature or production-safe arbitrary-file copier.

## Environment and evidence

| Component | Observed version |
|---|---|
| Host | Windows 11, build 26200, x64; PowerShell 7 |
| sbx client/server | v0.51.0, revision `7d55286de0c775148b985ed349f760f1eba06203`, API 0.49.0 |
| Docker Desktop / Engine | 4.99.0 / 29.9.3; Linux amd64 engine |
| Buildx / BuildKit | 0.38.1 / 0.34.2, `desktop-linux` docker driver |
| Kit frontend actually executed | 3.0.0-m.11, revision prefix `92835bb7` |
| .NET SDK / probe runtime | 11.0.100 targeting net10.0 / 10.0.15 |
| Representative Unix environment | Debian shell-kit filesystem, uid/gid 1000 (`agent`), `/home/agent` |

All three descriptor fixtures pin frontend image index digest
`sha256:adbc0d1ea63451871d5d78111120bb29f2fd2b265e63d39c664ae2c3d722c805`.
The workload and set also pin their published inputs. These are Linux amd64
fixtures; do not assume the pinned manifests support another architecture.

### Acceptance status

| Question | Actual result | Still needed |
|---|---|---|
| Does inspection expose host-service mappings? | **Unresolved.** Inspected an incomplete workload-only sbx sandbox; it had no host-service mapping data. That is not evidence about an admitted relay. | Inspect a successfully created sandbox with a required, active relay. Prefer supported JSON if it exposes effective mappings. |
| Do kit descriptors survive local/published creation, including `FROM`? | **Partially verified.** Local frontend builds plus isolated Docker execution verified set members' descriptors survive, but inherited `FROM` descriptors do not. | Actual local and published squad sbx smoke checks. |
| Does a declared name reach the loopback relay/container port? | **Blocked.** Installed sbx rejects required `host-port@1` as unsupported. Ordered hosts-file names resolve in the representative container, but no sbx relay connection was demonstrated. | A runtime with demonstrated capability support and a healthy disposable sandbox. |
| Effective .NET destination and Windows/Unix stdin? | **Representative checks pass, with an important adapter limitation.** .NET read the fixture after explicit UTF-8 Windows stdin and Unix stdin. Raw Unicode through the current `ProcessRunner` was corrupted on Windows CP850; default JSON ASCII escaping worked. | Repeat through real `sbx exec -i`, in the application user's effective environment. |
| Secrecy and permissions? | **Partial.** Metadata-only receiver emitted no values; directory 0700/file 0600, `agent:agent`; explicit UTF-8 preserved the source hash. Existing verbose diagnostics can expose a JSON scalar fragment; captured output remains raw. | Secret-safe captured-output/error policy and real sbx failure-path verification. |
| Viable approach, without unsupported assumptions? | Findings and fixtures recorded; suggestions below are **not adopted product decisions**. | Runtime support, hostname attribution decision, and remaining live checks. |

## 1. Runtime support and inspection: stop before guessing

Actual local composition with `workload/` and `relay/` exited 1:

```text
required capability selection rejected ... unsupported capability type com.docker.sandbox/host-port@1
```

No mapped sandbox was created. Do not make the capability optional to make the
probe pass: admission after skipping the relay proves nothing about forwarding.
The current [Docker capability support table][support] does not list
`host-port@1`. The [release notes][releases] list v0.53.0 as current and say
v0.52.0 was skipped, but do not establish that v0.53.0 supports this capability.
An upgrade alone is not a verified resolution; none was attempted.

A separate workload-only create built successfully but exceeded the 120-second
client timeout and left a stopped task sandbox. `sbx exec <probe> true` exited
125 with:

```text
503 Service Unavailable: sandbox network proxy is not initialized; restart the daemon after restoring proxy credentials and network policy
```

`sbx diagnose --format json` nevertheless reported passing daemon,
authentication and global-network-policy checks. No global repair was attempted.
That sandbox was removed by its exact task-owned name afterward.

Thus neither a healthy admitted-mapping inspect fixture nor a verified
runtime-discovery fallback exists yet. The staged files are a **candidate**
fallback whose artifact survival was checked, not proof of granted/effective
capabilities. In particular, optional entries/groups may not have been selected,
and a constituent descriptor can contain unresolved create-phase arguments.
Do not treat every declaration found in a file as an active grant.

`sbx ports` / `port@1` publish a sandbox service **to the host**. They are not
host-service relay metadata and were not used for this spike.

## 2. Staging and `FROM` are different composition operations

Actual pinned-frontend Docker build/run checks established:

- `workload/workload.yaml` builds `FROM` the published shell kit. Only
  `/usr/share/sandbox/kit/workload/kit.yaml` remains. The shell's `kit.yaml`
  is absent, while its `kit.dockerfile` is retained.
- `set/set.yaml` lists pinned published shell and gh kits. The local export
  contains `shell/kit.yaml`, `gh/kit.yaml`, and `set/kit.yaml`. The merged
  staged set descriptor has `kind: workload` and digest-pinned `kits`.
  No host-side component source directories were supplied.
- No synthetic user-secrets file was a build input or image layer: the receiver
  and payload were copied/transferred only into a disposable running container.

This matches upstream [frontend source][frontend] (`removeInheritedDescriptors`,
`stageKitSources`) and [set end-to-end tests][set-tests]
(`TestSetBuildsEndToEnd`, `inheritedSourcesSet`). A recipe inherits content,
**not** its base kit's declarations. A set retains the sources of its explicitly
listed components. A local thin workload therefore must carry its own effective
declarations; scanning inherited recipes or old descriptors is not a solution.

These are local exports from published inputs, **not** an actual published
squad pulled and created by sbx. Read-only attempts to resolve the example
`docker.io/docker/sbx-kit-team:1.0.0` and `:1.0.2` were denied/unavailable;
`rwest.jfrog.io/mtiae-docker-release/agentbox-rwest-sample/ac:latest` was not
found. Those example names are not evidence that no published squad exists.
No registry artifact was published to fill this gap.

## 3. Loopback and ordered names

The normative [host-port contract][host-port] is
`host 127.0.0.1:35431 -> sandbox 127.0.0.1:35432` for this TCP fixture.
`container: 0`/omitted defaults to `host`, transport defaults to TCP, and
host family defaults to IPv4. Even an IPv6 host grant has an IPv4 sandbox
loopback endpoint. Bypass HTTP proxies when testing it.

The capability has **no hostname field** and does not itself write `/etc/hosts`.
Our fixture separately requests a startup hook that appends:

```text
127.0.0.1 sql.first.abox.test sql.second.abox.test
127.0.0.1 unrelated.abox.test
```

Running that same hook manually in the disposable Docker container preserved
textual ordering; `getent hosts` resolved both SQL aliases to 127.0.0.1. This
checks name resolution only, not a relay. Real sbx startup hooks may run alongside
the application, so immediate discovery also needs a readiness policy.

**There is no general port-to-names join in these two data sources.** Every
loopback service shares 127.0.0.1; `/etc/hosts` contains neither ports nor kit
ownership. It cannot say whether `unrelated.abox.test` belongs to this service
or which alias belongs to a second relay. Filesystem glob order, DNS/getent
order, and arbitrary hook-script parsing cannot recover declaration provenance.

Suggested resolution for the user's decision: have kit authors stage explicit
ordered service-name-to-mapping metadata in the artifact, validated against
runtime-granted mappings and actual hosts-file resolution. Alternatively, agree
to preserve already-named hosts and use loopback for ambiguous local addresses,
which changes #2's first-declared-name requirement. No new metadata schema or
fallback policy is silently introduced by this spike.

## 4. Effective .NET path and stdin transfer

The compiled `Probe` calls the actual .NET `PathHelper` and configuration
provider with only `abox-issue5-synthetic-only`. It never enumerates secret ids
or prints values. [PathHelper source][path-helper] checks **APPDATA before HOME
even on Unix**; this is not an XDG data/config path. Discover the effective user
and environment of the application, not the host user, a root exec, or a guessed
`/home/agent`. App-specific environment overrides must be accounted for.

Observed in the representative container:

```text
uid=1000(agent) gid=1000(agent)
HOME=/home/agent; APPDATA unset
destination=/home/agent/.microsoft/usersecrets/abox-issue5-synthetic-only/secrets.json
```

With nonempty `APPDATA=/tmp/abox-issue5-appdata`, the actual .NET destination was
`/tmp/abox-issue5-appdata/Microsoft/UserSecrets/abox-issue5-synthetic-only/secrets.json`.
An isolated Windows HOME/APPDATA probe also read its Windows `Microsoft/UserSecrets`
destination. No real host user-secrets store was read. Empty/missing unusual home
environments need explicit handling; the shell fixture is not a complete
replacement for `PathHelper`'s fallback/error semantics.

Red/green smoke: the Unix probe exited **42** before transfer; after transfer it
exited **0** and verified both a non-ASCII marker and an escaped newline. An
explicit UTF-8, no-BOM `ProcessStartInfo` stdin writer on Windows preserved all
74 source bytes, SHA-256
`999f7ec4049ea51be9bf3f7745a0c73596b3231217773f6a4da7775906d6fb6d`.
The directory was **0700** and file **0600**, both owned by `agent:agent`.
A Unix `cat | sh` representative transfer with the APPDATA override passed too.

**Existing adapter limitation:** `ProcessRunner` does not set
`StandardInputEncoding`. This Windows process used CP850: the receiver exited 0
but the Unicode file was only 71 bytes, invalid UTF-8, and the .NET probe still
exited 42. A successful child exit alone does not verify a secret copy.
Serializing the parsed JSON with System.Text.Json's default ASCII escaping
preserved the values through that same adapter and the application passed.
For implementation, explicitly UTF-8 stdin is the preferable transport fix;
default ASCII-escaped JSON is a demonstrated alternative if its serialization
contract is retained. Neither fix is implemented in production here.

Use `ArgumentList`, redirected stdin, no TTY, a fixed receiver script, and no
payload interpolation into shell commands, arguments, or environment variables.
Do not rely on Windows PowerShell text pipelines to preserve encoding/bytes.
The receiver uses a same-directory temporary file, restrictive umask and atomic
rename. It is intentionally limited to the fixture id in a disposable target;
it does not solve production path/symlink/concurrency/rollback validation.

## 5. Secrecy is not guaranteed by stdin alone

Actual `ProcessOutput` characterization (`TransportProbe`) showed:

```text
verbose=False; full-json-hidden=True; scalar-diagnostic-visible=False
verbose=True; full-json-hidden=True; scalar-diagnostic-visible=True
raw-captured-result-retains-payload=True
```

Full stdin and full lines are redacted; a child returning just one scalar from
JSON is not. Escaping/reformatting values can also evade whole-input redaction.
`ProcessRunner` returns unmodified captured output, and `SbxClient`'s existing
outcome/error paths can surface it. The attached-console `ExecAsync` is not a
secret-safe capture boundary. A metadata-only receiver succeeded quietly, but
that does not prove arbitrary child failure output safe.

The copy ticket should use a sensitive-operation output/error policy that never
prints raw child output in ordinary **or verbose** mode, including failures,
while returning safe exit/status/key metadata. Test echoed/reformatted scalar
fragments, cancellation, and errors at the approved adapter seam. Preserve
`abox secrets` behavior; do not weaken the invariant to whole-payload redaction.

## Reproduce the completed representative checks

Run from the repository root with PowerShell 7, .NET SDK, Docker and Buildx.
Choose fresh names. A Docker name collision fails rather than replacing an
existing container. Builds below load **local** task tags; none pushes.

```powershell
$fixture = 'docs/verification/user-secrets'
$name = 'abox-issue5-unix-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$image = 'abox-issue5-workload:' + [guid]::NewGuid().ToString('N')
$setImage = 'abox-issue5-set:' + [guid]::NewGuid().ToString('N')
$out = Join-Path (Get-Location) ('.abox/issue5-' + [guid]::NewGuid().ToString('N'))
dotnet publish "$fixture/Probe" -c Release -r linux-x64 --self-contained true -o "$out/probe"
docker buildx build --builder desktop-linux --file "$fixture/workload/workload.yaml" --load --provenance=false --tag $image "$fixture/workload"
docker run --rm --network none --entrypoint sh $image -c 'test -f /usr/share/sandbox/kit/workload/kit.yaml && test ! -f /usr/share/sandbox/kit/shell/kit.yaml && test -f /usr/share/sandbox/kit/shell/kit.dockerfile'
docker buildx build --builder desktop-linux --file "$fixture/set/set.yaml" --load --provenance=false --tag $setImage "$fixture/set"
docker run --rm --network none --entrypoint sh $setImage -c 'test -f /usr/share/sandbox/kit/shell/kit.yaml && test -f /usr/share/sandbox/kit/gh/kit.yaml && test -f /usr/share/sandbox/kit/set/kit.yaml'
docker run --detach --name $name --network none --user agent --cap-drop ALL --security-opt no-new-privileges --mount "type=bind,source=$out/probe,target=/opt/abox-issue5,readonly" --entrypoint sleep $image infinity
```

Check every native command's `$LASTEXITCODE`; PowerShell does not stop on native
errors by default. Once `docker run` succeeds, use `try/finally` for the remaining
steps so cleanup runs even if a probe fails:

```powershell
try {
    docker exec $name /opt/abox-issue5/Probe     # expect 42: no synthetic file yet
    docker cp "$fixture/receive-synthetic.sh" "${name}:/tmp/abox-issue5-receive.sh"
    dotnet run --project "$fixture/TransportProbe" -- $name
    # Expect final success; raw-Unicode read exit can be 42 on Windows CP850.
    docker exec $name /opt/abox-issue5/Probe     # expect 0, values not printed
    docker exec $name sh -c 'stat -c "%a %U:%G %n" "$HOME/.microsoft/usersecrets/abox-issue5-synthetic-only" "$HOME/.microsoft/usersecrets/abox-issue5-synthetic-only/secrets.json"'
    docker cp "$fixture/synthetic.json" "${name}:/tmp/abox-issue5-synthetic.json"
    docker exec $name sh -c 'cat /tmp/abox-issue5-synthetic.json | APPDATA=/tmp/abox-issue5-appdata sh /tmp/abox-issue5-receive.sh; APPDATA=/tmp/abox-issue5-appdata /opt/abox-issue5/Probe'
} finally {
    docker rm --force $name
    docker image rm $image $setImage
    Remove-Item -Recurse -Force $out
}
```

`TransportProbe` reports existing adapter limitations; it is not a security-test
pass for the future feature. It only accepts a task-prefixed Docker container
name and the bundled synthetic payload. Do not point it at user sandboxes.

For exact UTF-8 byte verification, use a `ProcessStartInfo('docker')` writer with
`UseShellExecute=false`, `RedirectStandardInput/Output/Error=true`, and
`StandardInputEncoding = new UTF8Encoding(false)`. Add separately
`exec`, `-i`, `$name`, `sh`, `/tmp/abox-issue5-receive.sh` to `ArgumentList`;
write `File.ReadAllText(synthetic.json)` to stdin and close it. Drain both output
streams concurrently, check exit/status, compare `sha256sum` with `Get-FileHash`,
then run `Probe`. This was separately exercised; the current `ProcessRunner`
path deliberately remains unchanged so its encoding limitation is reproducible.

## Remaining live verification / decision gate

1. Obtain a runtime which actually admits required `host-port@1` and a healthy
   task-scoped sandbox. Runtime upgrade/restart/settings repair needs separate
   user approval; do not silently repair the shared installation.
2. With a fresh `abox-issue5-*` name and process-local host-builder selection,
   run `sbx --no-input create ./docs/verification/user-secrets/workload --kit
   ./docs/verification/user-secrets/relay --name <fresh> --pull never
   --static-mcp none --no-progress`. On v0.51.0, expect the recorded rejection.
3. If admitted, capture `sbx inspect <fresh> --json` and the staged descriptors
   plus `/etc/hosts`. Exclude unrelated credential metadata from retained
   evidence. Verify effective mappings, selected groups/defaults/args and ordered
   aliases before selecting inspect or staged-file discovery. Do not invent a
   JSON schema from the spec.
4. Start a **synthetic-only**, loopback-only host HTTP responder on free port
   35431 (fail if occupied). Compare
   `curl --noproxy '*' --fail --max-time 5 http://127.0.0.1:35432/` and
   `http://sql.first.abox.test:35432/` from that sandbox with its known marker.
   An HTTP response from a different host service is not success. Stop only that
   responder. This connection check has **not** run successfully here.
5. Repeat metadata, relay and transfer/read/permissions checks on a read-only
   consumable **already published squad**, with no component source tree or
   local build manifest. It must include a template-derived workload and mixins.
   Supply that reference rather than publishing an artifact for this spike.
6. Repeat explicit UTF-8/ASCII-JSON transfer through real `sbx exec -i` from
   Windows and a Unix-like host/representative caller, with the real application
   user/environment and fixed synthetic id. Capture safe failures as well as
   success. Never allocate `-t` or print the payload.
7. Decide how names associate with mappings and how to handle unsupported
   runtimes/ambiguous declarations. Do not change #2's first-name semantics
   implicitly. Until these gates are resolved, #5 remains **open/incomplete**.

## Primary sources

Upstream source links are pinned to investigation commit
`06ac386a1b5d570daff36bba4232fb9e71e45d50`; frontend m.11 independently exhibits
the same descriptor-removal behavior. Runtime product support and the normative
experimental specification are different evidence.

## Repository verification

- `dotnet test --solution cli/agentbox.slnx`: **452 passed**, no failures/skips.
- Release builds of the CLI solution, `Probe`, and `TransportProbe`: succeeded
  with zero warnings/errors.
- `dotnet csharpier check cli docs/verification/user-secrets` and
  `git diff --check`: passed.
- Opt-in fixture runs: staged-file presence/absence, .NET red/green reads,
  Windows explicit UTF-8 bytes/hash, CP850 corruption and ASCII-escaped recovery,
  Unix APPDATA transfer, ownership/modes, hosts-file alias resolution, deliberate
  child failure diagnostics, and invalid-target-name rejection.

These checks do not substitute for the incomplete sbx acceptance rows above.
Only task-owned disposable containers/sandboxes and local image tags were
removed. Downloaded public image/build caches were not globally pruned.

[support]: https://docs.docker.com/ai/sandboxes/customize/author/capabilities/
[releases]: https://docs.docker.com/ai/sandboxes/release-notes/
[host-port]: https://github.com/docker/sandbox-kit-spec/blob/06ac386a1b5d570daff36bba4232fb9e71e45d50/docs/spec/capabilities/com.docker.sandbox/host-port@1.md
[frontend]: https://github.com/docker/sandbox-kit-spec/blob/06ac386a1b5d570daff36bba4232fb9e71e45d50/cmd/frontend/build.go
[set-tests]: https://github.com/docker/sandbox-kit-spec/blob/06ac386a1b5d570daff36bba4232fb9e71e45d50/cmd/frontend/set_e2e_test.go
[path-helper]: https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Configuration.UserSecrets/src/PathHelper.cs
