# Crownfall — Remote Player Admin (UGS CLI)

Two ways to administrate player accounts:

1. **In-game admin console** (works on-device, no setup) — in any build, tap the
   **top-right corner 5x**, enter the PIN, and you get a live console to set
   coins/gems/trophies/level/xp, unlock sigils, drive quests, and push/pull the
   cloud. In the Unity Editor, press **F8**. Fastest path for your own device.

2. **Remote admin via the UGS CLI** (this folder) — read or reset **any** player's
   cloud-saved account from your PC. Use it to help a tester, wipe a corrupted
   save, or grant currency to an account that isn't in front of you.

Each player's whole account lives in one Cloud Save item, key **`player_state`**,
stored as a structured JSON object (see `CrownfallCloud.cs`). These scripts read
and edit that object.

---

## One-time setup

**No service account needed** — the CLI logs in with your Unity Hub account.

```powershell
# 1. (already downloaded this session to ~/.local/bin/ugs.exe; to reinstall:)
curl -sL -o "$HOME/.local/bin/ugs.exe" `
  "https://github.com/Unity-Technologies/unity-gaming-services-cli/releases/latest/download/ugs-windows-x64.exe"

# 2. Log in (uses Unity Hub credentials while Hub is running)
& "$HOME/.local/bin/ugs.exe" login

# 3. Point the CLI at this project + environment (once; it's remembered)
& "$HOME/.local/bin/ugs.exe" config set project-id 614df5b7-ad0d-414b-93ac-d88b7b8a51f2
& "$HOME/.local/bin/ugs.exe" config set environment-name production
```

> If you ever run this on a machine without Unity Hub, create a service account in
> the Unity Cloud dashboard (Administration > Service Accounts, roles: **Cloud Save
> Editor** + **Player Authentication Viewer**) and `ugs login` will prompt for its
> Key ID / Secret instead.

---

## Usage

```powershell
cd Tools/UgsAdmin

# List every player id in the project
./ugs-admin.ps1 players

# Show a player's whole account
./ugs-admin.ps1 get -PlayerId QxYtzuN1Ih4DjDgLuccPInK58vXP

# Set a field to an exact value
./ugs-admin.ps1 set -PlayerId QxYtzuN1Ih4DjDgLuccPInK58vXP -Field meta.gems -Value 500

# Add to a field (grant currency)
./ugs-admin.ps1 grant -PlayerId QxYtzuN1Ih4DjDgLuccPInK58vXP -Field meta.coins -Value 1000

# Wipe a player back to first-run defaults
./ugs-admin.ps1 reset -PlayerId QxYtzuN1Ih4DjDgLuccPInK58vXP
```

The player id is shown in the in-game admin console (`playerId`), printed at boot
(`[Crownfall] Backend online. PlayerId=...`), or via `./ugs-admin.ps1 players`.

Field names are the game's own keys:
`meta.coins`, `meta.gems`, `meta.trophies`, `meta.level`, `meta.xp`,
`meta.selectedClass`, `meta.sigilsOwned`, plus `quests.p.*` / `quests.c.*` and
`trophyroad.claimed.*`. String fields (`meta.playerName`, `meta.lastGift`,
`quests.day`) are handled automatically.

### Notes

- Every `set`/`grant`/`reset` bumps the snapshot's `rev`, so the change wins the
  next time that player's device pulls. If the player is **currently in a match**,
  have them use the in-game console's **Pull now** button or relaunch to see it.
- **Don't remote-edit a player who is actively playing** — their device pushes on
  each change and can race your write. Edit while they're on the menu or offline.
- Reads/writes go to the `production` environment set in step 3.
