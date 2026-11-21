# MTI-CA

Ports the nine v2 compositions from `cape-agent-sandbox` into native v3 mixins.
Source snapshot: `f8c2ac4801f29a73da086d42212fd41fca67cdbc`.

| Squad | Base | Additions |
| --- | --- | --- |
| base | — | Workload, agents, Git defaults, RWE GHEC, Azure DevOps auth, Tokyo Night color scheme |
| cape | base | CS defaults, database MCP, Elastic, JFrog, clean-code, host ports |
| ac / bc / cc | cape | Aliases |
| cape-subagents | cape | OpenCode model tiers |
| mod | base | MOD defaults, Context7, dual Elastic, MongoDB, tfswitch, AWS gateway |
| predictopus | base | AssetOpt Renewables defaults, metsca NuGet, Predictopus Elastic |
| bau | base | CS defaults, database/Elastic, Bedrock, BAU app/skill/command |

`abox build` prepares/publishes leaf squads only: ac, bc, cc, cape-subagents,
mod, predictopus and bau. base/cape remain inheritance inputs, not separately
published sets. All additions are unique across each inheritance chain.

## Build

```sh
abox build --team mti-ca --no-personal --metadata-only
abox build --team mti-ca --no-personal --no-verify
```

The first command only prepares metadata; it cannot be used with `create --local`.
The second builds the workload. sbx 0.51 cannot inspect recipe-based local kits,
so these builds require `--no-verify`; creation processes their recipes normally.
Do not publish without a successful image build and an authorized smoke test.

The workload retains all **41 mise tools and exact versions**, including npm
build permissions, Linux architecture mappings/checksums, Azure CLI's pip pin,
and tig's source build. Advanced entries use mise's `--tool-option`/`--postinstall`
flags. abox installs the list in reverse order. .NET remains dotnetup-managed:
manager `0.3.0-preview.1.26554.3`, SDKs `10.0.404` and `11.0.100`, checked after
channel installation. A newer channel release requires reviewing these pins.
MOD's tfswitch seed follows Terraform `1.17.2`, replacing the stale v2 `1.16.6` label.
APT dependencies, Git's PPA, Azure DevOps extension, shell completion and pinned
PowerShell modules are included. The old shell-docker image is digest-pinned.
It supplies agent UID 1000 with passwordless sudo for the workload's system setup.

## Host credentials

Real credentials never belong in kit arguments, payloads or workspace `abox.json`.
`abox secrets` stores each credential under the service ID named by its kit.
The proxy injects headers for that capability's exact hosts. Azure DevOps stores
`base64("pat:<PAT>")` (enter `pat` as its Basic username); JFrog stores
`base64("<username>:<token>")`. Supply the original raw/encoded Elastic API key,
not an extra base64 wrapper. MOD/Predictopus keys need `feature_agentBuilder.read`.
Context7 remains optional. Registry login remains separate from service auth.

The single azure-devops service covers dev.azure.com, pkgs.dev.azure.com and
vssps.dev.azure.com. Its AZURE_DEVOPS_EXT_PAT value inside the sandbox is a
sentinel, not the stored credential. Service IDs and encodings match v2.
These service secrets are separate from placeholder-replacement custom bindings.

The RWE GHEC binding cannot be expressed by abox's single-host, static-secret
schema. Preserve the command-backed custom binding on the host:

```sh
sbx secret set-custom \
  --host copilot-api.rwe.ghe.com --host api.rwe.ghe.com \
  --host rwe.ghe.com --host uploads.rwe.ghe.com \
  --env RWE_GHE_TOKEN --command 'gh auth token -h rwe.ghe.com' \
  --placeholder 'gho_{rand}'
```

