# Selecting .NET user-secret keys

`abox user-secrets [--location <folder>] [--force]` reads `abox.json` in the
sandbox folder and copies explicitly named host stores into that folder's
**existing** local or published sandbox. It never creates or replaces a sandbox.
Filtering uses the shared prepare/copy flow; there is no separate transport or
safety path for filtered stores.

```json
{
  "userSecrets": [
    "whole-store-id",
    { "id": "another-whole-store-id" },
    {
      "id": "filtered-store-id",
      "keys": ["ConnectionStrings:*", "Keycloak:*", "Feature:Enabled"]
    }
  ]
}
```

Only named stores are read. Plain IDs and objects can be mixed. Objects accept
only `id` and optional `keys`, with no duplicate properties. IDs retain the
existing safe-ID rules and may occur only once, case-insensitively, across both
entry forms. A malformed entry fails the entire preparation, even with `--force`.

## Pattern semantics

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

All named host files must still be readable, valid UTF-8/JSON objects. Duplicate
effective .NET keys (including case-only duplicates, nested/flat collisions and
array-index collisions) fail validation **before filtering**, even if neither
duplicate would be selected. Filtering and force cannot bypass malformed input.

The global YAML `userSecretsDenyList` checks only **selected original scalar
values**, using case-insensitive literal substrings. A denied unmatched value
does not block a safe selection. A denied selected value stops preparation for
all stores before any destination call/write, identifying its store and key but
not its value. `--force` overrides only that policy, not validation or transport
errors. Host files are never modified.

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
Address rewriting and automatic create/replacement integration are separate
follow-on work, not provided by filtering itself.
