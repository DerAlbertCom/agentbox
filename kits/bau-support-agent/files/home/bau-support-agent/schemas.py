"""
schemas.py — Pydantic settings for the CAPE BAU Support Agent.

Simplified version of AI-Azure-Agent/src/schemas.py — AWS/Bedrock/MIX fields omitted,
TicketData schema not included (we pass raw dicts to the model instead).

Settings are loaded from environment variables and an optional .env file.
All ADO credentials come from environment variables — never hardcode them.
"""
from __future__ import annotations

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    # ------------------------------------------------------------------
    # Azure DevOps — connection
    # ------------------------------------------------------------------
    ado_pat: str = Field(validation_alias="AZURE_DEVOPS_EXT_PAT")
    ado_org: str = "RWEST-MFI-AE"
    ado_project: str = "CS"

    # ------------------------------------------------------------------
    # Azure DevOps — write guards
    # All default to False — the agent must be explicitly enabled to write.
    # ------------------------------------------------------------------
    ado_writes_enabled: bool = False
    ado_comments_write_enabled: bool = False
    ado_tags_write_enabled: bool = False

    # ------------------------------------------------------------------
    # LLM — model provider selection
    # "bedrock" (default): BedrockModel against an eu-central-1 cross-region
    # inference profile, authenticated via ambient AWS credentials (the
    # sandbox's 'aws' kit + cs-nonprod SSO session, or any boto3-discoverable
    # credentials locally) — no token to manage.
    # "github_copilot" (legacy/dev, currently BROKEN — kept only in case a
    # working Copilot OAuth token becomes available later): GitHub Copilot's
    # OpenAI-compatible endpoint categorically rejects raw PATs for its Chat
    # Completions API (401) — see the 2026-09-07 bau-triage handoff. The
    # copilot_internal/v2/token exchange this would require is also blocked
    # by the sandbox's egress allowlist (api.github.com not reachable).
    # ------------------------------------------------------------------
    model_provider: str = Field(default="bedrock")

    # Cross-region inference profile ID (not a bare foundation-model ID) —
    # required for on-demand throughput on these models in eu-central-1.
    # Haiku, not a reasoning (o-series/opus) model: the triage stage is a
    # read-and-lookup problem (long noisy HTML, strict instruction following,
    # tool budget discipline), not a reasoning problem. Reasoning models burn
    # budget here and risk inventing causal chains when evidence is thin — see
    # the #39082 worked example that motivated the two-stage split.
    bedrock_model_id: str = Field(default="eu.anthropic.claude-haiku-4-5-20251001-v1:0")
    aws_region: str = Field(default="eu-central-1")

    # ------------------------------------------------------------------
    # LLM — GitHub Copilot (OpenAI-compatible), legacy/dev only — see above.
    # In der Sandbox wird GITHUB_COPILOT_TOKEN als Custom Secret injiziert
    # (GHE_PAT_CLASSIC aus PAT_VAULT_MAURICIO_RO_KIT, klassisches PAT mit
    # Copilot-Chat-Scope). Lokal: .env mit GITHUB_COPILOT_TOKEN setzen.
    # ------------------------------------------------------------------
    github_copilot_token: str = Field(
        default="",
        validation_alias="GITHUB_COPILOT_TOKEN",
    )
    github_copilot_model: str = Field(default="gpt-4.1")

    # ------------------------------------------------------------------
    # Agent behaviour
    # ------------------------------------------------------------------
    comments_limit: int = Field(default=5, ge=1)
