"""
ado_client.py — Azure DevOps REST API client for the CAPE BAU Support Agent.

Modelled after AI-Azure-Agent/src/ado_client.py (RWEST-MTI-TM/MarketDataIT).
Authentication: Basic Auth via Settings.ado_pat.
"""
from __future__ import annotations

import base64
import re
from typing import Any

import requests
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry

from schemas import Settings


class AzureDevOpsClient:
    """Thin ADO REST client — work items, comments, Git repo files."""

    API_VERSION = "7.1-preview.3"
    COMMENTS_API_VERSION = "7.1-preview.4"

    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        self._base_url = (
            f"https://dev.azure.com/{settings.ado_org.strip()}/{settings.ado_project.strip()}"
        )
        token = base64.b64encode(f":{settings.ado_pat.strip()}".encode()).decode()
        headers = {
            "Authorization": f"Basic {token}",
            "Content-Type": "application/json",
        }
        retry = Retry(total=3, backoff_factor=0.5, status_forcelist=[500, 502, 503, 504])
        adapter = HTTPAdapter(max_retries=retry)
        self._session = requests.Session()
        self._session.headers.update(headers)
        self._session.mount("https://", adapter)

    # ------------------------------------------------------------------
    # Context manager support
    # ------------------------------------------------------------------

    def __enter__(self) -> AzureDevOpsClient:
        return self

    def __exit__(self, *args: object) -> None:
        self.close()

    def close(self) -> None:
        """Close the underlying requests session."""
        self._session.close()

    # ------------------------------------------------------------------
    # Work items
    # ------------------------------------------------------------------

    def get_work_item(self, work_item_id: int) -> dict[str, Any]:
        """Return the full work item dict (fields + relations)."""
        url = (
            f"{self._base_url}/_apis/wit/workitems/{work_item_id}"
            f"?$expand=all&api-version={self.API_VERSION}"
        )
        r = self._session.get(url, timeout=15)
        r.raise_for_status()
        return r.json()

    def get_work_item_comments(
        self, work_item_id: int, max_comments: int = 5
    ) -> list[dict[str, Any]]:
        """Return the most recent comments for a work item."""
        url = f"{self._base_url}/_apis/wit/workitems/{work_item_id}/comments"
        r = self._session.get(
            url,
            params={"$top": max_comments, "api-version": self.COMMENTS_API_VERSION},
            timeout=15,
        )
        r.raise_for_status()
        data = r.json()
        return data.get("comments") or data.get("value", [])

    def add_comment(self, work_item_id: int, text: str) -> dict[str, Any]:
        """Post a comment to a work item."""
        url = (
            f"{self._base_url}/_apis/wit/workitems/{work_item_id}/comments"
            f"?api-version={self.COMMENTS_API_VERSION}"
        )
        r = self._session.post(url, json={"text": text}, timeout=15)
        r.raise_for_status()
        return r.json()

    def update_fields(self, work_item_id: int, fields: dict[str, Any]) -> dict[str, Any]:
        """Update work item fields using JSON Patch."""
        url = (
            f"{self._base_url}/_apis/wit/workitems/{work_item_id}"
            f"?api-version={self.API_VERSION}"
        )
        patch_doc = [
            {"op": "replace", "path": f"/fields/{field_name}", "value": value}
            for field_name, value in fields.items()
        ]
        r = self._session.patch(
            url,
            json=patch_doc,
            headers={"Content-Type": "application/json-patch+json"},
            timeout=15,
        )
        r.raise_for_status()
        return r.json()

    def ensure_work_item_tag(self, work_item_id: int, tag: str) -> dict[str, Any]:
        """Add a tag to a work item while preserving existing tags.

        Returns a dict with the work item ID, the merged tag string, and an
        ``updated`` flag (False if the tag was already present).
        """
        raw = self.get_work_item(work_item_id)
        existing_raw = raw.get("fields", {}).get("System.Tags", "") or ""
        existing = [t.strip() for t in existing_raw.split(";") if t.strip()]

        normalized = tag.strip()
        if normalized and normalized not in existing:
            existing.append(normalized)

        merged = "; ".join(existing)
        if merged == existing_raw:
            return {"id": work_item_id, "tags": merged, "updated": False}

        return self.update_fields(work_item_id, {"System.Tags": merged})

    # ------------------------------------------------------------------
    # Git repo files (generic)
    # ------------------------------------------------------------------

    def get_repo_file(
        self,
        repo: str,
        path: str,
        org: str | None = None,
        project: str | None = None,
    ) -> str:
        """Return the raw text content of a file from any ADO Git repository.

        Args:
            repo: Repository name (e.g. "Wiki", "Carmen.MasterData").
            path: File path within the repo (e.g. "/BAU-handbook/CARE/DQCs/page.md").
            org: ADO organisation. Defaults to the client's configured org.
            project: ADO project. Defaults to the client's configured project.
        """
        _org = (org or self._settings.ado_org).strip()
        _project = (project or self._settings.ado_project).strip()
        normalized_path = path if path.startswith("/") else f"/{path}"
        url = f"https://dev.azure.com/{_org}/{_project}/_apis/git/repositories/{repo}/items"
        r = self._session.get(
            url,
            params={
                "path": normalized_path,
                "api-version": self.API_VERSION,
                "$format": "text",
                "download": "true",
            },
            headers={"Accept": "text/plain"},
            timeout=15,
        )
        r.raise_for_status()
        return r.text

    def list_repo_files(
        self,
        repo: str,
        path: str = "/",
        org: str | None = None,
        project: str | None = None,
    ) -> list[str]:
        """Return a list of file paths under *path* in an ADO Git repository."""
        _org = (org or self._settings.ado_org).strip()
        _project = (project or self._settings.ado_project).strip()
        normalized_path = path if path.startswith("/") else f"/{path}"
        url = f"https://dev.azure.com/{_org}/{_project}/_apis/git/repositories/{repo}/items"
        r = self._session.get(
            url,
            params={
                "scopePath": normalized_path,
                "recursionLevel": "OneLevel",
                "api-version": self.API_VERSION,
            },
            timeout=15,
        )
        r.raise_for_status()
        return [item["path"] for item in r.json().get("value", [])]

    # ------------------------------------------------------------------
    # Relation type constants — ADO internal names for the relation
    # categories the agent cares about.
    # ------------------------------------------------------------------

    _REL_PARENT = "System.LinkTypes.Hierarchy-Reverse"
    _REL_CHILD = "System.LinkTypes.Hierarchy-Forward"
    _REL_RELATED = "System.LinkTypes.Related"
    _REL_DUPLICATE = "System.LinkTypes.Duplicate-Forward"
    _REL_DUPLICATE_OF = "System.LinkTypes.Duplicate-Reverse"
    _REL_SUCCESSOR = "System.LinkTypes.Dependency-Forward"
    _REL_PREDECESSOR = "System.LinkTypes.Dependency-Reverse"
    _REL_AFFECTS = "Microsoft.VSTS.Common.Affects-Forward"
    _REL_AFFECTED_BY = "Microsoft.VSTS.Common.Affects-Reverse"

    _REL_LABEL: dict[str, str] = {
        _REL_PARENT: "parent",
        _REL_CHILD: "child",
        _REL_RELATED: "related",
        _REL_DUPLICATE: "duplicate",
        _REL_DUPLICATE_OF: "duplicate of",
        _REL_SUCCESSOR: "successor",
        _REL_PREDECESSOR: "predecessor",
        _REL_AFFECTS: "affects",
        _REL_AFFECTED_BY: "affected by",
    }

    def get_linked_work_items(
        self,
        work_item_id: int,
        max_items: int = 5,
        relation_types: list[str] | None = None,
    ) -> list[dict[str, Any]]:
        """Return basic data for up to *max_items* linked work items.

        Args:
            work_item_id: The source work item ID.
            max_items: Maximum number of linked items to return.
            relation_types: ADO relation type strings to include. Defaults to all
                known relation types defined in _REL_*.

        Returns:
            List of dicts, each with keys:
                id, relation_type, title, state, work_item_type, description.
            Linked items that fail to fetch are silently skipped.
        """
        if max_items <= 0:
            return []

        if relation_types is None:
            relation_types = [
                self._REL_PARENT,
                self._REL_CHILD,
                self._REL_RELATED,
                self._REL_DUPLICATE,
                self._REL_DUPLICATE_OF,
                self._REL_SUCCESSOR,
                self._REL_PREDECESSOR,
                self._REL_AFFECTS,
                self._REL_AFFECTED_BY,
            ]

        url = (
            f"{self._base_url}/_apis/wit/workitems/{work_item_id}"
            f"?$expand=relations&api-version={self.API_VERSION}"
        )
        r = self._session.get(url, timeout=15)
        r.raise_for_status()
        data = r.json()

        relations: list[dict[str, Any]] = data.get("relations") or []
        result: list[dict[str, Any]] = []

        for rel in relations:
            if len(result) >= max_items:
                break

            rel_type = rel.get("rel", "")
            if rel_type not in relation_types:
                continue

            # Extract numeric work-item ID from the tail of the relation URL.
            tail = rel.get("url", "").rstrip("/").rsplit("/", 1)[-1]
            if not tail.isdigit():
                continue
            linked_id = int(tail)

            try:
                linked = self.get_work_item(linked_id)
            except Exception:
                continue

            fields = linked.get("fields", {})
            result.append(
                {
                    "id": linked_id,
                    "relation_type": self._REL_LABEL.get(rel_type, rel_type),
                    "title": fields.get("System.Title", ""),
                    "state": fields.get("System.State", ""),
                    "work_item_type": fields.get("System.WorkItemType", ""),
                    "description": fields.get("System.Description", ""),
                }
            )

        return result

    # ------------------------------------------------------------------
    # Wiki convenience wrappers
    # (The Wiki is a Git repo named "Wiki" in the configured org/project.)
    # ------------------------------------------------------------------

    WIKI_REPO = "Wiki"

    def get_wiki_page(self, path: str) -> str:
        """Return the markdown content of a wiki page by its Git path.

        Args:
            path: File path within the Wiki repo, e.g.
                '/BAU-handbook/CARE/DQCs/Nom_vs_Deal_and_Adj.md'
        """
        return self.get_repo_file(self.WIKI_REPO, path)

    def list_wiki_files(self, folder: str = "/") -> list[str]:
        """Return file paths under *folder* in the Wiki repo."""
        return self.list_repo_files(self.WIKI_REPO, folder)

    # ------------------------------------------------------------------
    # Static helpers
    # ------------------------------------------------------------------

    @staticmethod
    def html_url(raw: dict[str, Any]) -> str | None:
        """Return the browser-facing work item URL from a raw ADO payload.

        ADO nests this under ``_links.html.href``. Every hop is defensive
        because partial payloads (e.g. a relations-only ``$expand``) omit
        ``_links`` entirely. Returns None when the link is absent.
        """
        return raw.get("_links", {}).get("html", {}).get("href")

    @staticmethod
    def extract_wiki_links_from_html(html: str) -> list[str]:
        """Extract ADO wiki page links from HTML description content."""
        return re.findall(r'href="([^"]*/_wiki/[^"]*)"', html)

    @staticmethod
    def extract_dqc_name_from_title(title: str) -> str | None:
        """Extract the DQC subscription name from an ADO ticket title.

        Expects titles like 'DQC Alert: Missing_Area_and_Balancegroup_assignments — ...'
        Returns the snake_case name or None if no match.
        """
        match = re.search(r"DQC(?:\s+Alert)?[:\s]+([A-Za-z0-9_]+)", title)
        return match.group(1) if match else None
