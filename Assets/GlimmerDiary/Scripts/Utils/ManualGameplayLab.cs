using System;
using System.Globalization;
using System.Text;
using UnityEngine;
using GlimmerDiary.Data;

namespace GlimmerDiary.Utils
{
/// <summary>
/// Developer-only gameplay lab: one Inspector command consumes the staged five-dimensional
/// vector, advances exactly one world day, persists WorldSaveData, and emits a reproducible log.
/// </summary>
public class ManualGameplayLab : MonoBehaviour
{
    private const string PLACEHOLDER_TEXT = "[manual gameplay lab]";
    private const string LOG_PREFIX = "[GameplayLab:v2]";

    [Header("手动注入情绪参数")]
    [SerializeField, Range(-1f, 1f)] private float valence;
    [SerializeField, Range(0f, 1f)] private float arousal = 0.3f;
    [SerializeField, Range(0f, 1f)] private float temporality = 1f;
    [SerializeField, Range(0f, 1f)] private float sociality;
    [SerializeField, Range(0f, 1f)] private float certainty = 0.5f;

    private enum GameplayLabState
    {
        Ready,
        Invalid
    }

    private GameplayLabState _state = GameplayLabState.Ready;
    private string _sessionId;
    private int _cycleSequence;

    private void Awake()
    {
        _state = GameplayLabState.Ready;
        _sessionId = "S-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        _cycleSequence = 0;
    }

    [ContextMenu("Gameplay Lab/Tick One Day With Current Vector")]
    private void TickOneDayWithCurrentVector()
    {
        if (!CanRun("TICK_REJECTED")) return;
        if (!TryCreateInput(out EmotionVector input, out string errorCode, out string detail))
        {
            LogRejected("TICK_REJECTED", errorCode, detail);
            return;
        }
        if (!TryGetWorld(out WorldManager world, out detail))
        {
            LogRejected("TICK_REJECTED", "WORLD_NOT_READY", detail);
            return;
        }

        string cycleId = NextCycleId();
        CaptureBaseline(world.WorldSave,
            out int preDay,
            out int preHistory,
            out int preWorldEvents,
            out int preChronicles,
            out int prePermanentDamages,
            out int prePermanentTerrain);

        LogState("PRE_TICK", cycleId, input, world);

        var entry = new JournalEntry
        {
            entryId = Guid.NewGuid().ToString(),
            realTimestamp = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
            rawText = PLACEHOLDER_TEXT,
            emotion = input
        };

        try
        {
            // The lab deliberately bypasses OnJournalSubmitted: that method would add a
            // response pass before WorldTick and make one button simulate the world twice.
            world.InjectEmotion(entry);
            world.WorldTick(1);
        }
        catch (Exception ex)
        {
            Invalidate(cycleId, "CYCLE_EXECUTION_FAILED", ex.GetType().Name);
            return;
        }

        if (!ValidatePostTick(world.WorldSave, preDay, preHistory, preWorldEvents,
                prePermanentDamages, prePermanentTerrain, expectedHistoryDelta: 1,
                out errorCode, out detail))
        {
            Invalidate(cycleId, errorCode, detail);
            return;
        }

        try
        {
            // These are two existing non-atomic files. If the second write fails, the lab
            // enters Invalid rather than retrying and risking duplicate permanent history.
            SaveSystem.SaveWorldState(world.WorldSave);
            SaveSystem.AppendJournalEntry(entry);
        }
        catch (Exception ex)
        {
            Invalidate(cycleId, "CYCLE_PERSISTENCE_UNCERTAIN", ex.GetType().Name);
            return;
        }

        LogState("POST_TICK", cycleId, input, world,
            dayDelta: world.WorldSave.gameTime.ToAbsoluteDays() - preDay,
            historyDelta: world.WorldSave.emotionHistory.Count - preHistory,
            worldEventDelta: world.WorldSave.worldEvents.Count - preWorldEvents,
            chronicleDelta: ChronicleCount(world.WorldSave) - preChronicles,
            permanentDamageDelta: PermanentDamageCount(world.WorldSave) - prePermanentDamages,
            permanentTerrainDelta: PermanentTerrainCount(world.WorldSave) - prePermanentTerrain);
    }

