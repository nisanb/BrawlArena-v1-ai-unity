# Crownfall — Player Admin (external)

Admin is **not** shipped in the game (no console, no PIN in the binary). Player
accounts are managed from your PC. Each player's whole account is one Cloud Save
item, key **`player_state`**, a structured JSON object (see `CrownfallCloud.cs`).

Two tools, both reuse your `ugs login` (Unity Hub) session — **no secrets stored**:

- **`web/` — a small web UI** (recommended): a local page to browse players, edit
  fields, grant currency, and reset accounts.
- **`ugs-admin.ps1` — a PowerShell script** for quick one-off / scriptable edits.

---

## One-time setup

```powershell
# CLI (already at ~/.local/bin/ugs.exe; to reinstall:)
curl -sL -o "$HOME/.local/bin/ugs.exe" `
  "https://github.com/Unity-Technologies/unity-gaming-services-cli/releases/latest/download/ugs-windows-x64.exe"

ugs login                                                   # uses Unity Hub creds
ugs config set project-id 614df5b7-ad0d-414b-93ac-d88b7b8a51f2
ugs config set environment-name production
```

> No service account needed while Unity Hub is running. For a headless machine,
> create one in the Unity Cloud dashboard (Administration > Service Accounts, roles
> **Cloud Save Editor** + **Player Authentication Viewer**) and `ugs login` will
> prompt for its Key ID / Secret.

---

## Web UI (recommended)

```powershell
cd Tools/UgsAdmin/web
python admin-server.py            # Python 3; then open http://127.0.0.1:8787
```

It serves a page bound to `127.0.0.1` only (not exposed to your network) that talks
to Cloud Save through the CLI. Pick a player, **Load account**, edit any field, and
**Save changes**. Quick buttons: +1000 coins, +100 gems, +100 trophies, unlock all
sigils, complete quests, reset account.

`python admin-server.py --port 9000` to change the port.

---

## Script (power / scriptable)

```powershell
cd Tools/UgsAdmin
./ugs-admin.ps1 players
./ugs-admin.ps1 get   -PlayerId <id>
./ugs-admin.ps1 set   -PlayerId <id> -Field meta.gems  -Value 500
./ugs-admin.ps1 grant -PlayerId <id> -Field meta.coins -Value 1000
./ugs-admin.ps1 reset -PlayerId <id>
```

The player id shows in each tool, and is printed by the game at boot
(`[Crownfall] Backend online. PlayerId=...`).

Field names are the game's own keys: `meta.coins`, `meta.gems`, `meta.trophies`,
`meta.level`, `meta.xp`, `meta.selectedClass`, `meta.sigilsOwned`, plus `quests.p.*`
/ `quests.c.*` and `trophyroad.claimed.*`. Strings: `meta.playerName`,
`meta.lastGift`, `quests.day`.

### Notes

- Every write bumps the snapshot `rev`, so it wins the player's next sync.
- **Don't edit a player who is mid-match** — their device pushes on each change and
  can race your write. Edit while they're on the menu or offline.
- Reads/writes target the `production` environment set during setup.
