"""Library of Ruina MCP Server — main entry point.

Aligned with the actual RuntimeStateExport HTTP bridge running on port 17127.

Actual endpoints:
  GET  /health           → health check
  GET  /state            → full game state
  GET  /state/{layer}    → specific layer (navigation/progression/floors/inventory/availablestages/battle)
  GET  /static           → list static data files
  GET  /static/{name}    → read a static data file (e.g. cards, books, enemies, passives)
  POST /action           → execute an action (see BridgeActions for available actions)
  GET  /action-status    → check deferred action completion status
"""

from __future__ import annotations

import json
import time
from typing import Any

import httpx
from fastmcp import FastMCP

from lor_mcp.config import LOR_API_BASE_URL, LOR_MCP_TOOL_PROFILE, LOR_PROXY_FALLBACK_URL

mcp = FastMCP("lor-mcp-server")

_client: httpx.AsyncClient | None = None
_bridge_mode: str = "unknown"  # "native" or "proxy" or "unavailable"
_last_detect_time: float = 0.0
_DETECT_RETRY_INTERVAL = 15.0  # re-detect bridge every 15s if unavailable


async def _detect_bridge() -> httpx.AsyncClient:
    """Try native C# bridge first, fall back to Python proxy."""
    global _client, _bridge_mode, _last_detect_time
    _last_detect_time = time.monotonic()

    # Close any existing client before creating a new one
    if _client is not None and not _client.is_closed:
        try:
            await _client.aclose()
        except Exception:
            pass

    # Try native bridge (C# HttpListener on game port)
    try:
        native = httpx.AsyncClient(base_url=LOR_API_BASE_URL, timeout=5.0)
        resp = await native.get("/health")
        if resp.status_code == 200:
            _client = native
            _bridge_mode = "native"
            return _client
        await native.aclose()
    except (httpx.HTTPError, OSError, RuntimeError):
        pass

    # Fall back to Python proxy
    try:
        proxy = httpx.AsyncClient(base_url=LOR_PROXY_FALLBACK_URL, timeout=30.0)
        resp = await proxy.get("/health")
        if resp.status_code == 200:
            _client = proxy
            _bridge_mode = "proxy"
            return _client
        await proxy.aclose()
    except (httpx.HTTPError, OSError, RuntimeError):
        pass

    # Neither bridge available — create client anyway (will fail on requests)
    _client = httpx.AsyncClient(base_url=LOR_API_BASE_URL, timeout=30.0)
    _bridge_mode = "unavailable"
    return _client


async def _get_client() -> httpx.AsyncClient:
    """Return (and lazily create) the shared httpx async client.

    If the bridge was previously unavailable, re-detect periodically.
    """
    global _client
    if _client is None or _client.is_closed:
        _client = await _detect_bridge()
    elif _bridge_mode == "unavailable" and (time.monotonic() - _last_detect_time) > _DETECT_RETRY_INTERVAL:
        _client = await _detect_bridge()
    return _client


def _parse_params_into(params: str, body: dict[str, Any]) -> None:
    """Parse comma-separated key=value pairs into *body*, preserving commas inside brackets.

    Commas inside [...] or {...} are NOT treated as pair separators.
    """
    pairs: list[str] = []
    depth = 0
    start = 0
    for i, ch in enumerate(params):
        if ch in ("[", "{"):
            depth += 1
        elif ch in ("]", "}"):
            depth -= 1
        elif ch == "," and depth == 0:
            pairs.append(params[start:i])
            start = i + 1
    if start < len(params):
        pairs.append(params[start:])

    for pair in pairs:
        kv = pair.split("=", 1)
        if len(kv) != 2:
            continue
        key, val = kv[0].strip(), kv[1].strip()
        if val and val[0] in ("[", "{"):
            try:
                body[key] = json.loads(val)
            except json.JSONDecodeError:
                body[key] = val
            continue
        if val.lower() == "true":
            body[key] = True
        elif val.lower() == "false":
            body[key] = False
        else:
            try:
                body[key] = int(val)
            except ValueError:
                try:
                    body[key] = float(val)
                except ValueError:
                    body[key] = val