    [ContextMenu("Gameplay Lab/Tick One Day Without Input")]
    private void TickOneDayWithoutInput()
    {
        if (!CanRun("TICK_REJECTED")) return;
        if (!TryGetWorld(out WorldManager world, out string detail))
        {
            LogRejected("TICK_REJECTED", "WORLD_NOT_READY", detail);
            return;
        }

        string cycleId = NextCycleId();
        CaptureBaseline(world.WorldSave,
            out int preDay,
            out int preHistory,
            out int preWorldEvents,
            out int preChronicles,
            out int prePermanentDamages,
            out int prePermanentTerrain);

        LogState("PRE_TICK", cycleId, null, world);

        try
        {
            world.WorldTick(1);
        }
        catch (Exception ex)
        {
            Invalidate(cycleId, "CYCLE_EXECUTION_FAILED", ex.GetType().Name);
            return;
        }

        if (!ValidatePostTick(world.WorldSave, preDay, preHistory, preWorldEvents,
                prePermanentDamages, prePermanentTerrain, expectedHistoryDelta: 0,
                out string errorCode, out detail))
        {
            Invalidate(cycleId, errorCode, detail);
            return;
        }

        try
        {
            SaveSystem.SaveWorldState(world.WorldSave);
        }
        catch (Exception ex)
        {
            Invalidate(cycleId, "SAVE_FAILED", ex.GetType().Name);
            return;
        }

        LogState("POST_TICK", cycleId, null, world,
            dayDelta: world.WorldSave.gameTime.ToAbsoluteDays() - preDay,
            historyDelta: world.WorldSave.emotionHistory.Count - preHistory,
            worldEventDelta: world.WorldSave.worldEvents.Count - preWorldEvents,
            chronicleDelta: ChronicleCount(world.WorldSave) - preChronicles,
            permanentDamageDelta: PermanentDamageCount(world.WorldSave) - prePermanentDamages,
            permanentTerrainDelta: PermanentTerrainCount(world.WorldSave) - prePermanentTerrain);
    }

    [ContextMenu("Gameplay Lab/Log Snapshot")]
    private void LogSnapshot()
    {
        if (!Application.isPlaying)
        {
            LogRejected("SNAPSHOT_REJECTED", "NOT_PLAYING", "ENTER_PLAY_MODE");
            return;
        }
        if (!TryGetWorld(out WorldManager world, out string detail))
        {
            LogRejected("SNAPSHOT_REJECTED", "WORLD_NOT_READY", detail);
            return;
        }

        LogState("SNAPSHOT", CurrentCycleId(), null, world);
    }

    private bool CanRun(string phase)
    {
        if (!Application.isPlaying)
        {
            LogRejected(phase, "NOT_PLAYING", "ENTER_PLAY_MODE");
            return false;
        }
        if (_state == GameplayLabState.Invalid)
        {
            LogRejected(phase, "INVALID_STATE", "RESTART_PLAY_MODE");
            return false;
        }
        if (string.IsNullOrEmpty(_sessionId))
        {
            LogRejected(phase, "SESSION_NOT_READY", "ENABLE_COMPONENT_FIRST");
            return false;
        }
        return true;
    }

    private bool TryCreateInput(out EmotionVector input, out string errorCode, out string detail)
    {
        input = null;
        errorCode = null;
        detail = null;

        if (!IsFinite(valence)) return InputError("NON_FINITE_VALUE", "V_NON_FINITE", out errorCode, out detail);
        if (!IsFinite(arousal)) return InputError("NON_FINITE_VALUE", "A_NON_FINITE", out errorCode, out detail);
        if (!IsFinite(temporality)) return InputError("NON_FINITE_VALUE", "T_NON_FINITE", out errorCode, out detail);
        if (!IsFinite(sociality)) return InputError("NON_FINITE_VALUE", "S_NON_FINITE", out errorCode, out detail);
        if (!IsFinite(certainty)) return InputError("NON_FINITE_VALUE", "C_NON_FINITE", out errorCode, out detail);

        if (valence < -1f || valence > 1f) return InputError("OUT_OF_RANGE", "V_OUT_OF_RANGE", out errorCode, out detail);
        if (arousal < 0f || arousal > 1f) return InputError("OUT_OF_RANGE", "A_OUT_OF_RANGE", out errorCode, out detail);
        if (temporality < 0f || temporality > 1f) return InputError("OUT_OF_RANGE", "T_OUT_OF_RANGE", out errorCode, out detail);
        if (sociality < 0f || sociality > 1f) return InputError("OUT_OF_RANGE", "S_OUT_OF_RANGE", out errorCode, out detail);
        if (certainty < 0f || certainty > 1f) return InputError("OUT_OF_RANGE", "C_OUT_OF_RANGE", out errorCode, out detail);

        input = new EmotionVector
        {
            V = valence,
            A = arousal,
            T = temporality,
            S = sociality,
            C = certainty
        };
        return true;
    }

