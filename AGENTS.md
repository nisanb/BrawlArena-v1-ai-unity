# BrawlArena agent workflow

## Unity implementation delegation

- Delegate every Unity project implementation to the project custom agent `unity_developer`. This includes gameplay and UI code, bug fixes, refactors, tests, editor tooling, packages, project settings, scenes, prefabs, materials, animation, and other asset changes.
- Give `unity_developer` a concrete scoped task with the requested outcome, constraints, acceptance criteria, relevant context, and expected validation. Wait for its handoff before presenting the implementation as complete.
- Route implementation follow-ups discovered during review or validation back to `unity_developer` as well. The primary agent may inspect, coordinate, review, and independently validate, but should not bypass the implementation agent by editing Unity project files directly.
- Use one `unity_developer` implementation owner by default. Spawn additional agents only when the user explicitly requests delegation or parallel agent work.
- If the custom agent cannot be invoked, report that limitation before making Unity implementation changes unless the user explicitly authorizes a fallback.
- Use PowerShell to navigate, focus, capture, and otherwise drive the Unity Editor when it is useful for implementation or validation; combine it with Unity MCP and native Invector editor tooling instead of limiting work to source-code edits.

No Over-Engineering: Choose the simplest appropriate solution that fully and reliably meets the specific requirements and fits the existing architecture. Avoid premature abstractions, unnecessary layers, hypothetical edge-case handling, and functionality that is not required for the current task. Account for real and likely edge cases, but do not add complexity for purely hypothetical future scenarios. Keep changes small, direct, and easy to understand. Use more complex approaches only when the actual requirements make them necessary.
