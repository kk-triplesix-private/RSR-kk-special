using ECommons.DalamudServices;
using Newtonsoft.Json.Linq;
using RotationSolver.IPC;

namespace RotationSolver.RotationPlanner;

/// <summary>
/// Represents a boss encounter from BossMod's registry
/// </summary>
internal class EncounterInfo
{
    public uint OID { get; set; }
    public string BossName { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string Category { get; set; } = "";
    public string Expansion { get; set; } = "";
    public string GroupType { get; set; } = "";
    public int SortOrder { get; set; }

    public string DisplayName => string.IsNullOrEmpty(GroupName) ? BossName : $"{GroupName} - {BossName}";
    public string ShortName => BossName;
}

/// <summary>
/// Fetches fight timeline data from BossMod via IPC and converts to RotationPlan format
/// </summary>
internal static class BossModTimelineProvider
{
    private static uint _cachedOID;
    private static List<PlanPhase>? _cachedPhases;
    private static List<TimelineMechanic>? _cachedMechanics;
    private static float _cachedTotalDuration;
    private static List<EncounterInfo>? _cachedEncounters;

    public static bool IsAvailable => BossModTimeline_IPCSubscriber.IsEnabled;

    public static bool IsActive
    {
        get
        {
            try
            {
                return BossModTimeline_IPCSubscriber.Timeline_IsActive?.Invoke() ?? false;
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Try to populate a plan with BossMod mechanic and phase data
    /// </summary>
    public static bool TryPopulate(RotationPlan plan)
    {
        if (!IsAvailable) return false;

        try
        {
            RefreshCache();
            if (_cachedPhases == null || _cachedMechanics == null)
                return false;

            plan.Phases = new List<PlanPhase>(_cachedPhases);
            plan.Mechanics = new List<TimelineMechanic>(_cachedMechanics);
            plan.TotalDuration = _cachedTotalDuration;
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"BossModTimelineProvider.TryPopulate failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get cached phases (returns empty if unavailable)
    /// </summary>
    public static List<PlanPhase> GetPhases()
    {
        RefreshCache();
        return _cachedPhases ?? [];
    }

    /// <summary>
    /// Get cached mechanics (returns empty if unavailable)
    /// </summary>
    public static List<TimelineMechanic> GetMechanics()
    {
        RefreshCache();
        return _cachedMechanics ?? [];
    }

    /// <summary>
    /// Get all available boss encounters from BossMod
    /// </summary>
    public static List<EncounterInfo> GetEncounters()
    {
        if (_cachedEncounters != null)
            return _cachedEncounters;

        if (!IsAvailable)
        {
            Svc.Log.Information("BossModTimelineProvider.GetEncounters: BossMod not available");
            return [];
        }

        try
        {
            var func = BossModTimeline_IPCSubscriber.Encounters_GetAll;
            if (func == null)
            {
                Svc.Log.Warning("BossModTimelineProvider.GetEncounters: Encounters.GetAll IPC not available - update BossModReborn");
                return [];
            }

            var json = func.Invoke();
            Svc.Log.Information($"BossModTimelineProvider.GetEncounters: got {json?.Length ?? 0} chars");
            if (string.IsNullOrEmpty(json))
                return [];

            var encounters = new List<EncounterInfo>();
            var arr = JArray.Parse(json);
            foreach (var item in arr)
            {
                encounters.Add(new EncounterInfo
                {
                    OID = item["OID"]?.Value<uint>() ?? 0,
                    BossName = item["BossName"]?.Value<string>() ?? "",
                    GroupName = item["GroupName"]?.Value<string>() ?? "",
                    Category = item["Category"]?.Value<string>() ?? "",
                    Expansion = item["Expansion"]?.Value<string>() ?? "",
                    GroupType = item["GroupType"]?.Value<string>() ?? "",
                    SortOrder = item["SortOrder"]?.Value<int>() ?? 0
                });
            }

            encounters.Sort((a, b) =>
            {
                int cmp = string.Compare(a.Category, b.Category, StringComparison.Ordinal);
                if (cmp != 0) return cmp;
                cmp = string.Compare(a.GroupName, b.GroupName, StringComparison.Ordinal);
                if (cmp != 0) return cmp;
                return a.SortOrder.CompareTo(b.SortOrder);
            });

            Svc.Log.Information($"BossModTimelineProvider.GetEncounters: loaded {encounters.Count} encounters");
            _cachedEncounters = encounters;
            return encounters;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"BossModTimelineProvider.GetEncounters failed: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Populate a plan with offline timeline data for a specific encounter OID
    /// </summary>
    public static bool TryPopulateForOID(RotationPlan plan, uint oid)
    {
        if (!IsAvailable) return false;

        try
        {
            var phasesJson = BossModTimeline_IPCSubscriber.Encounters_GetPhasesForOID?.Invoke(oid);
            var statesJson = BossModTimeline_IPCSubscriber.Encounters_GetStatesForOID?.Invoke(oid);
            var totalDuration = BossModTimeline_IPCSubscriber.Encounters_GetTotalDuration?.Invoke(oid) ?? 0;

            plan.Phases = ParsePhases(phasesJson);
            plan.Mechanics = ParseMechanics(statesJson);
            plan.TotalDuration = totalDuration > 0 ? totalDuration : 600f;
            plan.EncounterOID = oid;
            return plan.Phases.Count > 0 || plan.Mechanics.Count > 0;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"BossModTimelineProvider.TryPopulateForOID failed: {ex.Message}");
            return false;
        }
    }

    public static void InvalidateCache()
    {
        _cachedOID = 0;
        _cachedPhases = null;
        _cachedMechanics = null;
    }

    public static void InvalidateEncounterCache()
    {
        _cachedEncounters = null;
    }

    private static void RefreshCache()
    {
        if (!IsAvailable) return;

        try
        {
            // Get encounter info to check if cache is still valid
            var encounterJson = BossModTimeline_IPCSubscriber.Timeline_GetEncounter?.Invoke();
            if (string.IsNullOrEmpty(encounterJson))
            {
                InvalidateCache();
                return;
            }

            var encounter = JObject.Parse(encounterJson);
            var oid = encounter["OID"]?.Value<uint>() ?? 0;
            if (oid == _cachedOID && _cachedPhases != null)
                return; // Cache still valid

            _cachedOID = oid;
            _cachedTotalDuration = encounter["TotalDuration"]?.Value<float>() ?? 0;

            // Parse phases
            var phasesJson = BossModTimeline_IPCSubscriber.Timeline_GetPhases?.Invoke();
            _cachedPhases = ParsePhases(phasesJson);

            // Parse states into mechanics
            var statesJson = BossModTimeline_IPCSubscriber.Timeline_GetStates?.Invoke();
            _cachedMechanics = ParseMechanics(statesJson);
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"BossModTimelineProvider.RefreshCache failed: {ex.Message}");
            InvalidateCache();
        }
    }

    private static List<PlanPhase> ParsePhases(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];

        var phases = new List<PlanPhase>();
        var arr = JArray.Parse(json);
        foreach (var item in arr)
        {
            phases.Add(new PlanPhase
            {
                Name = item["Name"]?.Value<string>() ?? "",
                StartTime = item["StartTime"]?.Value<float>() ?? 0,
                Duration = item["Duration"]?.Value<float>() ?? 0
            });
        }
        return phases;
    }

    private static List<TimelineMechanic> ParseMechanics(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];

        var mechanics = new List<TimelineMechanic>();
        var arr = JArray.Parse(json);
        foreach (var item in arr)
        {
            var name = item["Name"]?.Value<string>() ?? "";
            if (string.IsNullOrEmpty(name)) continue;

            var time = item["Time"]?.Value<float>() ?? 0;
            var duration = item["Duration"]?.Value<float>() ?? 0;

            // Determine mechanic type from flags — prioritized
            MechanicType type;
            if (item["IsRaidwide"]?.Value<bool>() == true) type = MechanicType.Raidwide;
            else if (item["IsTankbuster"]?.Value<bool>() == true) type = MechanicType.Tankbuster;
            else if (item["IsKnockback"]?.Value<bool>() == true) type = MechanicType.Knockback;
            else if (item["IsDowntime"]?.Value<bool>() == true) type = MechanicType.Downtime;
            else if (item["IsPositioning"]?.Value<bool>() == true) type = MechanicType.Positioning;
            else if (item["IsVulnerable"]?.Value<bool>() == true) type = MechanicType.Vulnerable;
            else if (item["BossIsCasting"]?.Value<bool>() == true) type = MechanicType.BossCast;
            else type = MechanicType.Custom;

            mechanics.Add(new TimelineMechanic
            {
                CombatTime = time,
                Name = name,
                Type = type,
                Duration = duration
            });
        }
        return mechanics;
    }
}
