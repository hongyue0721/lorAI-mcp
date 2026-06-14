#!/usr/bin/env python3
"""Register lor-mcp MCP server across all supported AI coding clients.

Usage:
    python -m lor_mcp.setup                  # auto-detect & register everywhere
    python -m lor_mcp.setup --client kimi    # specific client only
    python -m lor_mcp.setup --all            # register to ALL known clients
    python -m lor_mcp.setup --unregister     # remove from all detected
    python -m lor_mcp.setup --list           # show detected clients

Designed to be run by an AI agent with a single command — no user interaction needed.

Supported clients:
    kimi-code       ~/.kimi-code/mcp.json
    claude-code     Project .mcp.json (Claude Code CLI / OpenCode compatible)
    claude-desktop  Claude Desktop app config
    cursor          .cursor/mcp.json
    windsurf        Windsurf mcp_config.json
    cline           Cline (VS Code extension) settings
"""

from __future__ import annotations

import argparse
import json
import os
import platform
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path

SERVER_NAME = "lor-mcp"
HOME = Path(os.path.expanduser("~"))
IS_WINDOWS = platform.system() == "Windows"
IS_MACOS = platform.system() == "Darwin"
APPDATA = Path(os.environ.get("APPDATA", str(HOME / "AppData" / "Roaming"))) if IS_WINDOWS else None


# ── Server entry ──

def build_entry() -> dict:
    """Build the MCP server entry, using lor-mcp if on PATH, else python -m."""
    if shutil.which("lor-mcp"):
        return {
            "command": "lor-mcp",
            "args": [],
            "env": {"LOR_API_BASE_URL": "http://localhost:17127"},
        }
    return {
        "command": sys.executable,
        "args": ["-m", "lor_mcp.server"],
        "env": {"LOR_API_BASE_URL": "http://localhost:17127"},
    }


# ── Client definitions ──

@dataclass
class ClientSpec:
    """Describes where and how to write MCP config for a given client."""
    id: str           # short identifier for --client flag
    name: str         # human-readable name
    path: Path        # config file path
    top_key: str      # root key ("mcpServers" or "servers")
    project_level: bool = False  # if True, writes to project dir (cwd)


def _claude_desktop_path() -> Path:
    if IS_WINDOWS and APPDATA:
        return APPDATA / "Claude" / "claude_desktop_config.json"
    if IS_MACOS:
        return HOME / "Library" / "Application Support" / "Claude" / "claude_desktop_config.json"
    return HOME / ".config" / "Claude" / "claude_desktop_config.json"


def _cline_path() -> Path:
    if APPDATA:
        return APPDATA / "Code" / "User" / "globalStorage" / "saoudrizwan.claude-dev" / "settings" / "cline_mcp_settings.json"
    return HOME / ".config" / "Code" / "User" / "globalStorage" / "saoudrizwan.claude-dev" / "settings" / "cline_mcp_settings.json"


def _windsurf_path() -> Path:
    if IS_WINDOWS and APPDATA:
        return APPDATA / "Codeium" / "windsurf" / "mcp_config.json"
    if IS_MACOS:
        return HOME / ".codeium" / "windsurf" / "mcp_config.json"
    return HOME / ".codeium" / "windsurf" / "mcp_config.json"


def _get_client_specs() -> list[ClientSpec]:
    """Build client list. Project-level paths use cwd at runtime."""
    cwd = Path.cwd()
    return [
        ClientSpec(
            id="kimi",
            name="kimi-code",
            path=HOME / ".kimi-code" / "mcp.json",
            top_key="mcpServers",
        ),
        ClientSpec(
            id="claude-code",
            name="Claude Code (.mcp.json)",
            path=cwd / ".mcp.json",
            top_key="mcpServers",
            project_level=True,
        ),
        ClientSpec(
            id="claude-desktop",
            name="Claude Desktop",
            path=_claude_desktop_path(),
            top_key="mcpServers",
        ),
        ClientSpec(
            id="cursor",
            name="Cursor",
            path=cwd / ".cursor" / "mcp.json",
            top_key="mcpServers",
            project_level=True,
        ),
        ClientSpec(
            id="windsurf",
            name="Windsurf",
            path=_windsurf_path(),
            top_key="mcpServers",
        ),
        ClientSpec(
            id="cline",
            name="Cline (VS Code)",
            path=_cline_path(),
            top_key="servers",
        ),
    ]


