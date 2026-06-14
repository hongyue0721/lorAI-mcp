#!/usr/bin/env python3
"""Register lor-mcp MCP server in kimi-code's mcp.json.

Usage:
    python scripts/setup-mcp.py [--unregister]

This script is designed to be runnable by an AI agent with a single command.
It reads the existing mcp.json, adds (or updates) the lor-mcp entry,
and writes it back. If --unregister is passed, the entry is removed instead.
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import sys
from pathlib import Path

# ── Paths ──

KIMI_CODE_HOME = Path(os.environ.get(
    "KIMI_CODE_HOME",
    os.path.join(os.path.expanduser("~"), ".kimi-code"),
))

MCP_JSON_PATH = KIMI_CODE_HOME / "mcp.json"

SERVER_NAME = "lor-mcp"

# The entry we register. Uses the installed `lor-mcp` console script.
# If lor-mcp is not on PATH, falls back to `python -m lor_mcp.server`.
SERVER_ENTRY = {
    "command": "lor-mcp",
    "args": [],
    "env": {
        "LOR_API_BASE_URL": "http://localhost:17127",
    },
}

SERVER_ENTRY_FALLBACK = {
    "command": sys.executable,
    "args": ["-m", "lor_mcp.server"],
    "env": {
        "LOR_API_BASE_URL": "http://localhost:17127",
    },
}


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Register/unregister lor-mcp in kimi-code mcp.json"
    )
    parser.add_argument(
        "--unregister", action="store_true",
        help="Remove lor-mcp from mcp.json instead of adding it",
    )
    args = parser.parse_args()

    # ── Load existing config ──
    if MCP_JSON_PATH.exists():
        config = json.loads(MCP_JSON_PATH.read_text(encoding="utf-8"))
    else:
        config = {}

    if "mcpServers" not in config:
        config["mcpServers"] = {}

    servers = config["mcpServers"]

    # ── Unregister ──
    if args.unregister:
        if SERVER_NAME in servers:
            del servers[SERVER_NAME]
            MCP_JSON_PATH.write_text(
                json.dumps(config, indent=2, ensure_ascii=False) + "\n",
                encoding="utf-8",
            )
            print(f"[setup-mcp] Removed '{SERVER_NAME}' from {MCP_JSON_PATH}")
        else:
            print(f"[setup-mcp] '{SERVER_NAME}' not found in {MCP_JSON_PATH} (nothing to do)")
        return 0

    # ── Pick entry: prefer console script if on PATH, else fallback ──
    entry = SERVER_ENTRY
    if not shutil.which("lor-mcp"):
        entry = SERVER_ENTRY_FALLBACK
        print("[setup-mcp] 'lor-mcp' not on PATH, using 'python -m lor_mcp.server' fallback")

    # ── Register / update ──
    is_update = SERVER_NAME in servers
    servers[SERVER_NAME] = entry

    MCP_JSON_PATH.parent.mkdir(parents=True, exist_ok=True)
    MCP_JSON_PATH.write_text(
        json.dumps(config, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )

    action = "Updated" if is_update else "Registered"
    print(f"[setup-mcp] {action} '{SERVER_NAME}' in {MCP_JSON_PATH}")
    print(f"[setup-mcp] Entry: {json.dumps(entry, ensure_ascii=False)}")
    print(f"[setup-mcp] Restart kimi-code (or run :mcp reload) to activate.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
