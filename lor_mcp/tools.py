"""Guided MCP tools for Library of Ruina auto-play.

Architecture:
  - A single C# mod (LorAIHost) runs inside the game, exposing an HTTP
    bridge at http://localhost:17127 with /state, /action, /static endpoints.
  - This module registers MCP tools that an LLM can call to control the game.
  - Strategy decisions (which stage next, which emotion card to pick) are
    intentionally left to the LLM — these tools are the execution layer.

Key design:
  - get_game_state / get_battle_units / get_emotion_candidates → READ
  - start_game / start_battle / battle_loop / select_emotion_card → WRITE
  - The LLM reads state, decides, then calls the appropriate action tool.
"""

from __future__ import annotations

import asyncio
import json
from typing import Any

import httpx
from fastmcp import FastMCP

from lor_mcp.config import LOR_API_BASE_URL


# ── Shared HTTP client (avoids circular import with server.py) ──

_shared_client: httpx.AsyncClient | None = None

async def _get_client() -> httpx.AsyncClient:
    """Get the shared HTTP client.

    Tries to use server.py's client (with bridge detection + proxy fallback).
    Falls back to a direct connection if server.py isn't initialized yet.
    """
    global _shared_client
    if _shared_client is None or _shared_client.is_closed:
        try:
            from lor_mcp.server import _get_client as _get_shared_client
            _shared_client = await _get_shared_client()
        except Exception:
            _shared_client = httpx.AsyncClient(base_url=LOR_API_BASE_URL, timeout=60.0)
    return _shared_client

async def _post_action(body: dict[str, Any]) -> dict[str, Any]:
    client = await _get_client()
    resp = await client.post("/action", json=body)
    resp.raise_for_status()
    return resp.json()

async def _get_state(timeout: float = 5.0) -> dict[str, Any] | None:
    client = await _get_client()
    try:
        resp = await client.get("/state", timeout=timeout)
        if resp.status_code == 200:
            return resp.json()
    except httpx.HTTPError:
        pass
    return None

async def _safe_post(body: dict[str, Any]) -> dict[str, Any] | None:
    try:
        return await _post_action(body)
    except httpx.HTTPError:
        return None

def _result(resp: dict[str, Any] | None) -> dict[str, Any]:
    """Extract the result dict from an /action response."""
    if resp is None:
        return {"error": "Bridge not reachable"}
    return resp.get("result", resp)


# ── Tool registration ──

