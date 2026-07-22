# AGENTS.md — BrawlArena agent operations guide

How AI agents (Claude Code and others) drive this project. Rewritten 2026-07-22 after Unity
made MCP free and shipped the Unity CLI — this supersedes the harness-first methodology.

## Editor control: channel hierarchy

Use the highest channel that is currently reachable. Probe before relying on any of them.

### 1. Unity CLI (primary)

Standalone binary at `%LOCALAPPDATA%\Unity\bin\unity.exe` (on PATH in new shells), installed
2026-07-22, channel `beta` (1.0.0-beta.2). Free — no Unity AI subscription required, no
concurrency limits. Auth comes from the Unity Hub keyring login (already signed in).

```powershell
unity status --format json            # live state of connected editors (exit 6 = none reachable)
unity command                         # list ~150 commands the running editor exposes
unity command editor_play             # enter play mode (also editor_pause / editor_stop / editor_status)
unity command screenshot --output ./shot.png --width 1280 --height 720   # game view capture
unity command recompile               # trigger script compilation — NO editor focus needed
unity command package_resolve         # resolve manifest.json changes — NO editor focus needed
unity command run_tests               # + test_status / cancel_tests / list_tests
unity command eval                    # C# eval in the editor (also eval_file)
unity list --format json              # full tool schemas, incl. custom [CliCommand] tools
unity pipeline list --format json     # which editors have the Pipeline package + reachability
```

The command surface (verified 2026-07-22) covers scenes, prefabs, components/serialized fields,
assets, packages, animator/timeline authoring, navmesh/lighting/occlusion bakes, build
profiles, console, hot reload, and **in-editor input simulation** (`simulate_key`,
`simulate_pointer`) — virtual events into the game view only, which do not violate the
no-OS-input rule and may replace autopilot bots for some playtest scenarios.

- Requires `com.unity.pipeline` in `Packages/manifest.json` (installed: `0.3.1-exp.1`) **and**
  the editor to have resolved it (the in-editor Pipeline server must be up).
- Always pass `--format json --no-banner --non-interactive` when parsing output.
- The project's own `[CliCommand]` methods become CLI commands automatically — prefer adding
  those over new harness actions.

### 2. MCP servers (in-session tools)

Two servers registered in `~/.claude.json`:

- **`unity-editor-mcp`** (new, preferred): stdio server, `unity mcp --project-path C:\Users\sk8r\BrawlArena`.
  Exposes the same commands as `unity command` as MCP tools. Free, no entitlement gate.
  Needs a Claude Code session restart (or `/mcp`) to appear after first registration.
- **`unity-mcp`** (legacy assistant bridge): named-pipe bridge inside `com.unity.ai.assistant`.
  Works again from package **2.16.0-pre.1** ("MCP and gateway connections are no longer capped
  or gated by entitlement limits"). Approve once in Project Settings > AI > Unity MCP.

### 3. File harness (fallback only)

`Automation/command.json` polled by `Assets/Editor/BrawlAutomation/BrawlAutomationRunner.cs`.
Keep for: unattended play-test pumping while the editor is unfocused (its EditorApplication.update
pump steps frames when nothing else does), and as the channel of last resort when both MCP and
the CLI pipeline server are down. Do not grow it further — new automation belongs in
`[CliCommand]` tools.

## Hard-won diagnostics (2026-07-22)

**"Connection revoked" is not an approval problem.** The 2-week outage was an entitlement
denial: the licensing client returned `com.unity.editor.ai → granted: False` (404, zero
entitlement groups) because the seat had reverted to Unity Personal after the AI subscription
ended. Clicking Accept in Project Settings can never fix that. Diagnose with:

- `%LOCALAPPDATA%\Unity\Unity.Entitlements.Audit.log` — per-entitlement grant/deny audit trail
- `%LOCALAPPDATA%\Unity\Unity.Licensing.Client.log` — licensing client traffic, token refreshes
- `%LOCALAPPDATA%\Unity\Editor\Editor.log` — bridge handshake, `grep -iE "mcp|entitlement|Licensing"`

If the handshake reaches `Sent handshake (... tools=52)` and then dies at
`ValidateAndApproveAsync` with a Licensing 404, it's a seat/entitlement problem — check the
account, not the toggle. (Since 2.16.0-pre.1 this gate is gone entirely.)

**Embedded packages freeze bugs in place.** `Packages/<name>/` (source: embedded) overrides the
registry version and never updates. The assistant package was embedded at 2.13.0-pre.2 since the
initial commit, which is why the entitlement-gate fix never arrived. Prefer registry versions in
`manifest.json`; only embed to patch, and record why + when to un-embed. Registry metadata
(versions + changelogs, no auth): `https://packages.unity.com/<package-id>`.

**Editor package resolution / recompile without focus.** Editing `manifest.json` or scripts
does nothing until something triggers a refresh. With the pipeline server up, use
`unity command package_resolve` / `unity command recompile` (+ `recompile_status`) — the old
focus dance is obsolete. Only for the very first bootstrap (pipeline package itself not yet
resolved) is a focus nudge needed: Win32 AttachThreadInput+SetForegroundWindow on the editor
window is fine (it's window management, not input simulation); OS-level mouse/keyboard events
remain forbidden.

**Fresh PATH.** Shells spawned by the agent predate the CLI install; either refresh PATH from
the registry or call `%LOCALAPPDATA%\Unity\bin\unity.exe` directly.

## Standard loops

- **Verify a change wave** (`unity-verify` skill has full pitfalls): compile via editor focus /
  `refresh` → check `Editor.log` for `error CS` → `unity test` (EditMode) → play-test via
  `unity command editor_play` + `screenshot` → review evidence in motion (`motion-review` skill).
- **Playtest & feel**: `persona-playtest` skill — capture evidence, 3 persona reviews, fix, re-review.
- **Parallel work**: `parallel-work-orders` skill — exclusive file ownership + pre-declared contracts.
- **Live services / backend**: `build-live-game` skill.

## Current state (2026-07-22, all channels VERIFIED)

- Editor: Unity 6000.3.7f1, project open, seat = Unity Personal (group 18966787269994).
- `com.unity.ai.assistant`: **2.16.0-pre.1** from registry — old `unity-mcp` MCP bridge
  verified working again (first successful call since the entitlement outage).
- `com.unity.pipeline`: 0.3.1-exp.1 resolved; pipeline server ready on port 7800.
- Unity CLI: 1.0.0-beta.2, authenticated; `unity command screenshot` verified with a real
  game-view capture (`Automation/cli-mcp-verify.png`).
- Embedded 2.13.0-pre.2 backup sits in the 2026-07-22 session scratchpad — safe to delete.