# ──────────────────────────── health ────────────────────────────

@mcp.tool()
async def health_check() -> dict[str, Any]:
    """Check whether the Library of Ruina game Mod HTTP bridge is online and reachable."""
    client = await _get_client()
    try:
        resp = await client.get("/health")
        resp.raise_for_status()
        return resp.json()
    except httpx.HTTPError as exc:
        return {"online": False, "error": str(exc)}


# ──────────────────────────── state ────────────────────────────

@mcp.tool()
async def get_state_layer(layer: str) -> dict[str, Any]:
    """Return a specific layer of the game state.

    Valid layers: navigation, progression, floors, inventory, availablestages, battle
    """
    client = await _get_client()
    try:
        resp = await client.get(f"/state/{layer}")
        resp.raise_for_status()
        return resp.json()
    except httpx.HTTPError as exc:
        return {"error": str(exc)}


# ──────────────────────────── static game data ────────────────────────────

@mcp.tool()
async def get_static_data_list() -> dict[str, Any]:
    """List all available static game data files exported by StaticDataExport mod.

    Returns file names and sizes for: cards, books, enemies, passives, decks, stages, etc.
    """
    client = await _get_client()
    try:
        resp = await client.get("/static")
        resp.raise_for_status()
        return resp.json()
    except httpx.HTTPError as exc:
        return {"error": str(exc)}


@mcp.tool()
async def get_game_data_item(collection: str, item_id: str) -> dict[str, Any]:
    """Return a single item from a static game data collection.

    First calls /static/{collection} to get the full collection, then finds the item by id.
    For efficiency, prefer get_game_data_items when you need multiple items.

    Collections: cards, books, enemies, passives, decks, stages, drop_books,
                 card_drop_tables, gifts, emotion_cards, emotion_ego, formations,
                 quests, floor_levels, final_rewards, tooltips, titles, credits
    """
    client = await _get_client()
    try:
        resp = await client.get(f"/static/{collection}")
        resp.raise_for_status()
        data = resp.json()
    except httpx.HTTPError as exc:
        return {"error": str(exc)}
    # The static files are JSON arrays or dicts; search for the item by id
    if isinstance(data, list):
        for item in data:
            if isinstance(item, dict):
                # Try common id field names
                for key in ("id", "_id", "ID", "Id"):
                    if key in item:
                        raw_id = item[key]
                        # LorId can be int or dict like {"packageId": ..., "id": ...}
                        if isinstance(raw_id, dict):
                            str_id = f"{raw_id.get('packageId', '')}_{raw_id.get('id', '')}"
                        else:
                            str_id = str(raw_id)
                        if str_id == item_id:
                            return item
        return {"error": f"Item {item_id} not found in {collection}"}
    elif isinstance(data, dict):
        if item_id in data:
            return data[item_id]
        # Search nested
        for key, val in data.items():
            if isinstance(val, list):
                for item in val:
                    if isinstance(item, dict):
                        for id_key in ("id", "_id", "ID"):
                            if id_key in item and str(item[id_key]) == item_id:
                                return item
        return {"error": f"Item {item_id} not found in {collection}"}
    return {"error": f"Unexpected data format for {collection}"}


