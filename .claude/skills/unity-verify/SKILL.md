---
name: unity-verify
description: BrawlArena's compile/test/scene-rebuild/playtest verification loop via the Automation harness and Unity MCP, with the hard-won pitfalls. Use after any code change wave, before claiming work is done, or when tests/builds behave strangely (stale assemblies, silent no-ops, paused play mode).
---

# Unity verification loop (BrawlArena)

Order: compile -> full EditMode suite -> scene rebuild -> unattended playtest. Never skip
forward: each stage's failure poisons the next.

## Compile

1. Trigger: MCP `Unity_RunCommand` executing `AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate)`.
   This is the only reliable recompile trigger while the editor is unfocused; the RunCommand's
   own "compilation successful" refers to the injected snippet, NOT the project.
2. Barrier: write a harness `ping` to `Automation/command.json` and wait for the result file —
   the runner only answers after the domain reload completes.
3. Proof: newest `Assets/**/*.cs` mtime must be OLDER than `Library/ScriptAssemblies/Assembly-CSharp*.dll`.
4. `Editor.log` "error CS" lines are APPEND-ONLY across the session — old errors persist after
   they're fixed. Confirm a suspect error against current source before acting on it.
5. Play mode DEFERS recompiles. If `isPlaying=true`, nothing compiled. Check whether the OWNER
   is playing before exiting play on their behalf (a menu idle is fair game; a live match is not).

## EditMode suite

- Delete stale `Temp/BrawlArenaFullEditModeResults.xml`, then harness `run_invector_test`
  arg `full-editmode`. The immediate `ok:true` means "started", never "passed".
- Wait for the XML to reappear, parse `total/passed/failed`; failure messages live in
  `//test-case[@result='Failed']/failure/message` (often CDATA).
- If the XML never appears: the editor is in play mode, or a compile error blocked the run.

## Scene rebuild

- Harness `build_scene` (Arena) / `build_menu`. Builders run the full roster pipeline —
  a hero builder failing aborts the scene save.
- Scene-serialized fields BEAT code defaults: if a builder assigns a component field
  (e.g. BuildCamera setting the follow offset), editing the component's default is a silent
  no-op until the builder line is changed AND the scene rebuilt.
- `build_menu` leaves MainMenu as the open scene; several tools (GameplayProbe) need Arena
  open — `open_scene` back afterward.

## Unattended playtest

- Use `play_test` (writes autopilot.flag + enters play). `enter_play` after a manual flag
  write only works because the flag is read at boot; `exit_play` DELETES the flag.
- The editor sometimes enters play PAUSED — poll `status`; if `paused=True`, unpause via MCP
  (`EditorApplication.isPaused = false`) or the match silently stalls at the menu.
- `status` gives match state + per-brawler hp/super/pos/anim (anim shows `Base+Overlay` layers).
  A brawler frozen with `objective=None` while still auto-attacking = navigation not ready;
  probe `InvectorBrawlerNavigation.IsReady` + planner drift (off-mesh bodies used to deadlock
  until the 2.5m recovery probe fix).
- Grab `game_screenshot` evidence; JobTempAlloc warnings in results are Unity-internal noise.
