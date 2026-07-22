## Design Philosophy

This is not a game with mechanics. It is a living world that remembers 
the user's emotional history. Every technical decision must serve one of 
three principles:

1. The world has its own life — it runs independently of user input.
   Never make world state purely reactive to the latest journal entry.

2. Emotion has inertia — E_env approaches E_current slowly (alpha 0.1~0.3).
   Never directly set world parameters from raw sentiment output.

3. Irreversible changes carry weight — certain world events cannot be undone.
   Never add undo/reset functionality to permanent world state.

4. Mechanisms must interconnect — an isolated mechanism is a dead mechanism.
   Before adding any new rule/system, name which existing fields it reads
   and which existing systems react to what it writes. A mechanism that
   only talks to itself does not ship. (Benchmark: not BotW's player-as-
   catalyst chemistry, but shared substrates + cross-system propagation.)

5. No trace, no mechanism — every causal link must leave visible evidence
   in the scene. If a chain step cannot answer "what does the player see
   of this?", the chain does not exist experientially. Simulation depth
   that lives only in JSON is zero depth. (Player never sees animals —
   only their traces; the trace IS the render.)

Design authority: mechanism-interaction design decisions belong to the
designer, made in Docs/InteractionWorksheet.md. Claude inventories facts
and implements; it does not invent cross-mechanism interactions unasked.


## Architecture: Three Layers — Never Cross Boundaries

### Layer 1: SentimentEngine
- Input: raw journal text
- Output: EmotionVector(V, A, T, S, C) — all floats, all normalized
- Rules: No Unity API calls. No world state access. Pure analysis only.

### Layer 2: WorldSimulator  
- Owns: WorldState, NaturalClock, EventSystem
- Rules: Never reads from SentimentEngine directly.
          Receives EmotionVector as parameter, never pulls it.
          All permanent changes write to PersistentWorldState (no rollback).

### Layer 3: Visual / Audio
- Rules: Reads world state only. Never writes to it.
          Binds to E_env, NOT E_current.
          VFX parameters always lag behind emotional input.

          ## Code Rules

DO:
- Use C# Job System for L-System mesh generation (CPU intensive)
- Store permanent world events in append-only log (never delete entries)
- Keep NaturalClock ticking even when app is backgrounded (use timestamps)
- Name emotion parameters by dimension: valence, arousal, temporality, 
  sociality, certainty

DON'T:
- Never expose world reset or time-travel functionality, even in debug builds
- Never bind VFX spawn rate directly to raw sentiment score
- Never generate mesh synchronously on main thread
- Don't add features that make the world more "controllable" — 
  the world's independence is a feature, not a bug

  ## Project Structure

Assets/
  _Core/
    SentimentEngine/     # Layer 1 — no Unity dependencies if possible
    WorldSimulator/      # Layer 2 — world state, clock, events
    DataPersistence/     # Append-only journal + world event log
  _Visual/
    Shaders/             # Shader Graph assets
    VFX/                 # VFX Graph assets  
    Plants/              # L-System + mesh builders
  _Audio/                # FMOD event wrappers only
  _UI/                   # Input only, never touches world state directly

## Naming Conventions
- World state variables: PascalCase with World prefix (WorldValence, WorldArousal)
- Emotion vector fields: lowercase full words (valence, arousal, temporality)
- Permanent events: past tense (TreeBranchBroke, AnimalArrived)

## Communication

- When explaining work items or progress, ALWAYS use player-facing
  language: state what the change looks/feels like from the player's
  seat before (or instead of) the internal mechanics.
  e.g. not "WorldArousal now decays via Relax toward 0.5" but
  "激烈情绪过去后，世界会慢慢平静下来，而不是瞬间复位".
  The reader must grasp the meaning of a change without knowing the
  codebase. Technical detail may follow, but never leads.

## Documentation & Commits

- Every change batch (start or completion of a piece of work) gets a
  changelog doc: `Docs/更改_YYYY-MM-DD.md`, following the established
  format — 为什么改 (player-facing), 改动清单 (file-by-file table),
  plus any ownership-table rows. Same day → append to the existing doc.
- When a change lands, update the relevant plan/design docs in place
  (e.g. InteractionWorksheet.md entries, 设计工作台 sections) so plans
  never drift from what was actually built.
- After finishing each section/chunk of work: review the diff, then
  `git add` + `git commit` automatically — don't leave work uncommitted.
- Commit messages: plain, no Claude/AI co-author or attribution lines.


## When In Doubt

The north star is a single image: African savanna at night, baobab tree, 
hidden animals, vast sky. If a proposed implementation makes the world 
feel more like a dashboard or a game with rules, it is wrong.

Interaction stance (decided 2026-07-13, benchmark: Mountain):
no retention mechanics, no daily rewards, no notifications-as-pressure,
no progress bars, no social features. The only drivers are curiosity and
emotional projection. Player camera: fixed stage view + click-to-approach
on traces (option B). Trace-anchored attention markers are allowed as
"noticing guidance" — they point at places, never expose values.

Ask before implementing any feature that:
- Gives the user more control over world state
- Makes world behavior more predictable
- Adds UI that exposes internal parameters

## Current Priority
- Developing sentiment-Engine (L1, decoupled — slice uses SubmitEmotion stubs).
- Vertical slice: see Docs/InteractionWorksheet.md §6 for the build order;
  §5 design matrix is filled by the designer, not Claude.