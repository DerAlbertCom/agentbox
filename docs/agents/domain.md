# Domain docs

## Layout and reading rules

This is a single-context repository.

Before exploring, read the root `GLOSSARY.md` if present and relevant ADRs
under `docs/adr/`. If a root `GLOSSARY-MAP.md` is introduced, follow its
pointers to the relevant context glossaries and ADRs.

Missing glossary or ADR files do not block work. Proceed silently;
create domain documents lazily through domain-modeling when terminology
or decisions are resolved.

Existing project terminology and design decisions are documented in
[docs/concept.md](../concept.md). Read [docs/implementation.md](../implementation.md) for current implementation
status and [docs/authoring.md](../authoring.md) when working on squads, kits or workloads.

## Vocabulary and decisions

Use glossary vocabulary in tickets, designs, tests and explanations.
When a needed concept is absent, distinguish an existing synonym from
a genuine domain-model gap.

Surface conflicts with existing ADRs explicitly, identifying the ADR
and the reason to reconsider it.
