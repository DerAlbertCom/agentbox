# User Secrets selection and lifecycle test organization (#34)

## Scope

Selection/filtering, store-prefix expansion, preparation/copy snapshots, deny-list
safety, force accounting and create/run lifecycle tests now live under
`cli/test/Abox.Tests/UserSecrets/`, beside the shared feature they protect. The
existing `UserSecretsCliHarness` moved there unchanged; behavior-specific fixtures
remain private to their suites. All 76 test methods in this batch have explicit
Arrange, Act and Assert markers, including repeated phases for multi-step cases.

Namespaces, class/method names, theory inputs and synthetic filesystem/tool seams
are preserved. Partial classes retain compatibility with command-only cases under
`Features/UserSecrets/` (#32), the two endpoint-generation lifecycle cases (#35)
and the receiver/process safety cases (#36). This batch does not reorganize those
endpoint/adapter cases or change production behavior or public testing APIs.

## Preservation checks

- Baseline: integration `8df050820f6a2478ac1bd6bf6d542547838db3ed`.
- Integrated command organization from `integration/audit-21` at
  `3f18ac16f7d9b28b53ba55247ff19682b3853be2` before final verification.
- Executable xUnit discovery with `-preEnumerateTheories -list full/json` found
  **1,564 cases before and after**, with zero differences in IDs, class/method
  identities, display names, explicit flags or skip metadata.
- Syntax-token comparison of all 121 methods (including shared/private helpers)
  in the affected original suites found zero differences after ignoring comments,
  whitespace and the mechanical extraction of immediately asserted awaited results.
  Original assertions, callbacks, theory data and skip branches remain intact.
- Full-run XML comparison found the same seven skipped case identities and reasons.

## Verification

The solution was restored and built serially in Debug with zero warnings/errors.
Tests ran through `cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.exe`, not a
discovery-only `dotnet test` invocation.

| Check | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| Baseline full Debug | 1,557 | 7 | 0 |
| Focused selection/copy/filtering/lifecycle/safety/store-prefix classes | 252 | 5 | 0 |
| Final full Debug | 1,557 | 7 | 0 |

Focused execution included all of `UserSecretsCommandTests`, including the
command-specific partial, and all remaining adapter cases in
`UserSecretsSafetyTests`. The five focused skips are four native symlink receiver
rows and the case-distinct host-store directory case. The full suite additionally
skips the two native project-file symlink rows. They are platform limitations, not
passing coverage.

`dotnet csharpier check cli` and `git diff --check` passed. No live sandbox, host
secret store, registry publication or production service was used. No Release
verification was performed for this test-only batch.
