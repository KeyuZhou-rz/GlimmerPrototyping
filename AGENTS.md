# Glimmer Repository Instructions

This is a Unity project, not a conventional .NET application. `CLAUDE.md` contains the full design constitution; `.claude/skills/` and `.claude/workflows/` contain the detailed checklists. Keep this file as the compact OpenCode-facing mirror of those rules.

## Project facts

- Use Unity `6000.2.5f1` from `ProjectSettings/ProjectVersion.txt`.
- The MCP package is a machine-local dependency in `Packages/manifest.json` (`C:/Users/Z2005/unity-mcp/MCPForUnity`); do not replace it with a guessed registry version.
- Current code paths are `Assets/Emotion_engine_development/` (L1 where present), `Assets/GlimmerDiary/Scripts/{Core,Data,Utils}/` (L2), `Assets/Script/` (scene-facing L3 binders), and `Assets/Scripts/Lsystemv2/` (current 3D flora). `Assets/Scripts/Lsystem/` is legacy v1 and is still referenced by scenes; do not delete it without migrating those references.
- Interaction decisions belong to `Docs/交互设计工作台_2026-07-21_矩阵补全.md` (the single merged worksheet), especially its design matrix and slice checklist. Do not invent cross-mechanism relationships or fill designer-owned blanks.

## Non-negotiable design rules

- The world runs independently of the latest journal input; do not turn it into an immediate input-to-state reaction.
- L1 accepts raw journal text and returns normalized `EmotionVector` dimensions (`valence`, `arousal`, `temporality`, `sociality`, `certainty`) without Unity API or world-state access.
- L2 receives an `EmotionVector` as a parameter and owns world state, the natural clock, and events; it must not pull from L1.
- L3 visual/audio code reads world state only, binds to inertial `E_env` rather than raw input or `E_current`, smooths its presentation, and never writes simulation state.
- `E_env` must approach `E_current` gradually (current target alpha is about `0.1` to `0.3`); never assign raw sentiment directly to world or VFX parameters.
- Every new mechanism must name its existing inputs, single writer, downstream readers, and player-visible trace. Simulation that leaves no visible evidence is not a shipped mechanism.
- Permanent world events, permanent damage, and emotion history are append-only. Never add production reset, undo, clear, delete, or time-travel behavior.
- Do not add retention pressure, daily rewards, notification pressure, progress bars, social features, or UI that exposes internal emotion/world values. The intended interaction is fixed-stage viewing with click-to-approach trace inspection.

## Boundaries and ownership

- Preserve the `WorldManager.SimulatePass()` order: TranslationLayer -> WorldEnvironmentSystem -> location water/soil propagation -> VegetationSystem -> AnimalDriveSystem -> EmergentMomentDetector -> BehaviorNarrator -> NarrativeRuleEngine -> EntityRelationSystem.
- Keep single-writer ownership. In particular, `AnimalDriveSystem` owns animal internal state/behavior, `NarrativeRuleEngine` owns permanent damage/event rules, `EmotionInertiaSystem` owns `E_env`, and `WorldAtmosphereBinder` owns presentation-layer shader/visual pushes.
- Cross-entity animal reads use the tick snapshot protocol: snapshot at tick start, read snapshots only, write only the current entity, and advance causal chains one hop per tick.
- `SaveSystem` is the only production file-I/O and JSON persistence path. New persisted fields need defaults, `WorldInitializer.CreateNewWorld()` updates, and compatibility checks; do not remove, rename, or change the type of existing persisted fields.
- Any new/changed data field, serialized field, ScriptableObject tuning field, save format, or public/internal signature requires a `data-propose` proposal and user approval before implementation.
- Visual assets and prefab changes require explicit scope: creating an asset may proceed after informing the user; modifying existing assets needs a plan and approval; prefab/scene structure changes are not default work.
- Use the current `Lsystemv2` flora path for new 3D plant work. Mesh generation must not run synchronously in `Update()`; use precomputation or the C# Job System, and use `MaterialPropertyBlock` rather than cloning materials.

## Verification and handoff

- After C# changes, run `dotnet build Assembly-CSharp.csproj --no-restore`; include `Assembly-CSharp-Editor.csproj --no-restore` when editor scripts are affected. Treat existing warnings separately from new errors.
- Refresh/recompile in Unity and inspect the Console. Use the existing `GlimmerDiary/*` editor smoke tests and `WorldSimulationTester` context-menu scenarios for focused simulation checks; use Unity Test Framework tests when present.
- Before handoff, inspect the diff, check layer boundaries and naming/style against `.claude/skills/layer-boundary-check.md` and `.claude/skills/code-style-check.md`, and report failures rather than weakening the tested code.
- For each change batch, append `Docs/更改_YYYY-MM-DD.md` with player-facing rationale, a file/change table, ownership rows when relevant, and verification results. Update the relevant design document when implementation changes its status.
- Explain progress and results from the player's seat first: describe what the world now looks or feels like, then give the technical cause.
