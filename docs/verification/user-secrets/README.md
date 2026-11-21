# User-secrets verification spike (#5)

Resumed 2026-11-15 from `fbb2a5b`, after the user confirmed the port mapping and
explicitly asked to continue the tickets. This approves a **change in direction**
from #2/#8's experimental `host-port@1` relay to the supported
`host.docker.internal:<published HOST port>` mechanism. Existing named endpoints
stay unchanged. No hostname-to-port attribution or custom relay is required.
The [initial attempt](initial-attempt.md) is archived evidence, not current blockers.

Only the bundled synthetic values/id were transferred. No real secret stores,
registry publication, host/global settings, daemon repair, or existing sandbox
changes were involved. The pre-existing `agentic-box-abox-4F84A` was inspected only
and was stopped at inspection time; it was not started, replaced or removed.

## Gate and acceptance

| Check | Result |
| --- | --- |
| Healthy supported local mechanism | **Pass.** A fresh local sandbox admitted the required `network-policy@1` kit. Direct TCP connected to `host.docker.internal:8004`, `:1521`, `:8006`; LocalStack health returned HTTP 200. No DB credentials or queries. |
| Supported inspection metadata | **Pass with a different supported command.** `sbx inspect --json` exposes only `network_policy: {scope: global}` here; it is insufficient. `sbx policy ls <name> --source kit --type network --json` returns active, sandbox-scoped provisioned kit rules and the nine literal host ports. No staged-file discovery is necessary for these rules. |
| Local/template descriptor survival | **Pass.** Local `workload` and `host-access-cape` descriptors survive. The template's inherited `shell/kit.yaml` does not; a `FROM` is not declaration inheritance. |
| Actual published squad, without host kit sources | **Pass for creation, metadata and transfer.** An existing `mti-ca/ac` set was resolved read-only and created by digest, with no workspace, local kit sources or build manifest. Its `workload`, `config` and merged `set` descriptors survive; `/usr/share/abox/squad.json` is present. |
| Nonempty host-service declarations in a published squad | **Not demonstrated.** The accessible published set predates the new host-access kit: it declares only `sbx@1` and correctly returns `rules: []` for kit network rules. Its successful TCP reachability through the pre-existing global allow policy is **not** a kit declaration. No new artifact was published. |
| Effective application-user destination | **Pass in both sandboxes.** Default exec is `agent`, uid/gid 1000, HOME `/home/agent`, APPDATA unset. Actual .NET PathHelper/provider reads the HOME path and, with an explicit APPDATA override, the APPDATA path instead. |
| Windows noninteractive UTF-8 transfer | **Pass in both.** `sbx exec -i`, no TTY, explicit UTF-8/no BOM; .NET read exit 42 before and 0 after. Raw source bytes/hash preserved. |
| Unix representative transfer | **Pass in both.** A Unix shell frames and pipes the synthetic file into the same receiver, with APPDATA precedence verified by the actual .NET provider. This is a representative Unix caller, not an independent Unix sbx-host installation. |
| Secrecy and interrupted input | **Pass for the spike adapter.** Payload/frame only on stdin. Child output is drained to a null sink in ordinary and `--verbose` fixture runs, including scalar/reformatted-scalar failure output. Short/corrupt input exits 65 before rename and preserves the previous file. Directories 0700, file 0600, `agent:agent`. |

**Closure decision:** the obsolete unsupported-relay, unhealthy-runtime and
hostname-attribution gates are resolved/superseded, not outstanding work.
The copy/transport approach is now verified on both actual local and published
sandboxes. The remaining strict end-to-end rewrite acceptance is a published
squad **carrying a nonempty host-access declaration**: the available set proves
the empty-inventory case only. Artifact-only/local-set evidence cannot honestly
prove that missing published-runtime case. Keep #5 open if that full matrix is
required before #6; either an already-published suitable reference or explicit
acceptance of the tested local-nonempty/published-empty matrix is needed. No
GitHub statuses/dependencies were changed by this spike. No runtime upgrade,
relay probe, global policy change or name-association decision is needed.

## Observed runtime and published identity

Windows 11 x64 / PowerShell 7; sbx client/server **v0.51.0** revision
`7d55286de0c775148b985ed349f760f1eba06203`, API 0.49.0; Docker Engine 29.9.3;
Buildx 0.38.1; frontend 3.0.0-m.11; SDK 11.0.100, probe runtime **10.0.15**.

Found using Docker's existing image-reference inventory and read-only registry
inspection, not by reading Docker credentials or any secret values:

```text
rwest.jfrog.io/mtiae-docker-release/agentic-box-rwest-mti-ca/ac:latest
linux/amd64 manifest:
sha256:ae9c3211420760e6581ff3fdbcd3357cdd59761d1ad2d352ab0bba3adf81d954
```

