# User-secret store-prefix regression verification

## Report and red evidence

The user configured `{"squad":"ac","userSecrets":["Cape.*"]}` and reported
`user-secrets --location D:\dev\rwe` failing with `Unsafe user-secret ID`.
The required behavior is copying local stores starting with the literal `Cape.`
prefix, without restoring on-demand squad-record verification removed in `632698c`.

The regression uses the real composed CLI and shared copy flow, disposable
synthetic host stores (`Cape.Position`, `Cape.Deals`, and malformed nonmatches),
and fake sbx. It never invokes the user's command against their folder, reads
actual host user secrets, or contacts a live sandbox.

Before production edits, this command ran one test and failed in about one second:

```sh
dotnet test --project cli/test/Abox.Tests -- --filter-method '*CapePrefixCopiesEveryMatchingHostStoreIntoTheExistingSandbox*'
```

```text
Expected (fixture.Run()) to be 0 because error: Unsafe user-secret ID. Use 1–128 ASCII letters, digits, dots, underscores or hyphens, beginning with a letter or digit; no paths or reserved device names., but found 1 (difference of 1).
```

The first attempted invocation used unsupported VSTest-style filter flags and ran
zero tests; that is not the regression evidence. The command above uses this
repository's Microsoft.Testing.Platform/xUnit filtering syntax.

After the failing loop existed, ranked falsifiable hypotheses were shown before
inspection: configuration validates the pattern as an actual ID; preparation
accepts it but loads a literal directory; or a pattern-aware lifecycle selection
exists but is bypassed on demand. Inspection confirmed the first: parsing called
`UserSecretId.Validate` on every entry, and no expansion existed. The same command
then passed **1 test**, asserting exactly the two matching actual IDs in sorted
order and no creation or value-bearing output.

## Implemented contract

- Only a safe nonempty literal prefix plus one terminal `*` is new. `Cape.*`
  matches the literal `Cape.` prefix, ordinal case-insensitively. No bare star,
  embedded/repeated star, path, separator or arbitrary filesystem glob is allowed.
- Enumeration inspects only direct store directories under the existing
  APPDATA-preferred/HOME-fallback .NET root. It never walks projects or recursively
  searches for stores. Only selected files are read.
- Configuration patterns and actual expanded IDs are validated separately.
  Actual IDs retain portable ASCII/length/device-name/trailing-dot restrictions,
  including independent validation by the sbx adapter. Matching unsafe names or
  ambiguous case-distinct host directories fail, rather than silently dropping one.
- Entries retain declaration order; matches within each entry sort by ordinal
  case-insensitive ID, with ordinal tie breaking. Actual host spelling is retained.
  With prefixes present, exact selections resolve that spelling case-insensitively;
  exact-only requests retain host-native lookup. Each actual ID is copied once at
  its first occurrence. Overlapping key filters union; whole-store selection wins.
  Identical/case-variant selection strings remain configuration errors.
- No-match prefixes, including an absent host root, select no stores and write
  nothing. Missing exact IDs and genuine enumeration/permission failures still fail.
  A matched directory missing `secrets.json` is an incomplete selected store, not a
  no-match: it fails all preparation before sandbox calls, including with force.
- Shared preparation expands before host snapshots and original-value deny-list
  checks, so on-demand and local/published/accepted-run replacement copying agree.
  Automatic copying never forces denied values. On-demand force still bypasses
  only matched deny policy, not malformed host input, policy metadata or paths.
- A secondary red probe found host directory links were previously followed.
  Four synthetic exact/prefix + root/store junction cases wrongly returned success.
  Host path checks reject symlinks/reparse points in the user-secrets root itself,
  selected store directories and secret files before reads; the four junction cases
  pass. The initial implementation also rejected linked ancestors above the root;
  the review correction below removes that unintended profile-layout restriction.
  Destination safety, sensitive transfer and effective network authorization are
  unchanged. No on-demand squad/record gate was reintroduced.

See [the full selection semantics](../user-secrets-filtering.md).

## Initial implementation verification

Focused command:

```sh
dotnet test --project cli/test/Abox.Tests -- --filter-class '*UserSecretsStorePatternTests' --filter-class '*UserSecretsLifecycleTests' --filter-class '*UserSecretsCommandTests' --filter-class '*UserSecretsSafetyTests'
```

