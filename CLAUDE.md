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


## When In Doubt

The north star is a single image: African savanna at night, baobab tree, 
hidden animals, vast sky. If a proposed implementation makes the world 
feel more like a dashboard or a game with rules, it is wrong.

Ask before implementing any feature that:
- Gives the user more control over world state
- Makes world behavior more predictable
- Adds UI that exposes internal parameters

## Current Priority
- Developing sentiment-Engine.