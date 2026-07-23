# Crownfall — Remote Player Admin (UGS CLI)

Two ways to administrate player accounts:

1. **In-game admin console** (already built, works on-device) — in any build, tap the
   **top-right corner 5×**, enter the PIN, and you get a live console to set
   coins/gems/trophies/level/xp, unlock sigils, drive quests, and push/pull the
   cloud. In the Unity Editor, press **F8**. This is the fastest path for your own
   device.

2. **Remote admin via the UGS CLI** (this folder) — read or reset **any** player's
   cloud-saved account from your PC using a Unity service account. Use this to help
   a tester, wipe a corrupted save, or grant currency to an account that isn't on a
   device in front of you.

The player's whole account lives in one Cloud Save item, key **`player_state`**, as
a JSON blob (see `CrownfallCloud.cs`). These scripts read and overwrite that item.

---

## One-time setup

### a. Install the UGS CLI
Already downloaded to `~/.local/bin/ugs.exe` in this session. To (re)install:

```powershell
curl -sL -o "$HOME/.local/bin/ugs.exe" `
  "https://github.com/Unity-Technologies/unity-gaming-services-cli/releases/latest/download/ugs-windows-x64.exe"
```

### b. Create a service account (Unity Cloud Dashboard — one time, needs a browser)
1. Go to <https://cloud.unity.com> → your org **nisanb** → project **BrawlArena**
   (project id `614df5b7-ad0d-414b-93ac-d88b7b8a51f2`).
2. **Administration → Service Accounts → New**. Name it `crownfall-admin`.
3. Give it the **Cloud Save Editor** and **Player Authentication** roles (Editor,
   not just Viewer, so it can write).
4. Copy the generated **Key ID** and **Secret Key**.

### c. Log the CLI in
```powershell
& "$HOME/.local/bin/ugs.exe" login
# paste Key ID and Secret when prompted
```
Set the active project/environment once:
```powershell
& "$HOME/.local/bin/ugs.exe" config set project-id 614df5b7-ad0d-414b-93ac-d88b7b8a51f2
& "$HOME/.local/bin/ugs.exe" config set environment-name production
```

---

## Usage

`ugs-admin.ps1` wraps the raw CLI with friendly verbs. Every command needs the
target player's **UGS player id** (shown in the in-game admin console as
`playerId`, and printed at boot as `[Crownfall] Backend online. PlayerId=...`).

```powershell
# Show a player's whole account (pretty-printed JSON)
./ugs-admin.ps1 get -PlayerId QxYtzuN1Ih4DjDgLuccPInK58vXP

# Set a single field (bumps rev so the player's next launch pulls it down)
./ugs-admin.ps1 set -PlayerId QxYtzuN1Ih4DjDgLuccPInK58vXP -Field meta.gems -Value 500

# Grant currency (adds to the current value)
./ugs-admin.ps1 grant -PlayerId QxYtzuN1Ih4DjDgLuccPInK58vXP -Field meta.coins -Value 1000

# Wipe a player back to first-run defaults
./ugs-admin.ps1 reset -PlayerId QxYtzuN1Ih4DjDgLuccPInK58vXP
```

Field names are the same keys the game uses:
`meta.coins`, `meta.gems`, `meta.trophies`, `meta.level`, `meta.xp`,
`meta.selectedClass`, `meta.sigilsOwned`, plus `quests.*` and `trophyroad.claimed.*`.

> **Note on rev:** `set`/`grant`/`reset` all increment the snapshot's `rev` so the
> change wins the next time that player's device pulls. If the player is *currently*
> in-game, they should use the in-game console's **Pull now** button (or relaunch) to
> see it.
