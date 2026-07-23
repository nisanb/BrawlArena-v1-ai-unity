#!/usr/bin/env python3
"""
Crownfall remote admin - local web tool.

Serves a small single-page UI (index.html) and proxies player-data reads/writes
to Unity Cloud Save through the UGS CLI (ugs.exe). It reuses your existing
`ugs login` (Unity Hub) session, so NO secrets are stored here. It binds to
127.0.0.1 only, so nothing is exposed to your network.

Run:
    python admin-server.py           # then open http://127.0.0.1:8787
    python admin-server.py --port 9000

One-time (if not done already):
    ugs login
    ugs config set project-id 614df5b7-ad0d-414b-93ac-d88b7b8a51f2
    ugs config set environment-name production
"""
import argparse
import json
import os
import subprocess
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

KEY = "player_state"
HERE = os.path.dirname(os.path.abspath(__file__))


def find_ugs():
    candidates = [
        os.path.join(os.path.expanduser("~"), ".local", "bin", "ugs.exe"),
        os.path.join(os.path.expanduser("~"), ".local", "bin", "ugs"),
        "ugs.exe",
        "ugs",
    ]
    for c in candidates:
        if os.path.sep in c and os.path.exists(c):
            return c
    return "ugs"  # rely on PATH


UGS = find_ugs()


def run_ugs(args):
    """Run the CLI with args as a list (correct quoting, no shell). Returns stdout."""
    proc = subprocess.run([UGS] + args, capture_output=True, text=True)
    if proc.returncode != 0:
        raise RuntimeError(f"ugs {' '.join(args)} failed:\n{proc.stderr.strip()}")
    return proc.stdout


def list_players():
    out = run_ugs(["player", "list", "-j"])
    data = json.loads(out)
    results = data.get("Players", {}).get("results", []) or data.get("results", [])
    return [
        {"id": p["id"], "createdAt": p.get("createdAt"), "lastLoginAt": p.get("lastLoginAt")}
        for p in results
    ]


def get_snapshot(player_id):
    out = run_ugs(["cloud-save", "data", "player", "get",
                   "--player-id", player_id, "--keys", KEY, "-j"])
    data = json.loads(out)
    items = data.get("Items") or data.get("items") or []
    for it in items:
        if it.get("key") == KEY:
            val = it.get("value")
            # value is stored as a structured object; the CLI may return it as an
            # object or (older records) as a string - handle both.
            if isinstance(val, str):
                try:
                    return json.loads(val)
                except Exception:
                    return None
            return val
    return None


def cloud_rev(player_id):
    snap = get_snapshot(player_id)
    try:
        return int(snap.get("rev", 0)) if snap else 0
    except Exception:
        return 0


def save_snapshot(player_id, snapshot):
    # Bump rev past whatever is currently in the cloud so this write wins the
    # player's next pull, even if the UI's copy was slightly stale.
    snapshot["rev"] = max(int(snapshot.get("rev", 0)), cloud_rev(player_id)) + 1
    snapshot["playerId"] = player_id
    payload = json.dumps(snapshot, separators=(",", ":"))
    run_ugs(["cloud-save", "data", "player", "set",
             "--player-id", player_id, "--key", KEY, "--value", payload])
    return snapshot["rev"]


DEFAULT_SNAPSHOT = {
    "rev": 0, "utc": 0, "playerId": "",
    "ints": [
        {"k": "meta.gems", "v": 30}, {"k": "meta.coins", "v": 120},
        {"k": "meta.trophies", "v": 0}, {"k": "meta.level", "v": 1},
        {"k": "meta.xp", "v": 0}, {"k": "meta.selectedClass", "v": 0},
        {"k": "meta.sigilsOwned", "v": 0}, {"k": "meta.hasProfile", "v": 0},
    ],
    "strs": [],
}


class Handler(BaseHTTPRequestHandler):
    def _send(self, code, body, ctype="application/json"):
        data = body if isinstance(body, bytes) else body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _json(self, code, obj):
        self._send(code, json.dumps(obj))

    def log_message(self, *a):
        pass  # quiet

    def do_GET(self):
        u = urlparse(self.path)
        try:
            if u.path in ("/", "/index.html"):
                with open(os.path.join(HERE, "index.html"), "rb") as f:
                    self._send(200, f.read(), "text/html; charset=utf-8")
            elif u.path == "/api/players":
                self._json(200, {"players": list_players()})
            elif u.path == "/api/get":
                pid = parse_qs(u.query).get("playerId", [""])[0]
                if not pid:
                    return self._json(400, {"error": "playerId required"})
                self._json(200, {"snapshot": get_snapshot(pid)})
            else:
                self._json(404, {"error": "not found"})
        except Exception as e:
            self._json(500, {"error": str(e)})

    def do_POST(self):
        u = urlparse(self.path)
        try:
            length = int(self.headers.get("Content-Length", 0))
            body = json.loads(self.rfile.read(length) or b"{}")
            if u.path == "/api/save":
                pid = body["playerId"]
                rev = save_snapshot(pid, body["snapshot"])
                self._json(200, {"ok": True, "rev": rev})
            elif u.path == "/api/reset":
                pid = body["playerId"]
                snap = json.loads(json.dumps(DEFAULT_SNAPSHOT))
                rev = save_snapshot(pid, snap)
                self._json(200, {"ok": True, "rev": rev})
            else:
                self._json(404, {"error": "not found"})
        except Exception as e:
            self._json(500, {"error": str(e)})


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=8787)
    args = ap.parse_args()
    # Fail fast if the CLI isn't logged in, with a helpful message.
    try:
        run_ugs(["player", "list", "-j"])
    except Exception as e:
        print("Could not reach Unity Cloud Save via the UGS CLI.\n"
              "Make sure you have run:  ugs login\n"
              "and set the project/env config (see README).\n\nDetail: " + str(e))
        sys.exit(1)
    srv = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    print(f"Crownfall Admin running at  http://127.0.0.1:{args.port}")
    print("Press Ctrl+C to stop.")
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        print("\nstopped.")


if __name__ == "__main__":
    main()