def detect_clients(specs: list[ClientSpec]) -> list[ClientSpec]:
    """Return clients whose config file already exists OR whose parent dir exists."""
    detected = []
    for c in specs:
        if c.path.exists() or c.path.parent.exists():
            detected.append(c)
    # Always include project-level clients
    for c in specs:
        if c.project_level and c not in detected:
            detected.append(c)
    return detected


def select_clients(specs: list[ClientSpec], wanted_ids: list[str] | None, all_flag: bool) -> list[ClientSpec]:
    if all_flag:
        return list(specs)
    if wanted_ids:
        id_map = {c.id: c for c in specs}
        result = []
        for wid in wanted_ids:
            if wid in id_map:
                result.append(id_map[wid])
            else:
                print(f"[lor-mcp setup] WARNING: Unknown client '{wid}', skipping")
        return result
    return detect_clients(specs)


# ── Read / write config ──

def read_config(path: Path) -> dict:
    if path.exists():
        try:
            return json.loads(path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            return {}
    return {}


def write_config(path: Path, config: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(config, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def register_one(client: ClientSpec, entry: dict) -> bool:
    config = read_config(client.path)
    servers = config.setdefault(client.top_key, {})
    is_update = SERVER_NAME in servers
    servers[SERVER_NAME] = entry
    write_config(client.path, config)
    verb = "Updated" if is_update else "Registered"
    print(f"  [{client.id}] {verb} -> {client.path}")
    return True


def unregister_one(client: ClientSpec) -> bool:
    config = read_config(client.path)
    servers = config.get(client.top_key, {})
    if SERVER_NAME not in servers:
        print(f"  [{client.id}] Not found (skipped)")
        return False
    del servers[SERVER_NAME]
    write_config(client.path, config)
    print(f"  [{client.id}] Removed -> {client.path}")
    return True


# ── Main ──

def main() -> int:
    specs = _get_client_specs()

    parser = argparse.ArgumentParser(
        prog="lor-mcp-setup",
        description="Register lor-mcp MCP server across AI coding clients.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="Supported clients: " + ", ".join(c.id for c in specs),
    )
    parser.add_argument(
        "--client", action="append", dest="clients", metavar="ID",
        help="Target specific client (repeatable). Choices: " + ", ".join(c.id for c in specs),
    )
    parser.add_argument("--all", action="store_true", help="Register to ALL known clients")
    parser.add_argument("--unregister", action="store_true", help="Remove lor-mcp instead of adding")
    parser.add_argument("--list", action="store_true", help="List detected clients and exit")
    args = parser.parse_args()

    # ── List mode ──
    if args.list:
        detected = detect_clients(specs)
        print("MCP clients:")
        for c in specs:
            mark = "*" if c in detected else " "
            status = "config exists" if c.path.exists() else "dir exists" if c.path.parent.exists() else "not found"
            print(f"  {mark} {c.id:16s} {c.name:30s} {status}")
            print(f"                    {c.path}")
        return 0

    # ── Select targets ──
    targets = select_clients(specs, args.clients, args.all)
    if not targets:
        print("[lor-mcp setup] No MCP clients detected.")
        print("  Use --all to register everywhere, or --client <id> for a specific one.")
        print("  Run --list to see all known client paths.")
        return 1

    entry = build_entry()
    if not shutil.which("lor-mcp"):
        print("[lor-mcp setup] 'lor-mcp' not on PATH, using 'python -m lor_mcp.server' fallback\n")

    # ── Execute ──
    action = "Unregistering" if args.unregister else "Registering"
    print(f"[lor-mcp setup] {action} '{SERVER_NAME}' in {len(targets)} client(s):\n")

    success = 0
    for client in targets:
        if args.unregister:
            if unregister_one(client):
                success += 1
        else:
            if register_one(client, entry):
                success += 1

    # ── Summary ──
    print(f"\n[lor-mcp setup] Done: {success}/{len(targets)} client(s) updated.")
    if not args.unregister:
        print(f"[lor-mcp setup] Entry: {json.dumps(entry, ensure_ascii=False)}")
    print("[lor-mcp setup] Restart the target client(s) to activate changes.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
