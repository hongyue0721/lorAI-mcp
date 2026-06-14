"""LOR Proxy HTTP Server — bridges game Mod JSON files to HTTP API.

The game's C# HttpListener fails on Windows due to admin permissions.
This Python server reads the JSON files that RuntimeStateExport and StaticDataExport
mods write to disk, and exposes the same HTTP API on port 17127.

It also supports POST /action for executing game actions by writing
action command files that the game mod can pick up.
"""

from __future__ import annotations

import json
import os
import time
import threading
from pathlib import Path
from http.server import HTTPServer, BaseHTTPRequestHandler
from typing import Any

# ─── Paths ───

GAME_DATA_DIR = Path(r"D:\steam\steamapps\common\Library Of Ruina\LibraryOfRuina_Data")
MODS_DIR = GAME_DATA_DIR / "Mods"
STATE_DIR = MODS_DIR / "RuntimeStateExport" / "output"
STATIC_DIR = MODS_DIR / "StaticDataExport" / "output"
ACTION_DIR = STATE_DIR  # Action commands are written here
PORT = 17128

# ─── Helpers ───

def _read_json(path: Path) -> dict | list | None:
    """Read a JSON file, returning None on failure. Handles UTF-8 BOM."""
    try:
        if path.exists() and path.stat().st_size > 0:
            with open(path, "r", encoding="utf-8-sig") as f:
                return json.load(f)
    except (json.JSONDecodeError, OSError, UnicodeDecodeError):
        pass
    return None


def _safe_json(data: Any) -> str:
    """Serialize to JSON, handling non-UTF8 gracefully."""
    return json.dumps(data, ensure_ascii=False, indent=None)


# ─── Request Handler ───

