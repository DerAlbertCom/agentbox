# Issue tracker: GitHub

Issues and specs live in DerAlbertCom/agentbox on GitHub. Use the `gh` CLI,
explicitly targeting this repository for writes.

## Operations

- Read the full issue body and comments before acting.
- Publish one GitHub issue per approved ticket.
- Link child tickets using GitHub's native sub-issue relationship.
- Record blockers using GitHub's native issue dependencies.
- Create tickets in dependency order so blockers already have identifiers.
- A ticket is startable when all its blockers are closed.
- If native relationships are unavailable, reference the parent and blockers
  in the child issue body.
- Preserve parent issue content and status when publishing child tickets.

For sub-issues, use `gh issue create --parent` or
`gh issue edit --add-sub-issue` when supported. Otherwise POST to
`repos/DerAlbertCom/agentbox/issues/<parent>/sub_issues`
with `sub_issue_id=<child database id>` using `gh api`.

For blockers, use `--blocked-by` or `--add-blocked-by` when supported.
Otherwise POST to
`repos/DerAlbertCom/agentbox/issues/<child>/dependencies/blocked_by`
with `issue_id=<blocker database id>` using `gh api`.

Database IDs come from `gh api repos/DerAlbertCom/agentbox/issues/<number> --jq .id`; they are distinct from issue numbers and node IDs.

## Pull requests as a triage surface

PRs as a request surface: no.