    private static bool InputError(string code, string message, out string errorCode, out string detail)
    {
        errorCode = code;
        detail = message;
        return false;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool TryGetWorld(out WorldManager world, out string detail)
    {
        world = WorldManager.Instance;
        if (world == null)
        {
            detail = "NO_WORLD_MANAGER";
            return false;
        }
        WorldSaveData save = world.WorldSave;
        if (save == null || save.gameTime == null || world.EmotionInertia == null ||
            world.Environment == null || save.emotionHistory == null || save.animals == null ||
            save.plants == null || save.locations == null || save.pendingChronicles == null ||
            save.shownChronicles == null || save.worldEvents == null || save.ruleCooldowns == null)
        {
            detail = "WORLD_DATA_INCOMPLETE";
            return false;
        }

        detail = null;
        return true;
    }

    private static void CaptureBaseline(WorldSaveData save,
        out int absoluteDay,
        out int history,
        out int worldEvents,
        out int chronicles,
        out int permanentDamages,
        out int permanentTerrain)
    {
        absoluteDay = save.gameTime.ToAbsoluteDays();
        history = save.emotionHistory?.Count ?? 0;
        worldEvents = save.worldEvents?.Count ?? 0;
        chronicles = ChronicleCount(save);
        permanentDamages = PermanentDamageCount(save);
        permanentTerrain = PermanentTerrainCount(save);
    }

    private static bool ValidatePostTick(WorldSaveData save,
        int preDay,
        int preHistory,
        int preWorldEvents,
        int prePermanentDamages,
        int prePermanentTerrain,
        int expectedHistoryDelta,
        out string errorCode,
        out string detail)
    {
        int dayDelta = save.gameTime.ToAbsoluteDays() - preDay;
        if (dayDelta != 1)
            return InvariantError("DAY_DELTA_MISMATCH", "EXPECTED_1_GOT_" + dayDelta, out errorCode, out detail);

        int historyDelta = (save.emotionHistory?.Count ?? 0) - preHistory;
        if (historyDelta != expectedHistoryDelta)
            return InvariantError("HISTORY_DELTA_MISMATCH",
                "EXPECTED_" + expectedHistoryDelta + "_GOT_" + historyDelta, out errorCode, out detail);

        if ((save.worldEvents?.Count ?? 0) < preWorldEvents ||
            PermanentDamageCount(save) < prePermanentDamages ||
            PermanentTerrainCount(save) < prePermanentTerrain)
        {
            return InvariantError("NEGATIVE_APPEND_DELTA", "APPEND_ONLY_COUNT_DECREASED",
                out errorCode, out detail);
        }

        errorCode = null;
        detail = null;
        return true;
    }

    private static bool InvariantError(string code, string message, out string errorCode, out string detail)
    {
        errorCode = code;
        detail = message;
        return false;
    }

    private string NextCycleId()
    {
        _cycleSequence++;
        return "C-" + _cycleSequence.ToString("D4", CultureInfo.InvariantCulture);
    }

    private string CurrentCycleId() =>
        "C-" + _cycleSequence.ToString("D4", CultureInfo.InvariantCulture);

    private void Invalidate(string cycleId, string code, string detail)
    {
        _state = GameplayLabState.Invalid;
        Debug.LogError(BuildErrorLine("PROTOCOL_INVALID", cycleId, code, detail));
    }

    private void LogRejected(string phase, string code, string detail)
    {
        Debug.LogWarning(BuildErrorLine(phase, "NONE", code, detail));
    }

    private string BuildErrorLine(string phase, string cycleId, string code, string detail)
    {
        return $"{LOG_PREFIX} phase={phase} session={SessionId()} cycle={cycleId} " +
               $"utc={UtcNow()} labState={_state} code={code} detail={Sanitize(detail)}";
    }

    private void LogState(string phase, string cycleId, EmotionVector input, WorldManager world,
        int? dayDelta = null,
        int? historyDelta = null,
        int? worldEventDelta = null,
        int? chronicleDelta = null,
        int? permanentDamageDelta = null,
        int? permanentTerrainDelta = null)
    {
        WorldSaveData save = world.WorldSave;
        var environment = world.GetWorldState();
        EmotionVector eEnv = world.EmotionInertia.CurrentEEnv ?? EmotionVector.Neutral();
        LocationEntity lowland = FindLocation(save, "lowland");
        LocationEntity stone = FindLocation(save, "stone_area");
        AnimalEntity vole = FindAnimal(save, "vole");

        var line = new StringBuilder(768);
        line.Append(LOG_PREFIX);
        Append(line, "phase", phase);
        Append(line, "session", SessionId());
        Append(line, "cycle", cycleId);
        Append(line, "utc", UtcNow());
        Append(line, "gameDate", save.gameTime.ToKeyString());
        Append(line, "absDay", save.gameTime.ToAbsoluteDays().ToString(CultureInfo.InvariantCulture));
        Append(line, "labState", _state.ToString());
        AppendVector(line, "in", input);
        AppendVector(line, "env", eEnv);
        Append(line, "rain", F(environment.Rainfall));
        Append(line, "wind", F(environment.WindSpeed));
        Append(line, "fog", F(environment.FogDensity));
        Append(line, "stars", F(environment.StarVisibility));
        Append(line, "soil", F(environment.SoilMoisture));
        Append(line, "vegetation", F(environment.VegetationDensity));
        Append(line, "decay", F(environment.DecayLevel));
        Append(line, "creatures", F(environment.CreatureAbundance));
        Append(line, "drought", F(environment.DroughtDebt));
        Append(line, "lowlandWater", F(lowland?.waterLevel));
        Append(line, "lowlandSoil", F(lowland?.soilMoisture));
        Append(line, "stoneWater", F(stone?.waterLevel));
        Append(line, "stoneSoil", F(stone?.soilMoisture));
        Append(line, "voleLocation", Sanitize(vole?.location));
        Append(line, "voleBehavior", Sanitize(vole?.behavior?.drive));
        Append(line, "worldEvents", Count(save.worldEvents).ToString(CultureInfo.InvariantCulture));
        Append(line, "pendingChronicles", Count(save.pendingChronicles).ToString(CultureInfo.InvariantCulture));
        Append(line, "shownChronicles", Count(save.shownChronicles).ToString(CultureInfo.InvariantCulture));
        Append(line, "chronicleTotal", ChronicleCount(save).ToString(CultureInfo.InvariantCulture));
        Append(line, "emotionHistory", Count(save.emotionHistory).ToString(CultureInfo.InvariantCulture));
        Append(line, "permanentDamages", PermanentDamageCount(save).ToString(CultureInfo.InvariantCulture));
        Append(line, "permanentTerrainChanges", PermanentTerrainCount(save).ToString(CultureInfo.InvariantCulture));

        if (dayDelta.HasValue)
        {
            Append(line, "dayDelta", dayDelta.Value.ToString(CultureInfo.InvariantCulture));
            Append(line, "historyDelta", historyDelta.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
            Append(line, "worldEventDelta", worldEventDelta.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
            Append(line, "chronicleDelta", chronicleDelta.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
            Append(line, "permanentDamageDelta", permanentDamageDelta.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
            Append(line, "permanentTerrainDelta", permanentTerrainDelta.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
        }

        Debug.Log(line.ToString());
    }

    private static void AppendVector(StringBuilder line, string prefix, EmotionVector vector)
    {
        Append(line, prefix + "V", vector == null ? "NA" : F(vector.V));
        Append(line, prefix + "A", vector == null ? "NA" : F(vector.A));
        Append(line, prefix + "T", vector == null ? "NA" : F(vector.T));
        Append(line, prefix + "S", vector == null ? "NA" : F(vector.S));
        Append(line, prefix + "C", vector == null ? "NA" : F(vector.C));
    }

    private static void Append(StringBuilder line, string key, string value) =>
        line.Append(' ').Append(key).Append('=').Append(string.IsNullOrEmpty(value) ? "NA" : value);

    private static string F(float value) => value.ToString("F3", CultureInfo.InvariantCulture);

    private static string F(float? value) => value.HasValue ? F(value.Value) : "NA";

    private string SessionId() => string.IsNullOrEmpty(_sessionId) ? "UNINITIALIZED" : _sessionId;

    private static string UtcNow() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "NA";
        return value.Replace(' ', '_').Replace('\n', '_').Replace('\r', '_');
    }

    private static int ChronicleCount(WorldSaveData save) =>
        Count(save.pendingChronicles) + Count(save.shownChronicles);

    private static int PermanentDamageCount(WorldSaveData save)
    {
        int count = 0;
        if (save.plants == null) return count;
        foreach (PlantEntity plant in save.plants)
            count += plant?.permanentDamages?.Count ?? 0;
        return count;
    }

    private static int PermanentTerrainCount(WorldSaveData save)
    {
        int count = 0;
        if (save.locations == null) return count;
        foreach (LocationEntity location in save.locations)
            count += location?.permanentChanges?.Count ?? 0;
        return count;
    }

    private static int Count<T>(System.Collections.Generic.List<T> list) => list?.Count ?? 0;

    private static LocationEntity FindLocation(WorldSaveData save, string id)
    {
        if (save.locations == null) return null;
        foreach (LocationEntity location in save.locations)
            if (location != null && location.locationId == id) return location;
        return null;
    }

    private static AnimalEntity FindAnimal(WorldSaveData save, string id)
    {
        if (save.animals == null) return null;
        foreach (AnimalEntity animal in save.animals)
            if (animal != null && animal.speciesId == id) return animal;
        return null;
    }
}
}
