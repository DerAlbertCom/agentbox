# cape-bau-support-agent

First-response AI agent for CaRMen BAU Tasks in ADO (RWEST-MFI-AE / CS).

Built with [Strands Agents](https://strandsagents.com). Modelled after the
[AI-Azure-Agent](https://dev.azure.com/RWEST-MTI-TM/MarketDataIT/_git/AI-Azure-Agent)
reference implementation by the MTI team.

## What it does

Two run modes, both triggered from the same `agent.py` CLI:

1. **Triage-only** (`--triage-only`, recommended): a narrow, read-only agent
   fetches the ticket, extracts identifiers/stakeholders, classifies it
   against an 8-class taxonomy, and decides `needs_deep_analysis` — printing
   only the raw JSON result to stdout (no ADO write). Called as an external
   process by the `bau-task-analysis` skill's step 0, which parses the JSON
   and continues into that skill's deep-analysis stage only when the flag is
   set.
2. **Legacy single-stage** (no flag, original flow): when a **BAU Task** work
   item is created in ADO, the agent:
   1. Fetches the ticket and extracts the DQC subscription name from the title
   2. Looks up the matching resolution page in the CS Wiki (`Wiki` repo, `/BAU-handbook/CARE/DQCs/`)
   3. Extracts the affected data rows from the ticket description table
   4. Posts a structured first-response comment with issue summary, wiki resolution steps, and verification link

## Structure

```
cape-bau-support-agent/
  ado_client.py       — AzureDevOpsClient: work items, comments, Wiki Git repo access
  tools/
    ado_tools.py      — @tool fetch_ado_ticket, add_comment_to_ticket
    wiki_tools.py     — @tool get_wiki_page_for_dqc, get_wiki_page, search_wiki
  prompts.py          — System prompt (BAU/DQC-focused)
  agent.py            — Agent assembly + CLI entry point
```

## Usage

**From OpenCode (recommended):** just ask in prose — "analyse BAU ticket 38512".
The `bau-task-analysis` skill (shipped with this kit at
`~/.config/opencode/skills/bau-task-analysis/`) runs the triage stage as its step 0:
it detects this agent, runs the triage stage
below, presents the result, and continues to its deep-analysis stage only when
the triage stage sets `needs_deep_analysis: true`. `/bau-triage <work-item-id>`
is the same path with the ID given up front; see
`~/.config/opencode/commands/bau-triage.md`.

The routing deliberately lives in the skill rather than in the command: a
command only fires when the user types `/bau-triage`, so the model has no way to
reach it on its own, and a prose request would otherwise land straight in the
deep-analysis stage and skip triage entirely.

**Direct CLI:**

```bash
# AZURE_DEVOPS_EXT_PAT and AWS_BEARER_TOKEN_BEDROCK are proxy placeholders.
# Configure their real values only on the host with abox secrets.

# Two-stage pipeline (recommended): triage only, prints JSON to stdout
python3 agent.py 38512 --triage-only

# Legacy single-stage: fetch, classify, wiki lookup, format+optionally post
python3 agent.py 38512
```

## Prerequisites

The kit installs Strands, requests, pydantic-settings, Presidio and the English
and German spaCy models at sandbox creation. No additional install is needed.

Model provider: `BedrockModel` (default — `eu-central-1` cross-region
inference profile, see `schemas.Settings.bedrock_model_id`/`aws_region`) or
`github_copilot` (legacy/dev only, currently broken — GitHub Copilot's API
rejects raw PATs; see `agent.py`'s `_build_model()` docstring for the full
story). Switch via `Settings.model_provider` (env var `MODEL_PROVIDER`).

## Wiki access

The CS Wiki is stored as a Git repo named `Wiki` in RWEST-MFI-AE/CS.
The agent reads pages via the ADO Git Items API — no `vso.wiki` PAT scope required,
only `vso.code` (read).

DQC resolution pages live at:
```
/BAU-handbook/CARE/DQCs/<DQC-name>.md
```
