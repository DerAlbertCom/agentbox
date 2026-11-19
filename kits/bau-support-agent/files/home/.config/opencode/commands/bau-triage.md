---
description: Triage a BAU Task with the bau-support-agent (cheap model) and run the deep analysis only if triage says it is needed. A shortcut into the bau-task-analysis skill with the ticket ID given up front.
agent: build
---

Work item ID: `$ARGUMENTS` (a bare number, e.g. `38664`). If `$ARGUMENTS` is empty or not a
number, ask the user for a ticket ID before doing anything else — do not guess one.

Load the `bau-task-analysis` skill (via the `skill` tool) and run it for this ticket.

Its step 0 *is* the triage stage: it detects `~/bau-support-agent/agent.py`, runs
`python3 agent.py $ARGUMENTS --triage-only`, presents the result, and goes on to the
deep-analysis steps only when the triage stage sets `needs_deep_analysis: true`. Everything
this command used to spell out inline now lives there, so that plain prose ("analysiere
Ticket 38664") and this command take exactly the same path — which is the point, since a
command can only ever be triggered by the user typing it, never by the model.

Do not run `agent.py` yourself before loading the skill: the skill would then run the triage
stage a second time.

If the `skill` tool does not list `bau-task-analysis`, the kit's skill file did not land
(it ships at `~/.config/opencode/skills/bau-task-analysis/`). Fall back to
`cd ~/bau-support-agent && python3 agent.py $ARGUMENTS --triage-only`, report the triage
result only, and say that the deep analysis is unavailable until the sandbox is recreated
(`abox create --team mti-ca --squad bau` from the workspace on the host).
Do not improvise the deep-analysis stage from this file.
