# BrawlArena agent workflow

## Unity implementation delegation

- Delegate every Unity project implementation to the project custom agent `unity_developer`. This includes gameplay and UI code, bug fixes, refactors, tests, editor tooling, packages, project settings, scenes, prefabs, materials, animation, and other asset changes.
- Give `unity_developer` a concrete scoped task with the requested outcome, constraints, acceptance criteria, relevant context, and expected validation. Wait for its handoff before presenting the implementation as complete.
- Route implementation follow-ups discovered during review or validation back to `unity_developer` as well. The primary agent may inspect, coordinate, review, and independently validate, but should not bypass the implementation agent by editing Unity project files directly.
- Use one `unity_developer` implementation owner by default. Spawn additional agents only when the user explicitly requests delegation or parallel agent work.
- If the custom agent cannot be invoked, report that limitation before making Unity implementation changes unless the user explicitly authorizes a fallback.