class LorProxyHandler(BaseHTTPRequestHandler):
    """HTTP handler that mirrors the RuntimeStateExport HTTP bridge API."""

    def log_message(self, fmt, *args):
        """Suppress default logging for cleaner output."""
        pass

    def _send_json(self, code: int, data: Any):
        body = _safe_json(data).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()
        self.wfile.write(body)

    def do_OPTIONS(self):
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()

    def do_GET(self):
        path = self.path.rstrip("/")

        # ─── Health ───
        if path in ("", "/health"):
            self._send_json(200, {
                "status": "ok",
                "version": "0.4.0-proxy",
                "bridge_mode": "python-proxy",
                "state_file": STATE_DIR.exists(),
                "static_dir": STATIC_DIR.exists(),
                "timestamp": time.strftime("%Y-%m-%d %H:%M:%S"),
            })
            return

        # ─── Full state ───
        if path == "/state":
            data = _read_json(STATE_DIR / "full_state.json")
            if data is not None:
                self._send_json(200, data)
            else:
                self._send_json(404, {"error": "full_state.json not available"})
            return

        # ─── State layer ───
        if path.startswith("/state/"):
            layer = path[len("/state/"):].lower()
            data = _read_json(STATE_DIR / "full_state.json")
            if data and isinstance(data, dict) and layer in data:
                self._send_json(200, data[layer])
            elif data and isinstance(data, dict):
                # Try alternative keys
                for key in data:
                    if key.lower() == layer:
                        self._send_json(200, data[key])
                        return
                self._send_json(404, {"error": f"Unknown layer: {layer}"})
            else:
                self._send_json(404, {"error": "State data not available"})
            return

        # ─── Battle snapshot ───
        if path == "/battle":
            data = _read_json(STATE_DIR / "battle_snapshot.json")
            if data is not None:
                self._send_json(200, data)
            else:
                # Fall back to battle layer from full state
                full = _read_json(STATE_DIR / "full_state.json")
                if full and "battle" in full:
                    self._send_json(200, full["battle"])
                else:
                    self._send_json(404, {"error": "Battle data not available"})
            return

        # ─── Static data list ───
        if path == "/static":
            files = []
            if STATIC_DIR.exists():
                for f in STATIC_DIR.glob("*.json"):
                    files.append({
                        "name": f.stem,
                        "size": f.stat().st_size,
                    })
            self._send_json(200, {"files": files, "count": len(files)})
            return

        # ─── Static data file ───
        if path.startswith("/static/"):
            name = path[len("/static/"):]
            file_path = STATIC_DIR / (name + ".json")
            data = _read_json(file_path)
            if data is not None:
                self._send_json(200, data)
            else:
                self._send_json(404, {"error": f"File not found: {name}"})
            return

        # ─── Action status ───
        if path == "/action-status":
            # Read pending and completed action files
            completed = {}
            pending = []
            # Check for completed action results
            for f in ACTION_DIR.glob("action_result_*.json"):
                result = _read_json(f)
                if result:
                    req_id = f.stem.replace("action_result_", "")
                    completed[req_id] = result
            # Check for pending action requests
            for f in ACTION_DIR.glob("action_request_*.json"):
                req_id = f.stem.replace("action_request_", "")
                if req_id not in completed:
                    pending.append(req_id)
            self._send_json(200, {"completed": completed, "pending": pending})
            return

        self._send_json(404, {"error": "Not found. Try /health, /state, /state/{layer}, /static, /static/{name}, /battle, /action-status"})

    def do_POST(self):
        path = self.path.rstrip("/")

        if path == "/action":
            # Read request body
            content_len = int(self.headers.get("Content-Length", 0))
            body = self.rfile.read(content_len).decode("utf-8") if content_len > 0 else ""
            try:
                action_data = json.loads(body) if body else {}
            except json.JSONDecodeError:
                self._send_json(400, {"error": "Invalid JSON body"})
                return

            req_id = f"proxy_{int(time.time())}_{id(action_data)}"
            action_file = ACTION_DIR / f"action_request_{req_id}.json"

            # Write the action request file for the game mod to pick up
            try:
                with open(action_file, "w", encoding="utf-8") as f:
                    json.dump(action_data, f, ensure_ascii=False)
            except OSError as exc:
                self._send_json(500, {"error": f"Failed to write action file: {exc}"})
                return

            # Wait a short time for the result (the game mod processes on Update)
            # In practice, we return immediately with a "pending" status,
            # and the caller should check /action-status for completion.
            self._send_json(200, {
                "status": "pending",
                "action": action_data,
                "reqId": req_id,
                "message": f"Action request written to {action_file.name}. The game mod will process it on next Update tick. Check /action-status for completion.",
                "note": "For immediate actions, the game processes within 1-2 frames. Poll /action-status with the reqId.",
            })
            return

        self._send_json(404, {"error": "Unknown POST endpoint. Only /action is supported."})


# ─── Background state refresh ───

class StateRefresher(threading.Thread):
    """Periodically triggers the game to refresh its state export by writing a signal file."""

    def __init__(self, interval: float = 2.0):
        super().__init__(daemon=True, name="StateRefresher")
        self.interval = interval
        self._stop = threading.Event()

    def run(self):
        while not self._stop.is_set():
            # Write a refresh signal file that UpdateHook can detect
            # This causes the game to re-export full_state.json on its next Update tick
            signal_file = STATE_DIR / "_refresh_signal.json"
            try:
                with open(signal_file, "w", encoding="utf-8") as f:
                    json.dump({"t": time.time()}, f)
            except OSError:
                pass
            self._stop.wait(self.interval)

    def stop(self):
        self._stop.set()


# ─── Main ───

def main():
    print(f"[LOR Proxy] Starting HTTP proxy server on port {PORT}")
    print(f"[LOR Proxy] State dir: {STATE_DIR}")
    print(f"[LOR Proxy] Static dir: {STATIC_DIR}")

    # Start state refresher in background
    refresher = StateRefresher(interval=1.0)
    refresher.start()

    server = HTTPServer(("127.0.0.1", PORT), LorProxyHandler)
    print(f"[LOR Proxy] Listening on http://127.0.0.1:{PORT}")
    print(f"[LOR Proxy] Endpoints: /health, /state, /state/<layer>, /static, /static/<name>, /action, /action-status")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("[LOR Proxy] Shutting down...")
    server.server_close()
    refresher.stop()


if __name__ == "__main__":
    main()