The staged set retains digest-pinned workload and config mixin references.
`metadata/published.inspect.json` records its actual sandbox identity. Its bundled squad metadata is schema 1,
team `mti-ca`, squad `ac`, tag `latest`, CreatedAt
`2026-11-09T16:27:12.1739555+01:00`, with empty `Kits`, `Mcp` and `State`.
The workload's staged Dockerfile starts `FROM docker.io/sbx/shell-docker-image`.
No inherited shell descriptor survives in the published runtime either.
This reference is consumable under the **existing** allowed release prefix;
the developer-repository candidate is not allowlisted. No trust setting was changed.

Task-owned sandboxes exercised and removed afterward:
`abox-issue5-local-191454a2` and `abox-issue5-published-b36814c0`.
Sanitized/projection fixtures retain only relevant runtime fields, not secret
metadata. Downloaded images and normal runtime build caches were not pruned.

## Supported discovery and proposed concise rewrite contract (#8)

1. Use supported JSON, not host kit paths or `/etc/hosts`:

   ```sh
   sbx inspect <sandbox> --json
   sbx policy ls <sandbox> --source kit --type network --protocol tcp --decision allow --created-via provisioned --json
   sbx policy check network --sandbox <sandbox> --protocol tcp --json localhost:<port>
   ```

   The first command supplies runtime identity, **not** the port inventory.
   The second supplies active kit rules (observed examples in `metadata/`).
   Validate sandbox scope/identity, status `active`, resource type `network`,
   decision `allow`, action `net:connect:tcp` and provisioned provenance. Select
   only exact `localhost:<integer 1..65535>` resources; deduplicate/sort ports
   numerically. Do not broaden wildcard/global/manual rules into inferred kit
   mappings. An empty inventory is legitimate, not an invitation to guess.
   The final command verifies the current authorization for each candidate;
   active allow rules alone do not rule out a higher-priority deny. Failure or
   malformed required metadata must produce a safe actionable error. Policy
   checks normalize `host.docker.internal:8004` to `localhost:8004`, as observed.
   They do not prove service availability or prevent later policy changes.
2. In supported endpoint fields only, replace `localhost`/`127.0.0.1` with
   `host.docker.internal` **only when the explicit port is in that inventory and
   currently allowed**. Keep the port unchanged: it is already the host's
   published port. Preserve already-named hosts (including `host.docker.internal`),
   unmapped ports and unsupported forms unchanged. Preserve all unrelated
   credentials, text, URL paths/queries and connection-string fields.
3. There is **no** `5432 -> 8004`, `4566 -> 8006`, or container-port translation
   in runtime policy metadata. Thus `localhost:8004` may become
   `host.docker.internal:8004`, while `localhost:5432` stays unchanged.
   `Server=localhost,8004` may become `Server=host.docker.internal,8004`;
   `https://sql.internal:8004` stays unchanged for TLS/SNI. Never infer service
   identity from comments, `/etc/hosts`, port conventions or Compose files.
4. The old first-declared-name rule is inapplicable under the approved mechanism:
   no alias is selected and no named endpoint's port is changed. `sbx ports`
   remains the opposite direction and is not used.
5. Apply the deny list to selected **original** values before rewriting; retain
   the shared copy/force contract and report only keys/counts, never values.

The nine observed kit ports are `1521, 4317, 4318, 8004, 8005, 8006, 8007,
13133, 16686`. The host's pre-existing global allow also authorizes `5432`;
that does **not** add it to this inventory. This distinguishes declared rewrite
intent from broad ambient reachability. [The kit guide](../../../kits/host-access-cape/README.md)
documents the independently confirmed host/container inventory; it is not read
by secret copying. #8 still needs parser/endpoint-form tests and the missing
published-nonempty smoke if required by its revised acceptance.

## Safe transfer contract for #6

Discover effective uid/user, HOME and APPDATA through default application-user
exec, not `-u root`, login-shell guesses, or host path reuse. APPDATA takes
precedence even on Unix: `<APPDATA>/Microsoft/UserSecrets/<id>/secrets.json`,
otherwise `<HOME>/.microsoft/usersecrets/<id>/secrets.json`. Validate empty or
unusual environments rather than silently guessing. Application-specific
environment overrides must match the eventual application's environment.

The new fixture's stdin frame is decimal UTF-8 byte length, LF, lowercase SHA-256,
LF, then exact UTF-8 JSON bytes. The fixed-id receiver writes a same-directory
temporary file under umask 077, checks **both** length and digest, then renames.
EOF by itself is not a commit signal. The short-frame test first failed against
an unverified cat/rename receiver (exit 0 instead of 65); framing checks turned
it green. Both short and same-length corrupt frames now preserve the prior
readable file and its original SHA-256:

```text
74 bytes
999f7ec4049ea51be9bf3f7745a0c73596b3231217773f6a4da7775906d6fb6d
```

The fixture does not test arbitrary ids, symlink/concurrent-writer defenses,
cross-store atomicity, or actual host-process cancellation scheduling. Its
short-frame/EOF case covers truncated stdin, including EOF after interruption;
production cancellation handling still needs adapter tests. Payloads, frames
and payload-derived fingerprints must not be logged. The older
`receive-synthetic.sh` is historical/unframed and **must not** be used as the
production receiver or as evidence of safe interrupted transfer.

`SbxTransportProbe` is an opt-in spike executable, **not** abox's production
adapter. Its `--verbose` exercises an invariant suppression policy, not the
existing `ProcessOutput` implementation. The existing production encoding and
scalar-diagnostic issues described in the archive remain concrete #6 work:
explicit UTF-8 stdin and sensitive output/error handling must be implemented
and tested at the approved sbx/process seam. This spike demonstrates a viable
approach without silently changing `abox secrets`.

## Reproduce, synthetic only

PowerShell 7 from repository root; .NET, Docker/Buildx and healthy local sbx.
Check **every native exit code**, not just the final pipeline command. Choose
fresh names; never reuse/remove a user sandbox. No registry writes or persistent
settings changes are needed. Process-local builder environment is used only
for local source-kit creation.

```powershell
$fixture = 'docs/verification/user-secrets'
$local = 'abox-issue5-local-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$published = 'abox-issue5-published-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$out = '.abox/issue5-' + [guid]::NewGuid().ToString('N')
$ref = 'rwest.jfrog.io/mtiae-docker-release/agentic-box-rwest-mti-ca/ac@sha256:ae9c3211420760e6581ff3fdbcd3357cdd59761d1ad2d352ab0bba3adf81d954'
dotnet publish "$fixture/Probe" -c Release -r linux-x64 --self-contained true -o "$out/probe"
# Scope/save/restore these environment settings in your invoking shell.
$env:SBX_KIT_BUILDER = 'host'
$env:BUILDX_BUILDER = 'desktop-linux'
$env:DOCKER_CONTEXT = 'desktop-linux'
sbx --no-input create "./$fixture/workload" --kit ./kits/host-access-cape --name $local --cpus 2 --memory 2g --pull never --static-mcp none --no-progress
sbx --no-input create $ref --name $published --cpus 2 --memory 2g --pull always --static-mcp none --no-progress
# After verifying these creates succeeded, protect the remaining steps with finally.
try {
    foreach ($name in $local, $published) {
        sbx policy ls $name --source kit --type network --json
        sbx cp "$out/probe" "${name}:/tmp/abox-issue5"
        sbx cp "$fixture/receive-framed-synthetic.sh" "${name}:/tmp/abox-issue5-receive-framed.sh"
        dotnet run --project "$fixture/SbxTransportProbe" -- $name --verbose
        # Expect all nine status checks to pass, including exits 42, 65 and 9.
        sbx cp "$fixture/synthetic.json" "${name}:/tmp/abox-issue5-synthetic.json"
        sbx exec $name sh -c 'APPDATA=/tmp/abox-issue5-appdata /tmp/abox-issue5/Probe'
        # Expect 42 before the representative Unix transfer, then 0:
        sbx exec $name sh -c '(printf "74\n999f7ec4049ea51be9bf3f7745a0c73596b3231217773f6a4da7775906d6fb6d\n"; cat /tmp/abox-issue5-synthetic.json) | APPDATA=/tmp/abox-issue5-appdata sh /tmp/abox-issue5-receive-framed.sh && APPDATA=/tmp/abox-issue5-appdata /tmp/abox-issue5/Probe'
    }
} finally {
    sbx rm $local --force
    sbx rm $published --force
    Remove-Item -Recurse -Force $out
}
```

For forwarding, use the normal sandbox proxy environment for HTTP:
`curl --fail --silent --show-error --max-time 5 http://host.docker.internal:8006/_localstack/health --output /dev/null --write-out '%{http_code}\n'`.
A direct `socket.create_connection(('host.docker.internal', port), 5).close()`
from Python inside the disposable sandbox verifies TCP only, not authentication.

## Repository verification

- `dotnet test --solution abox-cli/agentic-box.slnx` in Debug and Release: **452 passed**
  each, no failures/skips.
