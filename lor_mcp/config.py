"""Configuration for Library of Ruina MCP Server."""

import os

# Game Mod HTTP bridge address
# Primary: C# HttpListener in game (port 17127, listens on IPv6 localhost)
# Fallback: Python proxy server (port 17128, reads JSON files from disk)
LOR_API_BASE_URL: str = os.environ.get("LOR_API_BASE_URL", "http://localhost:17127")
LOR_PROXY_FALLBACK_URL: str = os.environ.get("LOR_PROXY_FALLBACK_URL", "http://localhost:17128")

# MCP tool profile: "guided" exposes pre-defined action tools,
# "raw" exposes a single generic act tool.
LOR_MCP_TOOL_PROFILE: str = os.environ.get("LOR_MCP_TOOL_PROFILE", "guided")