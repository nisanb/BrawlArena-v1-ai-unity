---
name: parallel-work-orders
description: Run multiple coding agents in parallel on ONE working tree using a design doc with exclusive file ownership and pre-declared cross-agent API contracts. Use for any multi-subsystem change wave (3+ agents touching different parts of the codebase simultaneously).
---

# Parallel work orders on a shared working tree

Used for the 6-agent gameplay rework and two later fix waves. Compiled clean on the first
try once; the seams listed below caused every failure that did occur.

## The design doc (write it BEFORE launching agents)

One doc containing, per order: an EXCLUSIVE owned-file list (including test files and files
to delete), numbered steps, and a shared **API contracts** section — exact signatures any
agent must implement that another agent will call. Agents are told: "call the contract
exactly as written even though it doesn't exist in your checkout yet; the diffs land together."
Also include hard constraints (no compiling/running, .meta handling, style rules) — agents
respect these well.

## Ownership rules that mattered

- Every file that REFERENCES something an agent deletes must be in someone's list — grep for
  member names before deleting. Two out-of-list one-line compile fixes were still needed;
  agents handled it best when told "minimal necessary substitution only, report it as a deviation".
- Tests that grep source files for literal strings (this repo does it a lot) break when the
  string owner rewords something. Name those couplings in the constraints section
  (e.g. "MobileCombatRulesEditModeTests greps BrawlHUD for 'CONTROL ZONE - FIRST TO '").
- Sequencing: run dependent orders (wiring/integration) as a second phase gated on the
  producers finishing; pass the producers' summaries into the second-phase prompt.

## Expect these seams at the central fixup pass (budget ~30 min)

1. Interface additions: implementers living in files nobody owned (test doubles in editor
   test files) won't be updated.
2. Contract properties: an agent may implement most but not all of a contract — grep for
   every contract member before compiling.
3. "Already done" confusion: after a crashed/resumed run, agents find pre-existing partial
   edits; instruct them to verify-and-continue rather than redo (goal-stated orders converge).
4. Report mining: every agent's `deviations` and `risks` lists are where the real integration
   bugs hide — read them all before compiling, fix the named cross-boundary items preemptively.

## Verification

Central pass owns ALL execution: compile barrier, full test suite, scene rebuilds, playtest
(see unity-verify skill). Fix small seams inline; dispatch fresh focused agents only for
clustered failures (give them exact failing test names + messages + the new intended behavior,
and forbid weakening test intent).
