# Selecting .NET user-secret keys

`abox user-secrets [--location <folder>] [--force]` reads `abox.json` in the
sandbox folder and copies explicitly selected host stores into that folder's
**existing** local or published sandbox. It never creates or replaces a sandbox.
Filtering uses the shared prepare/copy flow; there is no separate transport or
safety path for filtered stores.

```json
{
  "userSecrets": [
    "whole-store-id",
    "Cape.*",
    { "id": "another-whole-store-id" },
    {
      "id": "filtered-store-id",
      "keys": ["ConnectionStrings:*", "Keycloak:*", "Feature:Enabled"]
    }
  ]
}
```

Only selected stores are read. Plain IDs and objects can be mixed. Objects accept
only `id` and optional `keys`, with no duplicate properties. Identical ID selection
strings may occur only once, case-insensitively, across both entry forms. A malformed
entry fails the entire preparation, even with `--force`.

## Store-ID prefix selections

An ID selection can be an exact safe ID or a nonempty safe literal prefix plus
one terminal `*`. For example, `Cape.*` selects `Cape.Deals` and `cape.Position`,
but not `CapeOther` or `Other.Cape.Deals`: the dot is literal. Both string entries
and objects such as `{"id":"Cape.*","keys":["ConnectionStrings:*"]}` support this.

- Prefix matching is ordinal and case-insensitive, consistent with existing ID
  duplicate checks. The prefix contains 1–128 ASCII letters/digits/dots/underscores/
  hyphens, beginning with a letter/digit. A trailing dot is allowed in a **prefix**,
  not an actual ID. Reserved device-name prefixes such as `CON.*` are rejected.
- Only that one final `*` is supported. Bare `*`, embedded/repeated stars, `?`,
  brackets, paths, separators, regexes and escape syntax are not supported.
  No trimming or arbitrary filesystem glob evaluation occurs.
- Enumeration visits only direct store directories under the effective host
  APPDATA/HOME .NET root, never project files or nested directories. Unrelated
  invalid names/content are not selected or read. Matching actual IDs must still
  pass strict portable ID validation (including the 128-character limit and no
  trailing dots/device names). Matching unsafe IDs, linked paths or case-colliding
  actual IDs fail the entire preparation before sandbox calls; force cannot bypass
  this. The host user-secrets root itself and selected store directories/files
  reject symlinks/reparse points. Linked ancestors above that trusted root, such as
  a linked profile or `/home`/`/var`, are allowed. Concurrent malicious filesystem
  changes remain outside the contract.
- A no-match prefix (including an absent host root) selects no stores and writes
  nothing. It does not broaden selection or clear any destination. An exact ID
  still selects a store when no host directory exists. A readable host file is
  required for copy-only stores; an associated project's standard appsettings can
  now supply configuration when the host file is missing. A matched store
  directory without `secrets.json` is selected, not silently skipped as a no-match;
  without that project source it fails all preparation, even with `--force`.
  Enumeration/permission errors fail, never silently become no matches.
- Selection entries are processed in declaration order; each prefix expands in
  ordinal case-insensitive ID order, with actual host spelling retained. When
  prefixes are present, an exact selection also resolves to the corresponding
  actual host spelling case-insensitively. Exact-only requests retain host-native
  filename lookup. Ambiguous case-distinct host directories fail, not pick one.
- Overlapping exact/prefix selections copy each actual ID once at its first
  occurrence. Their key selections form a union; any whole-store selection wins.
  Identical/case-variant selection strings remain duplicate configuration errors.

The shared preparation applies these rules on demand and during local/published
creation or accepted `run` replacement. Ordinary `run` still does not resync stores;
on-demand copying still does not require or verify a sandbox squad record.

## Key-pattern semantics

Patterns match **effective .NET configuration keys**, not JSON property segments
or secret values. For example, `{"ConnectionStrings":{"Db":"..."}}` has the
effective key `ConnectionStrings:Db`. An array item can have a key such as
`Services:2:Token`.

| Selection | Meaning |
| --- | --- |
| Plain ID, or object with `keys` omitted | All keys in that store. |
| `"keys": []` | No keys. Never falls back to copying the whole store. |
| `"keys": ["Feature:Enabled"]` | Exact full-key match, ignoring case. |
| `"keys": ["ConnectionStrings:*"]` | All keys starting with that prefix, including deeper nested keys. |
| `"keys": ["*"]` | All effective keys explicitly selected. |
| Patterns with no matches | No keys, just like an empty list. |

- Matching is ordinal, case-insensitive and independent of the host culture.
- `*` matches zero or more characters, **including `:`**. It may appear anywhere
  and more than once. Matching is anchored to the entire effective key.
- No other wildcard, regex or escape syntax exists: `?`, `[`, `]`, `.`, and `\`
  are literal characters. There is no syntax for matching a literal `*` alone.
- `ConnectionStrings` by itself does **not** select its descendants. Use
  `ConnectionStrings:*` for them.
- Patterns are combined as a union. Overlapping, repeated or case-variant patterns
  copy a matching key once. Pattern order does not change the result; selected
  keys follow the host JSON's traversal order.
- Each pattern must be a nonempty string, not whitespace-only, with no control
  characters. Other whitespace is literal, not trimmed. `"keys": null`, a string
  instead of a list, or non-string list items are errors, never whole-store copy
  requests.

## Output, validation and retry

Filtered output is a JSON object with flat effective key names. A selected
`Services:2:Token` stays exactly that key: filtering out indices 0 and 1 cannot
renumber it to index 0. Selected key spelling, string content, numeric values,
booleans, nulls and empty containers are preserved. Plain IDs and objects without
`keys` retain the whole store's JSON structure. Comments are not configuration
values and are not copied as secret content.

All present selected host files must still be readable, valid UTF-8/JSON objects. Duplicate
effective .NET keys (including case-only duplicates, nested/flat collisions and
array-index collisions) fail validation **before filtering**, even if neither
duplicate would be selected. Filtering and force cannot bypass malformed input.

The global YAML `userSecretsDenyList` checks only **selected original scalar
values**, using case-insensitive literal substrings. A denied unmatched value
does not block a safe selection. A denied selected value stops preparation for
all stores before any destination call/write, identifying its store and key but
not its value. `--force` overrides only that policy, not validation or transport
errors. Host files are never modified.

The same patterns govern [appsettings-derived endpoint overrides](user-secrets-appsettings.md).
Selected host keys take precedence case-insensitively, including null and values
needing no rewrite. Excluded keys cannot reappear through generation. Appsettings
files are validated before filtering; only selected originals that would generate
an override undergo the deny check during all-store target preparation. This can
follow runtime policy discovery, but always precedes destination writes.

Every selected store replaces its entire destination file through the existing
sensitive-stdin, validated atomic-replacement transport. An empty selected result
therefore **writes `{}`**, rather than skipping the file: retrying after a broader
copy removes every previously copied unmatched key. The command reports each
store's copied key count, including `0 keys` for this empty-file replacement, and
never prints values, even with `--verbose`.

Atomic replacement is per file, not transactional across stores. A transport
failure can leave earlier stores replaced; fix the destination/runtime and retry
`abox user-secrets`. There is no cross-store rollback. Payloads remain runtime-only,
not image/build/publication inputs; existing `abox secrets` behavior is unchanged.
Address rewriting, appsettings generation and automatic create/replacement use
these same selections through the shared copy flow.