- Release solution and all three probe builds: zero warnings/errors.
- `dotnet csharpier check abox-cli docs/verification/user-secrets` and `git diff --check`.
- New live red/green checks above: local and published UTF-8 reads, short/corrupt
  frame rejection preserving prior bytes, uid/modes, scalar failure suppression,
  representative Unix APPDATA reads, invalid existing-target rejection (exit 2),
  exact kit-policy inventory, direct TCP and LocalStack HTTP 200.

No production copy/rewrite command has been added by #5.

## Production copy verification (#6)

The user accepted the local/nonempty + published/empty scope and closed #5; see
the [parent clarification](https://github.com/DerAlbertCom/agentic-box/issues/2#issuecomment-6892259793).
The missing published/nonempty rewrite case remains a documented limitation,
not a blocker for #6. No rewrite is implemented by #6.

`CopyProbe` exercises the **production** shared copy and sbx/process adapters,
not the older spike receiver. It rejects all sandbox names except fresh
`abox-issue6-<8 hex>` names and creates its own synthetic host files. It does not
read actual host secret stores or abox configuration. Payload/frame only use
sensitive stdin, including integrity checks; verbose is enabled throughout.

Verified 2026-11-15 with sbx v0.51.0 in task-owned disposable sandboxes
`abox-issue6-182f2de6` (local) and `abox-issue6-6808739f` (published, using the
same digest-pinned reference recorded above, without workspace or host kit
sources). Both were removed in `finally`; every check passed in both:

- Production Windows UTF-8/no-BOM copy preserved exact Unicode bytes.
- Effective application-user ownership and directory/file modes were 0700/0600.
- Short/corrupt frames rejected before rename and retained prior bytes.
- A destination symlink was rejected before writes; its target stayed unchanged.
- An explicit application-user APPDATA environment took precedence over HOME.
- All checks passed with verbose enabled and no raw child output emitted.

Repository verification for #6: **517 passed** each in Debug and Release, no
failures/skips; Release solution and `CopyProbe` builds had zero warnings/errors.
`dotnet csharpier check abox-cli docs/verification/user-secrets` and `git diff --check`
passed. The full automated run is a Windows-host run; the Unix shell transfer is
representative, not a separately installed Unix sbx-host verification.

Automated tests additionally execute the captured production receiver through the
approved process seam with ordinary disposable files (Git Bash on Windows, `sh`
on Unix). They cover repeated/narrower whole-file replacement, environment
precedence, no-create destination validation, nonregular targets, short/corrupt
input and actual process cancellation during blocked stdin. Command/shared-flow
tests cover invalid/missing config and stores, unsafe IDs, duplicate effective
.NET keys, malformed Unicode/UTF-8, later-store validation/deny with zero writes,
literal/case-insensitive global YAML policy, force narrowness, in-memory snapshots,
value-free transport failures and cancellation recovery. Process tests verify
scalar-output suppression and explicit UTF-8 while retaining ordinary behavior.

Reproduce only with a fresh disposable sandbox, without registry publication,
global settings changes or existing sandbox replacement/removal:

```powershell
$name = 'abox-issue6-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$saved = @{}
foreach ($key in 'SBX_KIT_BUILDER', 'BUILDX_BUILDER', 'DOCKER_CONTEXT') {
    $saved[$key] = [Environment]::GetEnvironmentVariable($key)
}
try {
    $env:SBX_KIT_BUILDER = 'host'
    $env:BUILDX_BUILDER = 'desktop-linux'
    $env:DOCKER_CONTEXT = 'desktop-linux'
    sbx --no-input create ./docs/verification/user-secrets/workload --name $name --cpus 2 --memory 2g --pull never --static-mcp none --no-progress
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic sandbox creation failed' }
    dotnet run --project docs/verification/user-secrets/CopyProbe -c Release -- $name
    if ($LASTEXITCODE -ne 0) { throw 'Production copy smoke failed' }
} finally {
    sbx rm $name --force
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
}
```

For the published case, use a second fresh `$name` and replace only the create
source with the already-published digest below and `--pull always`; run the same
probe and `finally` cleanup. No publication or persistent trust change is needed:

```text
rwest.jfrog.io/mtiae-docker-release/agentic-box-rwest-mti-ca/ac@sha256:ae9c3211420760e6581ff3fdbcd3357cdd59761d1ad2d352ab0bba3adf81d954
```

No cross-store rollback, malicious same-user filesystem-race defense, crash
durability or already-published/nonempty rewrite proof is claimed. Cancellation
after a full valid frame may leave that atomic rename committed; interruption
before it cannot rename truncated data. Host-process cancellation is automated
through the adapter; this live smoke does not claim a separate real-sbx
cancellation-scheduling experiment.