@mcp.tool()
async def get_game_data_items(collection: str, item_ids: str) -> dict[str, Any]:
    """Return items from a static game data collection by comma-separated ids.

    More efficient than calling get_game_data_item multiple times.

    Example: get_game_data_items(collection='cards', item_ids='1,2,3')
    """
    ids = [x.strip() for x in item_ids.split(",")]
    client = await _get_client()
    try:
        resp = await client.get(f"/static/{collection}")
        resp.raise_for_status()
        data = resp.json()
    except httpx.HTTPError as exc:
        return {"error": str(exc), "items": [], "count": 0}
    results = []
    if isinstance(data, list):
        for item in data:
            if isinstance(item, dict):
                for key in ("id", "_id", "ID", "Id"):
                    if key in item:
                        raw_id = item[key]
                        if isinstance(raw_id, dict):
                            str_id = f"{raw_id.get('packageId', '')}_{raw_id.get('id', '')}"
                        else:
                            str_id = str(raw_id)
                        if str_id in ids:
                            results.append(item)
                            break
    return {"items": results, "count": len(results)}


# ──────────────────────────── action execution ────────────────────────────

@mcp.tool()
async def act(action: str, params: str = "") -> dict[str, Any]:
    """Execute a game action through the Mod HTTP bridge.

    Available actions:
      navigate          - navigate UI phase (params: phase=<phase>)
      selectSephirah    - select a sephirah floor (params: sephirah=<sephirah>)
      getFloor          - get floor data (params: sephirah=<sephirah>)
      startStage        - set stage on invitation panel (params: stageId=<id>)
      runStage          - run a stage end-to-end (params: stageId=<id>)
      startBattle       - start the battle from BattleSetting panel
      startGame         - click Continue/New Game on title screen
      prepareBattle     - auto-select books and prepare battle (params: stageId=<id>)
      autoPlay          - game's built-in auto card placement
      confirmCards      - confirm card placement (ApplyLibrarianCardPhase only)
      playBattleRound   - atomic autoPlay + confirmCards
      endBattle         - force-end current battle
      closeBattleScene  - close the battle scene
      clickBattleResult - click battle result button
      gameOver          - trigger game over
      killAllEnemy      - kill all enemy units (debug)
      getStageInfo      - get stage controller info (params: stageId=<id>)
      getBattleUnits    - export all battle unit data
      getEmotionCandidates - get emotion card candidates
      selectEmotionCard - select emotion card by index (params: index=<n>)
      forceAdvancePhase - force-advance a stuck phase (params: phase=<name>)
      skipStory         - skip current story
      endStory          - end current story
      advanceStory      - advance current story
      listMethods       - list methods on a type (params: type=<type>)
      callMethod        - call method via reflection (params: type=<type>, method=<name>)
      getGameState      - diagnostic singleton dump

    When LOR_MCP_TOOL_PROFILE == 'guided', prefer the specific tools from tools.py.
    params: comma-separated key=value pairs, e.g. "stageId=3,sephirah=Malkuth"
            Commas inside [...] or {...} are preserved (not split on).
    """
    client = await _get_client()
    body: dict[str, Any] = {"action": action}
    if params:
        _parse_params_into(params, body)
    try:
        resp = await client.post("/action", json=body)
        resp.raise_for_status()
        return resp.json()
    except httpx.HTTPError as exc:
        return {"error": str(exc), "action": action}


# ──────────────────────────── action status ────────────────────────────

@mcp.tool()
async def get_action_status() -> dict[str, Any]:
    """Check the status of deferred (async) actions.

    Note: The /action-status endpoint may not be available in all mod versions.
    Returns completed action results and pending action IDs if supported.
    """
    client = await _get_client()
    try:
        resp = await client.get("/action-status")
        if resp.status_code == 404:
            return {"available": False, "message": "action-status endpoint not in this mod version"}
        resp.raise_for_status()
        return resp.json()
    except httpx.HTTPError as exc:
        return {"available": False, "error": str(exc)}


# ──────────────────────────── guided tools ────────────────────────────

if LOR_MCP_TOOL_PROFILE == "guided":
    from lor_mcp.tools import register_guided_tools

    register_guided_tools(mcp)


# ──────────────────────────── entry ────────────────────────────

def main() -> None:
    """Run the MCP server."""
    mcp.run()


if __name__ == "__main__":
    main()