Run this only if that exact binding is absent. If an incompatible binding exists,
review it manually; do not delete/rebind another sandbox's credentials blindly.
Host `gh` must already be logged in with the required scopes (including `workflow`
for workflow changes). There is no automatic login/scope refresh or legacy-binding
cleanup. Public GH_TOKEN/GITHUB_TOKEN stay empty. The kit fails setup without a
token-shaped tenant placeholder. It never writes the host token into the image.
Standalone Copilot still needs its separate `/login` inside the sandbox.

## Squad kit parity

| v2 kit(s) | v3 owner |
| --- | --- |
| cao-docker-ai-sandbox | workload + sandbox-agents |
| git-user / rwe-ghec / azure-devops | same-named mixins |
| cape-azure-devops / mod-squad-azure-devops / predictopus-azure-devops | azure-devops-project, parameterized per squad |
| rwe-jfrog / predictopus-jfrog | jfrog, parameterized per squad |
| cape-opencode / mod-squad-opencode / predictopus-opencode | squad-mcp profiles |
| cape-database-mcp / cape-elastic / clean-code-mcp | same-named mixins |
| cape-localhost-ports | host-access-cape (keeps v3 port 8005, restores Rider ports) |
| context7 / mod-squad-elastic / mod-squad-terraform / mod-squad-tools / mod-squad-aws-mcp | same-named mixins |
| predictopus-elastic / opencode-subagents / aws-bedrock / bau-support-agent | same-named mixins |

All 41 old payload files are represented. The three squad OpenCode files become
complete MCP profiles. The base Claude settings are already included in the RWE
settings; rwe-ghec owns that path alone. The base `.claude.json` and OpenCode
defaults are incorporated into the profiles instead of colliding with them.
BAU's Python application, bilingual PII redaction, skill and slash command are
copied rather than rewritten. Its dependency/model downloads remain create-time
operations, as in v2.

`squad-mcp` installs complete agent-native configs for local ADO/clean-code and
remote MCP servers. This preserves commands, PAT environment references, URLs,
Elastic filters, OpenCode code-mode choices, and Claude/Copilot disabled defaults
without unsupported `.abox mcp.command/args/headers` fields. Profile endpoint and
organization values come from squad parameters. A small install hook substitutes
only those nonsecret values; credential and gateway references remain runtime
references. Service hosts must stay aligned with their credential bindings.
Claude/Copilot still cannot enforce Elastic's wildcard tool
allow-list; this is the same v2 limitation, not an authorization boundary.

MOD's AWS gateway is optional; creating the squad needs no host MCP registration.
The agent-native profile retains the runtime MCP_GATEWAY_URL reference. After
host registration, use `sbx mcp load aws-gateway --sandbox <sandbox>` when needed.
Host SSO/proxy provisioning is not performed here. Both AWS profile/account
instructions and `codemode: false` for this gateway are retained.

## Remaining host-orchestration differences

- Git identity uses optional `git-identity@1`; unsupported runtimes skip it.
  Host alias synchronization is not automatic. The old `sbx-sync-git-user` helper
  is retained for explicit use with its GIT_USER_NAME/GIT_USER_EMAIL/GIT_ALIASES_BASE64
  inputs; it is not an implicit host-environment tunnel.
- The eight v2 state directories persist under abox's own state root. Existing
  `~/.cabox/state` contents are not moved automatically.
- Host skill/font synchronization, Copilot credit-budget reapplication and the old
  Herdr TUI attach orchestration are not abox configuration features. Their host
  automation is not ported. TUI helpers/config remain available; use abox's
  `herdr-app` launcher for its supported App/Core flow.
- Open egress is retained from v2 for install/runtime. Credential injection remains
  restricted to each service's exact hosts. Host/organization denies still win.

Descriptor validation uses the upstream v3 spec at
`docker/sandbox-kit-spec@cc033f80d58bb9516db1c26b71a3ce8184100fd0`.
Metadata/schema/syntax checks do not prove image builds, host secret bindings,
gateway availability or end-to-end agent authentication; those need a separately
authorized runtime smoke test.