def register_guided_tools(mcp: FastMCP) -> None:

    # ════════════════════════════════════════════
    #  READ: State & Data
    # ════════════════════════════════════════════

    @mcp.tool()
    async def get_game_state() -> dict[str, Any]:
        """Get the full current game state from the bridge.

        Returns: navigation, progression, floors, inventory, availableStages, battle.
        Key fields for decision-making:
          - navigation.activeScene: Title | Main | Battle | Story
          - navigation.currentUIPhase: Sephiroth | Invitation | BattleSetting | BattleResult | ...
          - battle.phase: RoundStartPhase_UI | ApplyLibrarianCardPhase | RoundEndPhase | EndBattle | ...
          - battle.inBattle, battle.roundTurn
          - availableStages.stages[]: id, chapter, floorNum, stageType
        """
        state = await _get_state()
        return state if state else {"error": "Bridge not reachable"}

    @mcp.tool()
    async def get_battle_units() -> dict[str, Any]:
        """Get detailed data for all current battle units (players + enemies).

        Returns HP, break, speed dice, hand cards, emotion level for each unit.
        Only works during an active battle.
        """
        return _result(await _safe_post({"action": "getBattleUnits"}))

    @mcp.tool()
    async def get_emotion_candidates() -> dict[str, Any]:
        """Get current emotion card candidates during RoundEndPhase.

        Returns list of candidate cards with: index, id, name, state
        (Positive/Negative), emotionLevel, targetType.
        Returns {active: false} if emotion card UI is not currently shown.

        The LLM should call this when battle.phase is RoundEndPhase and
        the phase seems stuck, then decide which card to pick.
        """
        return _result(await _safe_post({"action": "getEmotionCandidates"}))

    @mcp.tool()
    async def get_stage_info(stage_id: int) -> dict[str, Any]:
        """Get info about a specific stage by ID.

        Returns stage controller state: wave, round, isEndContents.
        """
        return await _post_action({"action": "getStageInfo", "stageId": stage_id})

    # ════════════════════════════════════════════
    #  WRITE: Navigation & Battle Flow
    # ════════════════════════════════════════════

    @mcp.tool()
    async def start_game(wait_seconds: int = 30) -> dict[str, Any]:
        """Click Continue on the title screen and wait for the main UI.

        The HTTP bridge may go down during scene transition; this tool
        retries until the scene is no longer Title.
        """
        await _safe_post({"action": "startGame"})

        for _ in range(max(5, wait_seconds // 2)):
            await asyncio.sleep(2)
            state = await _get_state(timeout=3.0)
            if state and state.get("navigation", {}).get("activeScene") != "Title":
                return {"success": True, "scene": state["navigation"]["activeScene"]}
        return {"success": False, "message": "Still on title screen"}

    @mcp.tool()
    async def navigate(phase: str) -> dict[str, Any]:
        """Navigate to a UI phase.

        Valid phases: Sephirah, Librarian, Invitation, BattleSetting,
                      Sepiroth, Story, Main_ItemList, Custom_CoreBook
        """
        return await _post_action({"action": "navigate", "phase": phase})

    @mcp.tool()
    async def start_battle(stage_id: int, wait_seconds: int = 40) -> dict[str, Any]:
        """Start a battle for the given stage with automatic book selection.

        Flow: navigate Invitation -> prepareBattle (auto-selects best books
        by HP) -> wait for BattleSetting -> startBattle -> wait for Battle.

        The LLM should call this after deciding which stage to play,
        then call battle_loop to auto-play the combat.

        Args:
            stage_id: Stage ID (e.g. 1, 2, 10001).
            wait_seconds: Max seconds to wait for battle to start.
        """
        await _safe_post({"action": "navigate", "phase": "Invitation"})
        await asyncio.sleep(2)

        result = _result(await _safe_post({
            "action": "prepareBattle", "stageId": stage_id,
        }))
        if not result.get("success", True):
            return {"success": False, "error": result.get("error", "unknown")}

        await asyncio.sleep(2)

        # Poll until battle starts
        for _ in range(max(10, wait_seconds // 2)):
            state = await _get_state(timeout=3.0)
            if state is None:
                await asyncio.sleep(2)
                continue

            battle = state.get("battle", {})
            nav = state.get("navigation", {})
            bstate = battle.get("battleState", "")
            phase = nav.get("currentUIPhase", "")
            scene = nav.get("activeScene", "")

            if bstate == "Battle":
                return {"success": True, "phase": battle.get("phase")}

            if phase == "BattleSetting" or bstate == "Setting":
                await _safe_post({"action": "startBattle"})
                await asyncio.sleep(3)
                continue

            if scene == "Story":
                await _safe_post({"action": "skipStory"})
                await asyncio.sleep(2)
                continue

            await asyncio.sleep(2)

        return {"success": False, "message": f"Battle did not start within {wait_seconds}s"}

    @mcp.tool()
    async def battle_loop(max_rounds: int = 60) -> dict[str, Any]:
        """Auto-play through an entire battle until it ends.

        Handles: round-start dice, auto card placement, emotion card
        selection, phase stuck detection, and story skips.

        Emotion card strategy: calls get_emotion_candidates, picks the
        Positive card with highest emotionLevel. For smarter selection,
        the LLM can interrupt and call select_emotion_card manually.

        Returns {"result": "victory"|"timeout"|"not_in_battle", "rounds": N}.
        """
        stuck_phase = ""
        stuck_count = 0
        _recent_phases: list[str] = []

        for round_num in range(max_rounds):
            state = await _get_state()
            if state is None:
                await asyncio.sleep(2)
                continue

            battle = state.get("battle", {})
            nav = state.get("navigation", {})
            phase = battle.get("phase", "")
            scene = nav.get("activeScene", "")

            # Victory / Defeat
            if phase in ("EndBattle", "EndBattle2"):
                player_alive = battle.get("playerAliveCount", 0)
                enemy_alive = battle.get("enemyAliveCount", 0)
                if enemy_alive == 0 and player_alive > 0:
                    result = "victory"
                elif player_alive == 0:
                    result = "defeat"
                else:
                    result = "ended"
                # Auto-handle battle result UI before returning
                await asyncio.sleep(2)
                await _safe_post({"action": "clickBattleResult"})
                await asyncio.sleep(2)
                await _safe_post({"action": "closeBattleScene"})
                return {"result": result, "rounds": round_num, "phase": phase,
                        "playerAlive": player_alive, "enemyAlive": enemy_alive}

            # Battle ended but phase not yet EndBattle (scene transition)
            if not battle.get("inBattle"):
                # Check if battle result UI is showing
                if scene == "Battle":
                    # Still in battle scene, might be result UI
                    await asyncio.sleep(2)
                    click_r = await _safe_post({"action": "clickBattleResult"})
                    await asyncio.sleep(1)
                    await _safe_post({"action": "closeBattleScene"})
                    await asyncio.sleep(2)
                return {"result": "ended", "rounds": round_num, "phase": phase,
                        "scene": scene}

            # Skip stories
            if scene == "Story" or phase == "BattleStoryPhase":
                await _safe_post({"action": "skipStory"})
                await asyncio.sleep(1)
                # Try advancing story multiple times (some stories need several clicks)
                await _safe_post({"action": "advanceStory"})
                await asyncio.sleep(2)
                _recent_phases.clear()
                continue

            # Track stuck phases (detect oscillation within a set of phases)
            _recent_phases.append(phase)
            if len(_recent_phases) > 6:
                _recent_phases.pop(0)
            # If the last 6 phases are cycling through the same 1-3 phases, we're stuck
            unique_recent = set(_recent_phases)
            if phase in unique_recent and len(_recent_phases) >= 4:
                phase_counts = {}
                for p in _recent_phases:
                    phase_counts[p] = phase_counts.get(p, 0) + 1
                # If any single phase appeared 3+ times in last 6 iterations
                stuck_count = max(phase_counts.values()) if phase_counts else 0
            else:
                stuck_count = 0

            # RoundEndPhase: emotion card selection
            if phase == "RoundEndPhase":
                if stuck_count >= 2:
                    # Check if emotion UI is actually active before selecting
                    emo_result = _result(await _safe_post({"action": "getEmotionCandidates"}))
                    if isinstance(emo_result, dict) and emo_result.get("active"):
                        best_idx = await _pick_best_emotion_card()
                        await _safe_post({"action": "selectEmotionCard", "index": best_idx})
                        await asyncio.sleep(2)
                        _recent_phases.clear()
                        continue
                    # No emotion UI active, try force-advance
                    await _safe_post({"action": "forceAdvancePhase", "phase": phase})
                    await asyncio.sleep(2)
                    _recent_phases.clear()
                    continue
                await asyncio.sleep(3)
                continue

            # Force-advance stuck phases (ArrangeEquippedCards etc.)
            if stuck_count >= 3:
                await _safe_post({"action": "forceAdvancePhase", "phase": phase})
                await asyncio.sleep(2)
                _recent_phases.clear()
                continue

            # Round start phases
            if phase == "RoundStartPhase_UI":
                await _safe_post({
                    "action": "callMethod", "type": "StageController",
                    "method": "SkipRoundStartUI",
                })
                await asyncio.sleep(2)
                continue

            if phase == "RoundStartPhase_System":
                await _safe_post({
                    "action": "callMethod", "type": "StageController",
                    "method": "StopSpeedDiceRoll",
                })
                await asyncio.sleep(2)
                continue

            # Card placement phase
            if phase == "ApplyLibrarianCardPhase":
                await _safe_post({"action": "playBattleRound"})
                await asyncio.sleep(2)
                continue

            # ArrangeEquippedCards: transient, auto-transitions
            if phase == "ArrangeEquippedCards":
                await asyncio.sleep(1)
                continue

            # Default: wait for animations
            await asyncio.sleep(3)

        return {"result": "timeout", "rounds": max_rounds}

    @mcp.tool()
    async def select_emotion_card(index: int) -> dict[str, Any]:
        """Select an emotion card by candidate index.

        Call get_emotion_candidates first to see the options, then
        call this with the chosen index (0-based).

        Handles SelectOne cards by auto-targeting the first alive player.
        """
        return _result(await _safe_post({"action": "selectEmotionCard", "index": index}))

    @mcp.tool()
    async def play_round() -> dict[str, Any]:
        """Play one battle round: stop dice -> auto-play cards -> confirm.

        Waits for ApplyLibrarianCardPhase before acting. Use this for
        fine-grained control instead of battle_loop.
        """
        for _ in range(15):
            state = await _get_state()
            if state is None:
                await asyncio.sleep(2)
                continue
            phase = state.get("battle", {}).get("phase", "")
            if phase == "RoundStartPhase_UI":
                await _safe_post({
                    "action": "callMethod", "type": "StageController",
                    "method": "SkipRoundStartUI",
                })
                await asyncio.sleep(2)
                continue
            if phase == "RoundStartPhase_System":
                await _safe_post({
                    "action": "callMethod", "type": "StageController",
                    "method": "StopSpeedDiceRoll",
                })
                await asyncio.sleep(2)
                continue
            if phase == "ApplyLibrarianCardPhase":
                break
            return {"success": True, "acted": False, "phase": phase}
        else:
            return {"success": False, "message": "Timed out waiting for card phase"}

        # Atomic autoPlay + confirmCards
        result = await _safe_post({"action": "playBattleRound"})
        return {"success": True, "acted": True, "result": _result(result)}

    @mcp.tool()
    async def skip_story() -> dict[str, Any]:
        """Skip the current story/event scene."""
        return await _post_action({"action": "skipStory"})

    @mcp.tool()
    async def click_battle_result() -> dict[str, Any]:
        """Click the battle result button after a battle ends."""
        return await _post_action({"action": "clickBattleResult"})

    @mcp.tool()
    async def close_battle_scene() -> dict[str, Any]:
        """Close the battle scene and return to the main UI."""
        return await _post_action({"action": "closeBattleScene"})

    # ════════════════════════════════════════════
    #  DEBUG: Reflection & Cheats
    # ════════════════════════════════════════════

    @mcp.tool()
    async def list_methods(object_type: str) -> dict[str, Any]:
        """List available public methods on a game object type via reflection."""
        return await _post_action({"action": "listMethods", "type": object_type})

    @mcp.tool()
    async def call_method(
        object_type: str,
        method_name: str,
        args: str = "",
    ) -> dict[str, Any]:
        """Call an arbitrary method on a game object via reflection.

        For advanced debugging. args is a JSON array of typed arguments,
        e.g. '[{"type":"string","value":"6"}]'.

        Common targets:
          - StageController: SkipRoundStartUI, StopSpeedDiceRoll,
            CompleteApplyingLibrarianCardPhase, KillAllEnemy
          - UIController: OnClickGameStart, PrepareBattle
        """
        body: dict[str, Any] = {
            "action": "callMethod",
            "type": object_type,
            "method": method_name,
        }
        if args:
            try:
                body["args"] = json.loads(args)
            except (json.JSONDecodeError, TypeError):
                body["args"] = args
        return await _post_action(body)

    @mcp.tool()
    async def kill_all_enemy() -> dict[str, Any]:
        """Kill all enemy units (debug cheat). For testing only."""
        return await _post_action({"action": "killAllEnemy"})


# ── Internal helpers (not exposed as tools) ──

async def _pick_best_emotion_card() -> int:
    """Get emotion candidates and return the best card index.

    Heuristic: prefer Positive state, then highest emotionLevel.
    The LLM can override this by calling select_emotion_card directly.
    """
    result = _result(await _safe_post({"action": "getEmotionCandidates"}))
    if not isinstance(result, dict) or not result.get("active"):
        return 0
    cards = result.get("candidates", [])
    if not cards:
        return 0
    best = max(cards, key=lambda c: (
        1 if c.get("state") == "Positive" else 0,
        c.get("emotionLevel", 0),
    ))
    return best.get("index", 0)
