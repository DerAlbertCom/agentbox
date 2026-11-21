# Issue tracker: GitHub

Issues and specs live in DerAlbertCom/agentbox on GitHub. Use the `gh` CLI,
explicitly targeting this repository for writes.

## Operations

- Read the full issue body and comments before acting:
  `gh issue view <number> --json number,title,body,labels,comments`.
- List issues with `gh issue list --state open --json number,title,body,labels,comments`,
  adding `--label` and `--state` filters as needed.
- Publish one GitHub issue per approved ticket: `gh issue create --title "..." --body "..."`
  (use a heredoc for multi-line bodies).
- Link child tickets using GitHub's native sub-issue relationship.
- Record blockers using GitHub's native issue dependencies.
- Create tickets in dependency order so blockers already have identifiers.
- A ticket is startable when all its blockers are closed.
- If native relationships are unavailable, reference the parent (`Part of #<parent>`)
  and blockers (`Blocked by: #<n>, #<n>`) at the top of the child issue body.
- Preserve parent issue content and status when publishing child tickets.
- Comment: `gh issue comment <number> --body "..."`.
- Labels: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`.
- Close: post the explanation with `gh issue comment`, then `gh issue close <number>`.
  On an already-closed issue, `gh issue close --comment` drops the comment and still exits 0.

For sub-issues, use `gh issue create --parent` or
`gh issue edit --add-sub-issue` when supported. Otherwise POST to
`repos/DerAlbertCom/agentbox/issues/<parent>/sub_issues`
with `sub_issue_id=<child database id>` using `gh api`.

For blockers, use `--blocked-by` or `--add-blocked-by` when supported.
Otherwise POST to
`repos/DerAlbertCom/agentbox/issues/<child>/dependencies/blocked_by`
with `issue_id=<blocker database id>` using `gh api`.

Count open blockers with
`gh api repos/DerAlbertCom/agentbox/issues/<n> --jq .issue_dependencies_summary.blocked_by`
(0 means startable). `gh issue view <n> --json blockedBy` also lists closed
blockers, so filter it: `--jq '[.blockedBy.nodes[] | select(.state == "OPEN")] | length'`.

Database IDs come from `gh api repos/DerAlbertCom/agentbox/issues/<number> --jq .id`; they are distinct from issue numbers and node IDs.

File issues only into DerAlbertCom/agentbox. Never file into `mattpocock/skills`.
Before the first `gh issue create` of a session, confirm the target with
`gh repo view --json nameWithOwner`.

## Pull requests as a triage surface

PRs as a request surface: no. _(Set to `yes` if this repo treats external PRs
as feature requests; `/triage` reads this flag.)_

When set to `yes`, PRs run through the same labels and states as issues, using
the `gh pr` equivalents (`gh pr view`, `gh pr view --comments`, `gh pr diff`,
`gh pr comment`, `gh pr edit --add-label`/`--remove-label`, `gh pr close`).
GitHub shares one number space across issues and PRs, so resolve a bare `#42`
with `gh pr view 42` and fall back to `gh issue view 42`.

## When a skill says "publish to the issue tracker"

Create a GitHub issue.

## When a skill says "fetch the relevant ticket"

Read it as described under Operations.

## Wayfinding operations

Used by `/wayfinder`. The **map** is a single issue with **child** issues as tickets.

- **Map**: a single issue labelled `wayfinder:map`, holding the Notes /
  Decisions-so-far / Fog body. `gh issue create --label wayfinder:map`.
- **Child ticket**: an issue linked to the map as a GitHub sub-issue. Where
  sub-issues aren't enabled, add the child to a task list in the map body and
  put `Part of #<map>` at the top of the child body. Labels:
  `wayfinder:<type>` (`research`/`prototype`/`grilling`/`task`). Once claimed,
  the ticket is assigned to the driving dev.
- **Blocking**: as described under Operations.
- **Frontier query**: list the map's open children, drop any with an open
  blocker or an assignee; first in map order wins.
- **Claim**: `gh issue edit <n> --add-assignee @me`, the session's first write.
- **Resolve**: `gh issue comment <n> --body "<answer>"`, then
  `gh issue close <n>`, then append a context pointer (gist + link) to the
  map's Decisions-so-far.
