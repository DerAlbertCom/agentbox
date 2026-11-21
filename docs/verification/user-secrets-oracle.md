# Oracle endpoint adaptation — issue #15

## Scope and seam

Synthetic-only verification through the existing CLI command with temporary
workspace/host stores and a substituted `ISbxClient`, reusing
`UserSecretsCliHarness`. No real secrets, Oracle instance, sandbox, publication,
global settings or `run-abox.ps1` changes are involved. These checks establish
configuration output and safety, not authentication, query readiness or service
availability. Existing policy and sensitive-transport verification remains
applicable; no second copy/testing pipeline was added.

`UserSecretsCopy.Oracle.cs` is a private part of the existing endpoint dispatcher.
It records host spans rather than rebuilding connection strings/descriptors.
Quoted `Data Source` values are decoded only for parsing, with spans mapped back
to their original quoting. Original-value denial, precedence, filtering,
project/store selection, all-store preparation, destination validation and
transport remain owned by the shared flow.

## Red → green evidence

Commands were run in the task-owned `feat/issue-15-oracle` worktree, originally
based on integration `2e51210` (the merged #13 foundation):

- `dotnet test cli/agentic-box.slnx -c Debug --filter FullyQualifiedName~UserSecretsOracleCommandTests`:
  the initial Easy Connect slice failed **4 of 5** cases because generated
  endpoints were absent; the implementation passed all five.
- The descriptor slice failed **4 of 4**, then passed all four after structural
  parsing was added.
- Unsafe-input coverage failed **3 of 16**, exposing unfinished nested
  `Data Source` quoting and malformed Easy Connect options. The fixed command
  tests passed, including normal/verbose output and zero writes across stores.
- Additional unbracketed IPv6 cases failed **2**, then passed after explicit
  fail-closed recognition. Valid bracketed IPv6 remains supported.
- A doubled-quote descriptor nested in a quoted connection-string value failed
  **1 of 5** descriptor cases, then passed after source-span quote decoding.
- Generic TCP URI userinfo/escaped-path/fragment regression cases failed **2 of 5**,
  then passed after retaining the existing URL dispatch for URI-only syntax.

## Coverage

`UserSecretsOracleCommandTests` covers standalone and nested Easy Connect and
descriptors from both appsettings and selected host secrets; localhost, dotted
IPv4 and IPv6 loopback; remote/unmapped hosts; compound destinations, shared
trailing ports and address lists; default 1521 only when mapped; preserved
port spelling, credentials, service/SID/instance names, descriptor formatting and
options; malformed/duplicate/proxy/ambiguous mapped inputs; all-store zero-write
failures; source immutability; normal/verbose value-safe diagnostics;
development/authoritative-secret precedence; exact/wildcard/empty filters;
selected/unselected stores; project-only generation; denial/force accounting;
policy changes and regeneration without stale entries.

Two historical "unsupported Oracle" preservation fixtures now use an unmapped
port instead. Their formerly mapped forms are covered by command-level positive
tests. Generic TCP URL preservation retains its regression coverage.

## Verification before integrating siblings

- Oracle command suite: **59 passed**, no skips or failures.
- Debug solution build: **0 warnings, 0 errors**.
- Full Debug suite: **1049 passed, 7 explicit platform skips, 0 failures**
  (**1056 total**). Skips are native Windows file-link privilege/case-distinct
  directory limitations, not passing tests.
- Formatting was applied using `dotnet csharpier format cli`.

Release verification is intentionally left to the final integration orchestrator.

## After merging shared-store integration

Merged `integration/issue-12-endpoint-overrides` at `32569b8` (including #14)
into the task branch without conflicts or resets. Oracle continues to feed the
existing `ProjectOverrides` effective/generated maps through `RewriteEndpoint`;
no alternate compatibility implementation was introduced.

Additional Oracle command cases prove compatible Easy Connect/descriptor merges,
already-adapted consumers, conflicting remote/unmapped/service/options values,
discovery-order independence and authoritative shared-secret precedence.

- `dotnet test cli/agentic-box.slnx -c Debug --filter 'FullyQualifiedName~UserSecretsOracleCommandTests|FullyQualifiedName~UserSecretsSharedStoreCommandTests'`:
  **90 passed**, no skips/failures (**66 Oracle**, **24 shared-store**).
- `dotnet build cli/agentic-box.slnx -c Debug --no-restore`:
  **0 warnings, 0 errors**.
- `dotnet test cli/agentic-box.slnx -c Debug --no-build`:
  **1076 passed, 7 explicit platform skips, 0 failures** (**1083 total**).
- `dotnet csharpier check cli` and `git diff --check`: passed.

No Release build/tests or independent live Oracle/sandbox run were performed.
The final integration orchestrator owns Release verification and PR/merge work.