Result: **188 passed, 5 explicitly skipped, 193 total, zero failures**.
Coverage includes multiple matches/nonmatches, literal dots, case-insensitive
matching, deterministic expansion, exact/prefix overlaps and key unions,
whole-store precedence, empty suffix and the 128-character limit, no-match/missing
root behavior, missing exact IDs, duplicate selections, invalid/path-like patterns,
unsafe/overlength host names, malformed stores, host path links, deny/force and
automatic local/published/accepted-run copying. Effective policy allow/deny and
malformed-check tests delegate policy discovery to the real sbx adapter with a fake
process runner, verifying that expanded IDs cannot bypass its authorization checks.

Final verification:

- `dotnet test --solution cli/agentic-box.slnx`: **847 passed, 5 explicitly skipped,
  852 total, zero failures** (about 30 seconds).
- `dotnet build cli/agentic-box.slnx --no-restore`: **zero warnings/errors**.
- `dotnet build cli/agentic-box.slnx --no-restore -c Release`: **zero warnings/errors**.
- `dotnet tool run csharpier check cli`: **160 files checked**.
- `git diff --check`: passed.

Release tests were not run; the full test result above is Debug.

## Independent-review follow-up

The independent review found that checking every ancestor to the drive/filesystem
root regressed ordinary exact-ID copying for legitimate linked user profiles and
system layouts such as `/home` or macOS `/var`. The synthetic CLI reproducer adds
a linked APPDATA profile above an ordinary user-secrets root, for both exact IDs
and prefixes. Before production edits:

```sh
dotnet test --project cli/test/Abox.Tests -- --filter-method '*LinkedProfileAncestorsAreAllowedForExactAndPrefixSelections*'
```

Both cases failed with:

```text
Expected (fixture.Run()) to be 0 because error: Host user-secret paths must not contain a symbolic link or reparse point; no stores were copied., but found 1 (difference of 1).
```

Host checks now inspect only the trusted user-secrets root itself and its selected
store directory/file. Ancestors above the root may be linked. The same two
acceptance cases plus the four existing at-root/below-root junction rejection
cases pass (**6 passed**, zero skips/failures).

Additional regressions explicitly cover incomplete matched directories failing
before sandbox access, with and without force, and heterogeneous declaration order
remaining unchanged when a later overlap unions an earlier store's key selection.
Expansion now uses an explicit ordered list plus an ID-to-index lookup; it no longer
relies on dictionary iteration order.

The documented casing distinction remains unchanged: exact-only requests retain
host-native lookup, while requests containing prefixes canonicalize exact entries
against host IDs case-insensitively. Removing it would require separate host-native
exact-file resolution without disrupting case-insensitive overlap deduplication;
this follow-up does not add that behavior change.

Follow-up focused verification:

```sh
dotnet test --project cli/test/Abox.Tests -- --filter-class '*UserSecrets*'
```

Result: **357 passed, 5 explicitly skipped, 362 total, zero failures**.
The original `Cape.*` regression and both new linked-profile cases also pass in
isolation (**3 passed**, zero skips/failures).

Follow-up full verification:

- `dotnet test --solution cli/agentic-box.slnx`: **852 passed, 5 explicitly skipped,
  857 total, zero failures** (about 30 seconds).
- Debug and Release solution builds: **zero warnings/errors**.
- `dotnet tool run csharpier check cli`: **160 files checked**.
- `git diff --check`: passed.

Release tests and independent Unix-host/live-sbx runs were not performed. Skip
reasons remain the same five host limitations described below; all six junction
acceptance/rejection cases pass on this Windows host.

## Limitations

This is native Windows-host synthetic verification, not a new live-sbx,
independent Unix-host or actual user-secret run. Four existing receiver symlink
tests explicitly skip because this host lacks native symlink privilege; the new
case-collision test explicitly skips because its filesystem cannot create
case-distinct directories. None is claimed as passing. The four new host-path
junction rejection tests and two linked-profile acceptance tests run without
symlink privilege and pass. Unix layout fixtures on
Windows are not independent Unix-host execution evidence.

Concurrent malicious filesystem changes between checking and reading remain
outside the existing contract; the new host checks do not claim a race-free
filesystem snapshot. No production-secret reads, host-secret mutations, sandbox
changes, registry operations or persistent sbx settings changes were needed.
The user's uncommitted `run-abox.ps1` is untouched and excluded from this fix.
