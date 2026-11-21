# Team-qualified local builds (#26)

## Contract and synthetic coverage

Local storage is `<state dir>/builds/<team>/<squad>/<tag>/`. The existing
`BuildManifest.Reference` remains the explicit ownership marker; it is not a
published reference. No project-root storage dimension or artifact migration is
introduced.

Tests exercise the existing build, sandbox-creation, publication, run and
build-store interfaces using temporary sources/state and fake Docker/sbx adapters:

- Both sequential team orders and both multi-team orders preserve generated
  workload/state sources, manifests, fingerprints and unique runtime/image
  selections for the same squad/tag, including metadata-only preparation.
- Metadata-only preparation invalidates only the selected team's image pointer;
  another team's build and existing legacy/runtime artifacts remain unchanged.
- Local creation and publication select each team's own generated sources and
  metadata. Published repository/reference naming and fingerprint validation stay
  unchanged. Publication also works with explicitly nonpersonal metadata-only
  builds.
- Run selects replacement builds and create hints by recorded team. Another
  team's newer build, unknown legacy ownership or invalid qualified ownership
  cannot offer replacement or interrupt ordinary launch. Owned legacy builds can
  still replace; teamless legacy hints recover only explicit legacy provenance.
- Legacy reads require matching squad/tag/ownership. Missing, mismatched,
  malformed or duplicate ownership metadata fails with a rebuild instruction.
  Invalid qualified metadata never silently falls back to legacy artifacts.
  Successful compatibility reads/publication/creation do not migrate manifests.
- Registry-tag validation protects qualified reads/writes and legacy hint reads
  before path access. Existing configured-default and command-preflight
  regressions remain included in focused verification.

## Red and green evidence

Before implementation, all four sequential shared-squad cases failed because
the second build overwrote the first team's generated workload recipe. A later
legacy regression failed because deserialization accepted the last of conflicting
duplicate ownership properties. Requested-team rebuild diagnostics also failed
for unknown legacy ownership before correction.

After merging integration through
`8df050820f6a2478ac1bd6bf6d542547838db3ed` (including #25, #27, #29 and #31):

- Debug build: zero warnings/errors.
- Focused executable xUnit run: **416 passed, zero failed/skipped** (team build,
  creation, publication, run, tag safety, owning-root discovery, state safety and
  runtime guidance).
- Whole Debug executable xUnit run: **1604 passed, 7 skipped, zero failed**
  (**1611 total**).
- `dotnet csharpier check abox-cli` and `git diff --check`: passed.

The seven existing Windows-host limitations are four receiver-symlink cases,
two linked-project cases and one case-distinct-directory case. They are skips,
not passing cross-platform evidence. Tests ran via
`abox-cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.exe`, not `dotnet test`.
No live sandbox/registry operation or real host-secret access was used. Release
verification was not run for this ticket.
