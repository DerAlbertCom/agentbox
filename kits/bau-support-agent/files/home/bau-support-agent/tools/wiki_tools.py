"""
tools/wiki_tools.py — Strands @tool wrappers for ADO Wiki access.

The Wiki is stored as a Git repo named "Wiki" in RWEST-MFI-AE/CS.
DQC resolution pages live under /BAU-handbook/CARE/DQCs/
General CaRMen BAU pages live under /BAU-handbook/CaRMen/

DQC page lookup is dynamic: the tool lists the folder contents at runtime and
uses fuzzy name-matching to find the right page — no hardcoded path mapping needed.
"""
from __future__ import annotations

import json
import sys
import os
from typing import Any

from strands import tool

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
from ado_client import AzureDevOpsClient

# Wiki folders to search for DQC and BAU pages.
_WIKI_FOLDERS = [
    "/BAU-handbook/CARE/DQCs",
    "/BAU-handbook/CaRMen",
]


def _normalize(text: str) -> str:
    """Lower-case and replace separators for fuzzy comparison."""
    return text.lower().replace("-", " ").replace("_", " ").replace("%2d", " ")


def _find_best_match(name: str, paths: list[str]) -> str | None:
    """Return the path whose filename best matches *name*, or None.

    Strategy (in order):
    1. Exact filename match (after normalization).
    2. All words of *name* present in the filename.
    3. Any word of *name* (>3 chars) present in the filename.
    """
    normalized_name = _normalize(name)
    name_words = [w for w in normalized_name.split() if len(w) > 3]

    exact, all_words, any_word = None, None, None
    for path in paths:
        filename = _normalize(path.split("/")[-1].replace(".md", ""))
        if filename == normalized_name and exact is None:
            exact = path
        if all(w in filename for w in name_words) and all_words is None:
            all_words = path
        if any(w in filename for w in name_words) and any_word is None:
            any_word = path

    return exact or all_words or any_word


def create_wiki_tools(client: AzureDevOpsClient) -> list[Any]:
    """Return Strands tool list for wiki access backed by *client*."""

    def _list_all_wiki_files() -> list[str]:
        """List all files across the known wiki folders."""
        files: list[str] = []
        for folder in _WIKI_FOLDERS:
            try:
                files.extend(client.list_wiki_files(folder))
            except Exception:
                pass
        return files

    @tool
    def get_wiki_page_for_dqc(dqc_name: str) -> str:
        """Look up the wiki resolution page for a DQC subscription name.

        Dynamically lists the DQC wiki folder and uses fuzzy name-matching to
        find the right page — no hardcoded mapping required.

        Args:
            dqc_name: The DQC subscription name from the ticket title, e.g.
                'CaRMen_DQC_NOM_VS_DEAL_AND_ADJ' or 'OFFSETDEAL_MATCH'.

        Returns:
            Wiki page content as JSON, or a not-found message with a search
            suggestion if no matching page was found.
        """
        try:
            all_files = _list_all_wiki_files()
            path = _find_best_match(dqc_name, all_files)

            if not path:
                return json.dumps({
                    "error": f"No wiki page found matching DQC name '{dqc_name}'.",
                    "suggestion": f"Try search_wiki('{dqc_name}') or get_wiki_page with a known path.",
                    "available_pages": [p.split("/")[-1] for p in all_files],
                })

            content = client.get_wiki_page(path)
            if not content:
                return json.dumps({"error": f"Page found at '{path}' but has no content."})
            return json.dumps({"dqc_name": dqc_name, "path": path, "content": content[:8000]})
        except Exception as e:
            return json.dumps({"error": f"Could not look up wiki page for '{dqc_name}': {e}"})

    @tool
    def get_wiki_page(page_path: str) -> str:
        """Fetch the content of a wiki page by its full Git path.

        Use this when you already know the exact path (e.g. from a previous
        search_wiki result or a link in the ticket description).

        Args:
            page_path: Full wiki file path, e.g.
                '/BAU-handbook/CARE/DQCs/Nom_vs_Deal_and_Adj.md'

        Returns:
            Markdown content of the page, or an error message if not found.
        """
        try:
            content = client.get_wiki_page(page_path)
            if not content:
                return json.dumps({"error": f"Page '{page_path}' found but has no content."})
            return json.dumps({"path": page_path, "content": content[:8000]})
        except Exception as e:
            return json.dumps({"error": f"Could not fetch wiki page '{page_path}': {e}"})

    @tool
    def search_wiki(query: str, top: int = 5) -> str:
        """Search wiki pages by filename across the BAU handbook folders.

        Lists files in the DQC and CaRMen BAU folders and returns those whose
        filename contains any significant word from the query.

        Args:
            query: Search text, e.g. 'offset deal' or 'nexus processing errors'.
            top: Max number of results to return (default 5).

        Returns:
            JSON list of matching file paths. Use get_wiki_page to read a result.
        """
        try:
            query_words = [w for w in _normalize(query).split() if len(w) > 3]
            all_files = _list_all_wiki_files()

            matches = []
            for path in all_files:
                filename = _normalize(path.split("/")[-1].replace(".md", ""))
                if any(w in filename for w in query_words):
                    matches.append(path)
                if len(matches) >= top:
                    break

            if not matches:
                return json.dumps({
                    "results": [],
                    "message": f"No wiki pages found matching '{query}'.",
                    "hint": "Try get_wiki_page_for_dqc with the DQC subscription name.",
                })
            return json.dumps({"query": query, "results": matches})
        except Exception as e:
            return json.dumps({"error": f"Wiki search failed: {e}"})

    return [get_wiki_page_for_dqc, get_wiki_page, search_wiki]
