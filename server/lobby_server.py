"""
Close Encounters — Lobby / Matchmaking Server

A tiny HTTP server that lets game hosts register themselves and lets
joiners discover available games.  No database — everything lives in
memory.  Games auto-expire after 60 seconds without a heartbeat.

Endpoints:
    GET  /games              — list all active games
    POST /games              — register or heartbeat a game (JSON body)
    DELETE /games/<ip>/<port> — remove a game

Run locally for testing:
    python lobby_server.py

Deploy for internet play:
    - Render (free): add a render.yaml or Dockerfile
    - Railway (free): push this repo, set start command
    - Any VPS: just run it with Python 3.9+

The game client talks to this via LOBBY_SERVER_URL in network_manager.gd.
Change that constant to your deployed URL.

Dependencies: none beyond Python 3 stdlib (uses http.server).
"""

import json
import time
import threading
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse

# ---------------------------------------------------------------------------
# In-memory game registry
# ---------------------------------------------------------------------------

# Dict keyed by "ip:port" -> game info dict + "_last_seen" timestamp
games: dict[str, dict] = {}
games_lock = threading.Lock()

# Games expire after this many seconds without a heartbeat (re-POST).
GAME_TIMEOUT = 60


def cleanup_expired():
    """Remove games that haven't sent a heartbeat recently."""
    now = time.time()
    with games_lock:
        expired = [k for k, v in games.items() if now - v.get("_last_seen", 0) > GAME_TIMEOUT]
        for k in expired:
            del games[k]


# ---------------------------------------------------------------------------
# HTTP handler
# ---------------------------------------------------------------------------

class LobbyHandler(BaseHTTPRequestHandler):

    def do_GET(self):
        path = urlparse(self.path).path

        if path == "/games":
            cleanup_expired()
            with games_lock:
                # Strip internal fields before sending to clients.
                result = []
                for g in games.values():
                    entry = {k: v for k, v in g.items() if not k.startswith("_")}
                    result.append(entry)
            self._json_response(200, result)

        elif path == "/health":
            self._json_response(200, {"status": "ok"})

        else:
            self._json_response(404, {"error": "not found"})

    def do_POST(self):
        path = urlparse(self.path).path

        if path == "/games":
            body = self._read_body()
            if body is None:
                self._json_response(400, {"error": "invalid JSON"})
                return

            ip = body.get("ip", "")
            port = body.get("port", 7777)

            # If the client didn't send its public IP, use the request source.
            if not ip:
                ip = self.client_address[0]
                body["ip"] = ip

            key = f"{ip}:{port}"

            with games_lock:
                body["_last_seen"] = time.time()
                games[key] = body

            print(f"[lobby] Registered/heartbeat: {key}  ({body.get('name', '?')})")
            self._json_response(201, {"status": "registered", "key": key})

        else:
            self._json_response(404, {"error": "not found"})

    def do_DELETE(self):
        path = urlparse(self.path).path

        # Expect /games/<ip>/<port>
        parts = path.strip("/").split("/")
        if len(parts) == 3 and parts[0] == "games":
            key = f"{parts[1]}:{parts[2]}"
            with games_lock:
                if key in games:
                    del games[key]
                    print(f"[lobby] Unregistered: {key}")
            self._json_response(200, {"status": "removed", "key": key})
        else:
            self._json_response(404, {"error": "not found"})

    # CORS preflight for browser-based clients (just in case).
    def do_OPTIONS(self):
        self.send_response(204)
        self._cors_headers()
        self.end_headers()

    # ─── Helpers ──────────────────────────────────────────────────────────

    def _read_body(self) -> dict | None:
        length = int(self.headers.get("Content-Length", 0))
        if length == 0:
            return None
        try:
            return json.loads(self.rfile.read(length))
        except (json.JSONDecodeError, UnicodeDecodeError):
            return None

    def _json_response(self, code: int, data):
        body = json.dumps(data).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self._cors_headers()
        self.end_headers()
        self.wfile.write(body)

    def _cors_headers(self):
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")

    def log_message(self, format, *args):
        # Quieter logs — only print our own messages.
        pass


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    HOST = "0.0.0.0"
    PORT = 8080

    server = HTTPServer((HOST, PORT), LobbyHandler)
    print(f"Close Encounters lobby server running on {HOST}:{PORT}")
    print(f"Games auto-expire after {GAME_TIMEOUT}s without heartbeat.")
    print("Press Ctrl+C to stop.\n")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nShutting down.")
        server.server_close()
