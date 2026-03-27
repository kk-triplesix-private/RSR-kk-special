using System.ComponentModel;
using System.Text.Json;
using ECommons.DalamudServices;
using RotationSolver.Basic.Configuration;
using RotationSolver.IPC;

namespace RotationSolver.ExtraRotations.Magical;

[Rotation("SMN Experimental", CombatType.PvE, GameVersion = "7.45")]
[SourceCode(Path = "main/ExtraRotations/Magical/SMN_Experimental.cs")]

public sealed class SMN_Experimental : SummonerRotation
{
    // ========================================================================
    // CONFIG OPTIONS
    // ========================================================================

    #region Config Options — Base (from SMN Dynamic)

    [RotationConfig(CombatType.PvE, Name = "Dynamic Egi Selection (avoid Ifrit casts while moving)")]
    public bool DynamicEgis { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Auto Addle on raidwide casts")]
    public bool AutoAddle { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Smart Radiant Aegis: Use on any incoming damage (raidwide/stack/AoE)")]
    public bool SmartAegis { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use GCDs to heal (ignored if healers are alive)")]
    public bool GCDHeal { get; set; } = false;

    [RotationConfig(CombatType.PvE, Name = "Use Crimson Cyclone at any range")]
    public bool AddCrimsonCyclone { get; set; } = true;

    [Range(1, 20, ConfigUnitType.Yalms)]
    [RotationConfig(CombatType.PvE, Name = "Max distance for Crimson Cyclone use")]
    public float CrimsonCycloneDistance { get; set; } = 3.0f;

    [RotationConfig(CombatType.PvE, Name = "Use Crimson Cyclone while moving")]
    public bool AddCrimsonCycloneMoving { get; set; } = false;

    [RotationConfig(CombatType.PvE, Name = "Use Swiftcast on Resurrection")]
    public bool AddSwiftcastOnRaise { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Raise while in Solar Bahamut")]
    public bool SBRaise { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use Swiftcast on Garuda (Slipstream)")]
    public bool AddSwiftcastOnGaruda { get; set; } = false;

    [RotationConfig(CombatType.PvE, Name = "Radiant Aegis on cooldown: Use charges freely outside of damage events")]
    public bool AegisOnCooldown { get; set; } = false;

    [RotationConfig(CombatType.PvE, Name = "Smart Ruin IV: Save for movement, prefer Ruin III when stationary")]
    public bool SmartRuinIV { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Movement Ruin IV: Use Ruin IV instantly when moving in primal phase")]
    public bool MovementRuinIV { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use BossMod IPC for raidwide/stack detection (requires BossModReborn)")]
    public bool UseBossModIPC { get; set; } = true;

    [Range(1, 10, ConfigUnitType.Seconds)]
    [RotationConfig(CombatType.PvE, Name = "BossMod lookahead (seconds) for raidwide/stack prediction")]
    public float BossModLookahead { get; set; } = 5f;

    [RotationConfig(CombatType.PvE, Name = "BossMod SpecialMode: Adapt rotation to Pyretic/NoMovement/Freezing mechanics")]
    public bool UseSpecialMode { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "M12S: Pause rotation during Directed Grotesquerie (prevent facing changes)")]
    public bool PauseOnDirectedGrotesquerie { get; set; } = true;

    [Range(0.5f, 5f, ConfigUnitType.Seconds)]
    [RotationConfig(CombatType.PvE, Name = "M12S: Seconds before mechanic resolves to pause rotation")]
    public float DirectionPauseLeadTime { get; set; } = 1.5f;

    [RotationConfig(CombatType.PvE, Name = "M11S: Always summon Ifrit last during Trophy Weapon phases")]
    public bool M11SIfritLast { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Opener Addle: Use Addle in opener")]
    public bool OpenerAddle { get; set; } = false;

    [Range(3, 15, ConfigUnitType.Seconds)]
    [RotationConfig(CombatType.PvE, Name = "Opener Addle: Use within first N seconds of combat")]
    public float OpenerAddleWindow { get; set; } = 10f;

    [Range(1f, 10f, ConfigUnitType.Seconds)]
    [RotationConfig(CombatType.PvE, Name = "Addle timing: seconds before cast ends to apply Addle")]
    public float AddleLeadTime { get; set; } = 3.5f;

    [RotationConfig(CombatType.PvE, Name = "Diagnostics: Show real-time decision state in Rotation Status panel")]
    public bool ShowDiagnostics { get; set; } = false;

    #endregion

    #region Config Options — Movement Prediction (NEW)

    [RotationConfig(CombatType.PvE, Name = "[NEW] Movement Prediction: Preemptively use instants when movement is predicted")]
    public bool UseMovementPrediction { get; set; } = true;

    [Range(1f, 8f, ConfigUnitType.Seconds)]
    [RotationConfig(CombatType.PvE, Name = "[NEW] Movement lookahead: How far ahead to predict movement (seconds)")]
    public float MovementLookahead { get; set; } = 3.5f;

    [RotationConfig(CombatType.PvE, Name = "[NEW] Preemptive Swiftcast: Use Swiftcast before predicted movement for Slipstream")]
    public bool PreemptiveSwiftcast { get; set; } = true;

    #endregion

    #region Config Options — Party Buff Tracking (NEW)

    [RotationConfig(CombatType.PvE, Name = "[NEW] Party Buff Tracking: Sync burst with party raid buffs")]
    public bool UsePartyBuffTracking { get; set; } = true;

    [Range(1, 5, ConfigUnitType.None)]
    [RotationConfig(CombatType.PvE, Name = "[NEW] Buff threshold: Min active party buffs to consider it a burst window")]
    public int BuffWindowThreshold { get; set; } = 2;

    [RotationConfig(CombatType.PvE, Name = "[NEW] Pool Fester/Necrotize: Hold charges outside buff windows for burst alignment")]
    public bool PoolFesterForBuffs { get; set; } = true;

    #endregion

    // ========================================================================
    // HELPER PROPERTIES & FIELDS
    // ========================================================================

    #region Helper Properties — Base

    private static bool HasFurtherRuin => StatusHelper.PlayerHasStatus(true, StatusID.FurtherRuin_2701);

    private static bool IsActionTargetBossDying(IBaseAction action)
    {
        var target = action.Target.Target;
        return (target.IsBossFromTTK() || target.IsBossFromIcon()) && target.IsDying();
    }

    private static class SpecialModes
    {
        public const string Normal = "Normal";
        public const string Pyretic = "Pyretic";
        public const string NoMovement = "NoMovement";
        public const string Freezing = "Freezing";
    }

    // Per-Frame Cache
    private bool _pauseForDirectionCached;
    private bool _pauseForDirectionValid;
    private string? _specialModeCache;
    private bool _specialModeCacheValid;
    private bool _m11sTrophyCached;
    private bool _m11sTrophyValid;

    // ForbiddenDirections JSON cache
    private (float Center, float HalfWidth)[]? _forbiddenArcsCached;
    private string? _forbiddenArcsJson;

    // Simulation State
    private static bool _simEnabled;
    private static bool _simRaidwideImminent;
    private static bool _simSharedImminent;
    private static bool _simTankbusterImminent;
    private static bool _simMagicalCast;
    private static bool _simMoving;
    private static int _simSpecialModeIndex;

    // Cast-ID Lookup
    private static int _lookupCastId;
    private static string? _lookupResult;

    // Decision Log
    private static readonly string[] _decisionLog = new string[40];
    private static int _decisionLogIndex;
    private static int _decisionLogCount;

    // IPC error throttle
    private DateTime _lastIpcErrorLog = DateTime.MinValue;

    private static readonly string[] SpecialModeNames = [SpecialModes.Normal, SpecialModes.Pyretic, SpecialModes.NoMovement, SpecialModes.Freezing];

    // Lumina caches
    private static readonly Dictionary<uint, string> _actionNameCache = new();
    private static readonly Dictionary<uint, string> _attackTypeCache = new();

    private static void LogDecision(string message)
    {
        _decisionLog[_decisionLogIndex] = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _decisionLogIndex = (_decisionLogIndex + 1) % _decisionLog.Length;
        if (_decisionLogCount < _decisionLog.Length) _decisionLogCount++;
    }

    #endregion

    // ========================================================================
    // PARTY BUFF TRACKER (NEW)
    // ========================================================================

    #region Party Buff Tracker

    /// <summary>
    /// Known raid buff StatusIDs that indicate a burst window.
    /// These are party-wide damage buffs on a ~2-minute cycle.
    /// </summary>
    private static readonly uint[] RaidBuffStatusIds =
    [
        (uint)StatusID.BattleLitany,    // DRG - Crit rate up
        (uint)StatusID.Brotherhood,     // MNK - Damage up
        (uint)StatusID.TechnicalFinish, // DNC - Damage up
        (uint)StatusID.ArcaneCircle,    // RPR - Damage up
        (uint)StatusID.Embolden,        // RDM - Magic damage up (party)
        (uint)StatusID.SearingLight,    // SMN - Damage up (our own)
        (uint)StatusID.BattleVoice,     // BRD - Direct hit up
        (uint)StatusID.RadiantFinale,   // BRD - Damage up
        (uint)StatusID.Divination,      // AST - Damage up
        (uint)StatusID.StarryMuse,      // PCT - Damage up
    ];

    /// <summary>
    /// Enemy debuffs that increase damage taken (e.g., Dokumori, Chain Stratagem).
    /// </summary>
    private static readonly uint[] RaidDebuffStatusIds =
    [
        (uint)StatusID.Dokumori,        // NIN - Damage taken up
        (uint)StatusID.ChainStratagem,  // SCH - Crit rate up on target
    ];

    // Per-frame buff tracking cache
    private int _activeBuffCount;
    private float _shortestBuffRemaining;
    private bool _buffTrackingValid;
    private bool _targetHasDebuff;

    // Historical buff window tracking (for prediction)
    private DateTime _lastBuffWindowStart = DateTime.MinValue;
    private DateTime _lastBuffWindowEnd = DateTime.MinValue;
    private float _avgBuffWindowInterval = 120f; // default 2 min
    private int _buffWindowsSeen;

    /// <summary>
    /// Scans party members for active raid buffs. Cached per frame.
    /// Returns the number of distinct active raid buffs.
    /// </summary>
    private int GetActivePartyBuffCount()
    {
        if (_buffTrackingValid) return _activeBuffCount;
        _buffTrackingValid = true;
        _activeBuffCount = 0;
        _shortestBuffRemaining = float.MaxValue;
        _targetHasDebuff = false;

        if (!UsePartyBuffTracking) return 0;

        // Scan party members for raid buffs
        var party = PartyMembers;
        if (party != null)
        {
            // Use a bitfield to count DISTINCT buffs (don't double-count same buff from multiple sources)
            uint seenBuffMask = 0;

            foreach (var member in party)
            {
                if (member == null || member.IsDead) continue;
                var statusList = member.StatusList;
                if (statusList == null) continue;

                foreach (var status in statusList)
                {
                    for (int i = 0; i < RaidBuffStatusIds.Length; i++)
                    {
                        if (status.StatusId == RaidBuffStatusIds[i] && (seenBuffMask & (1u << i)) == 0)
                        {
                            seenBuffMask |= (1u << i);
                            _activeBuffCount++;
                            if (status.RemainingTime < _shortestBuffRemaining)
                                _shortestBuffRemaining = status.RemainingTime;
                            break;
                        }
                    }
                }
            }
        }

        // Check target for raid debuffs (Dokumori, Chain)
        if (HostileTarget != null)
        {
            var targetStatus = HostileTarget.StatusList;
            if (targetStatus != null)
            {
                foreach (var status in targetStatus)
                {
                    for (int i = 0; i < RaidDebuffStatusIds.Length; i++)
                    {
                        if (status.StatusId == RaidDebuffStatusIds[i])
                        {
                            _targetHasDebuff = true;
                            _activeBuffCount++; // debuffs count toward burst threshold
                            if (status.RemainingTime < _shortestBuffRemaining)
                                _shortestBuffRemaining = status.RemainingTime;
                            break;
                        }
                    }
                }
            }
        }

        // Track buff window history for prediction
        if (_activeBuffCount >= BuffWindowThreshold)
        {
            if ((DateTime.Now - _lastBuffWindowStart).TotalSeconds > 30)
            {
                // New buff window started
                if (_lastBuffWindowStart != DateTime.MinValue)
                {
                    float interval = (float)(DateTime.Now - _lastBuffWindowStart).TotalSeconds;
                    if (interval is > 60 and < 180) // sanity check
                    {
                        _avgBuffWindowInterval = _buffWindowsSeen > 0
                            ? (_avgBuffWindowInterval * _buffWindowsSeen + interval) / (_buffWindowsSeen + 1)
                            : interval;
                        _buffWindowsSeen++;
                    }
                }
                _lastBuffWindowStart = DateTime.Now;
                LogDecision($"BuffTracker: window started ({_activeBuffCount} buffs)");
            }
        }
        else if (_activeBuffCount == 0 && _lastBuffWindowStart != DateTime.MinValue
                 && (DateTime.Now - _lastBuffWindowStart).TotalSeconds < 30)
        {
            _lastBuffWindowEnd = DateTime.Now;
        }

        return _activeBuffCount;
    }

    /// <summary>Whether the party is currently in a burst window (enough raid buffs active).</summary>
    private bool IsInPartyBurstWindow => GetActivePartyBuffCount() >= BuffWindowThreshold;

    /// <summary>Estimated seconds until next burst window (based on observed 2-min intervals).</summary>
    private float EstimatedSecondsUntilNextBuffWindow
    {
        get
        {
            if (_lastBuffWindowStart == DateTime.MinValue) return 120f; // no data yet
            float elapsed = (float)(DateTime.Now - _lastBuffWindowStart).TotalSeconds;
            float remaining = _avgBuffWindowInterval - elapsed;
            return remaining > 0 ? remaining : 0;
        }
    }

    #endregion

    // ========================================================================
    // MOVEMENT PREDICTION (NEW)
    // ========================================================================

    #region Movement Prediction

    // Per-frame movement prediction cache
    private float _movementUrgency;
    private bool _movementPredictionValid;
    private string? _movementReason;

    /// <summary>
    /// Calculates a movement urgency score (0.0 = safe to hardcast, 1.0 = must move NOW).
    /// Uses multiple data sources: current movement, BossMod mechanics, forbidden zones.
    /// Cached per frame.
    /// </summary>
    private float GetMovementUrgency()
    {
        if (_movementPredictionValid) return _movementUrgency;
        _movementPredictionValid = true;
        _movementUrgency = 0f;
        _movementReason = null;

        // Base: current movement state
        bool currentlyMoving = _simEnabled ? _simMoving : IsMoving;
        if (currentlyMoving)
        {
            _movementUrgency = 1.0f;
            _movementReason = "currently moving";
            return _movementUrgency;
        }

        if (!UseMovementPrediction) return 0f;

        // BossMod SpecialMode: some modes require or forbid movement
        var mode = GetCachedSpecialMode();
        if (mode != null)
        {
            switch (mode)
            {
                case SpecialModes.Pyretic:
                    // Must NOT move - urgency is 0 (prefer casts)
                    _movementUrgency = 0f;
                    _movementReason = "Pyretic: no movement";
                    return 0f;
                case SpecialModes.Freezing:
                    // Must NOT act - but if we do, instants are safer
                    _movementUrgency = 0.8f;
                    _movementReason = "Freezing: prefer instants";
                    return _movementUrgency;
                case SpecialModes.NoMovement:
                    _movementUrgency = 0f;
                    _movementReason = "NoMovement: casts OK";
                    return 0f;
            }
        }

        // BossMod ForbiddenDirections: check if our current position is about to become unsafe
        if (UseBossModIPC && BossModHints_IPCSubscriber.IsEnabled)
        {
            try
            {
                // Check for imminent raidwide/shared (often require repositioning)
                if (BossModHints_IPCSubscriber.Hints_IsRaidwideImminent?.Invoke(MovementLookahead) == true
                    || BossModHints_IPCSubscriber.Hints_IsSharedImminent?.Invoke(MovementLookahead) == true)
                {
                    // Raidwides/stacks often require pre-positioning — mild urgency
                    _movementUrgency = MathF.Max(_movementUrgency, 0.3f);
                    _movementReason = "raidwide/stack: possible repositioning";
                }

                // Check forbidden directions - if any exist, movement may be needed
                var json = BossModHints_IPCSubscriber.Hints_ForbiddenDirections?.Invoke();
                if (!string.IsNullOrEmpty(json) && json != "[]")
                {
                    var arcs = ParseForbiddenDirections(json);
                    if (arcs.Length > 0)
                    {
                        // Movement likely needed to reposition
                        // Scale urgency by how much of the circle is forbidden
                        float totalForbidden = 0f;
                        for (int i = 0; i < arcs.Length; i++)
                            totalForbidden += arcs[i].HalfWidth * 2f;
                        float forbiddenFraction = MathF.Min(totalForbidden / (2f * MathF.PI), 1f);

                        float dirUrgency = 0.3f + forbiddenFraction * 0.5f;
                        if (dirUrgency > _movementUrgency)
                        {
                            _movementUrgency = dirUrgency;
                            _movementReason = $"forbidden dirs ({forbiddenFraction:P0} blocked)";
                        }
                    }
                }

                // Check BossMod timeline for upcoming positioning mechanics
                if (BossModTimeline_IPCSubscriber.IsEnabled)
                {
                    try
                    {
                        var statesJson = BossModTimeline_IPCSubscriber.Timeline_GetStates?.Invoke();
                        if (!string.IsNullOrEmpty(statesJson) && statesJson != "[]")
                        {
                            float timelineUrgency = CheckTimelineForMovement(statesJson);
                            if (timelineUrgency > _movementUrgency)
                            {
                                _movementUrgency = timelineUrgency;
                                _movementReason = "timeline: positioning mechanic";
                            }
                        }
                    }
                    catch { /* timeline IPC optional */ }
                }
            }
            catch (Exception ex)
            {
                if ((DateTime.Now - _lastIpcErrorLog).TotalSeconds > 10)
                {
                    _lastIpcErrorLog = DateTime.Now;
                    LogDecision($"IPC error in MovementPrediction: {ex.Message}");
                }
            }
        }

        return _movementUrgency;
    }

    /// <summary>
    /// Quick check: should we prefer instant-cast GCDs right now?
    /// Returns true when movement urgency is above a useful threshold.
    /// </summary>
    private bool ShouldPreferInstants()
    {
        if (!UseMovementPrediction) return _simEnabled ? _simMoving : IsMoving;
        return GetMovementUrgency() >= 0.5f;
    }

    /// <summary>
    /// Check BossMod timeline states for upcoming positioning mechanics.
    /// Returns urgency 0.0-0.8 based on how soon a positioning mechanic occurs.
    /// </summary>
    private float CheckTimelineForMovement(string statesJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(statesJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return 0f;

            foreach (var state in root.EnumerateArray())
            {
                bool isPositioning = false;
                bool isKnockback = false;
                float time = 0f;

                if (state.TryGetProperty("IsPositioning", out var posProp))
                    isPositioning = posProp.GetBoolean();
                if (state.TryGetProperty("IsKnockback", out var kbProp))
                    isKnockback = kbProp.GetBoolean();
                if (state.TryGetProperty("Time", out var timeProp))
                    time = timeProp.GetSingle();

                if ((isPositioning || isKnockback) && time > 0 && time <= MovementLookahead)
                {
                    // Closer in time = higher urgency
                    float timeRatio = 1f - (time / MovementLookahead);
                    return 0.3f + timeRatio * 0.5f; // 0.3 to 0.8
                }
            }
        }
        catch { /* parse error - ignore */ }

        return 0f;
    }

    #endregion

    // ========================================================================
    // CACHED HELPERS (from SMN Dynamic)
    // ========================================================================

    #region Cached Helpers

    private string? GetCachedSpecialMode()
    {
        if (_specialModeCacheValid) return _specialModeCache;
        _specialModeCacheValid = true;
        _specialModeCache = null;

        if (!UseSpecialMode || !UseBossModIPC || !BossModHints_IPCSubscriber.IsEnabled)
            return null;

        try
        {
            _specialModeCache = BossModHints_IPCSubscriber.Hints_SpecialMode?.Invoke();
        }
        catch (Exception ex)
        {
            if ((DateTime.Now - _lastIpcErrorLog).TotalSeconds > 10)
            {
                _lastIpcErrorLog = DateTime.Now;
                LogDecision($"IPC error in GetCachedSpecialMode: {ex.Message}");
            }
        }
        return _specialModeCache;
    }

    private static float DirectedGrotesquerieRemaining
    {
        get
        {
            var statusList = Player?.StatusList;
            if (statusList == null) return 0f;
            foreach (var status in statusList)
            {
                if (status.StatusId == 3558) return status.RemainingTime;
            }
            return 0f;
        }
    }

    private bool ShouldPauseForDirection()
    {
        if (_pauseForDirectionValid) return _pauseForDirectionCached;
        _pauseForDirectionValid = true;
        _pauseForDirectionCached = ComputeShouldPauseForDirection();
        return _pauseForDirectionCached;
    }

    private bool ComputeShouldPauseForDirection()
    {
        if (!PauseOnDirectedGrotesquerie) return false;

        var remaining = DirectedGrotesquerieRemaining;
        if (remaining <= 0f || remaining > DirectionPauseLeadTime) return false;

        if (UseBossModIPC && BossModHints_IPCSubscriber.IsEnabled)
        {
            try
            {
                var json = BossModHints_IPCSubscriber.Hints_ForbiddenDirections?.Invoke();
                if (!string.IsNullOrEmpty(json) && json != "[]" && HostileTarget != null && Player != null)
                {
                    var dx = HostileTarget.Position.X - Player.Position.X;
                    var dz = HostileTarget.Position.Z - Player.Position.Z;
                    var dirToTarget = MathF.Atan2(dx, dz);
                    var arcs = ParseForbiddenDirections(json);
                    var forbidden = IsDirectionForbidden(dirToTarget, arcs);
                    if (forbidden) LogDecision($"Direction forbidden, remaining={remaining:F1}s");
                    return forbidden;
                }
            }
            catch (Exception ex)
            {
                if ((DateTime.Now - _lastIpcErrorLog).TotalSeconds > 10)
                {
                    _lastIpcErrorLog = DateTime.Now;
                    LogDecision($"IPC error in ForbiddenDirections: {ex.Message}");
                }
            }
        }

        return true;
    }

    private (float Center, float HalfWidth)[] ParseForbiddenDirections(string json)
    {
        if (json == _forbiddenArcsJson && _forbiddenArcsCached != null)
            return _forbiddenArcsCached;

        _forbiddenArcsJson = json;
        using var doc = JsonDocument.Parse(json);
        var list = new List<(float, float)>();
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            list.Add((entry.GetProperty("Center").GetSingle(), entry.GetProperty("HalfWidth").GetSingle()));
        }
        _forbiddenArcsCached = list.ToArray();
        return _forbiddenArcsCached;
    }

    private static bool IsDirectionForbidden(float directionRad, (float Center, float HalfWidth)[] arcs)
    {
        for (int i = 0; i < arcs.Length; i++)
        {
            var diff = MathF.IEEERemainder(directionRad - arcs[i].Center, 2f * MathF.PI);
            if (MathF.Abs(diff) < arcs[i].HalfWidth) return true;
        }
        return false;
    }

    private bool IsInM11STrophyPhaseCached()
    {
        if (_m11sTrophyValid) return _m11sTrophyCached;
        _m11sTrophyValid = true;
        _m11sTrophyCached = IsInM11STrophyPhase();
        return _m11sTrophyCached;
    }

    private enum TrophyWeaponType : byte { None, Axe, Scythe, Sword }
    private TrophyWeaponType _detectedTrophyWeapon;
    private float _m11sTrophyCastTime = -1f;
    private const float TrophyCastBridgeWindow = 8f;

    private bool IsInM11STrophyPhase()
    {
        if (!DataCenter.IsInM11S)
        {
            _m11sTrophyCastTime = -1f;
            _detectedTrophyWeapon = TrophyWeaponType.None;
            return false;
        }

        float combatTime = DataCenter.CombatTimeRaw;
        if (combatTime == 0)
        {
            _m11sTrophyCastTime = -1f;
            _detectedTrophyWeapon = TrophyWeaponType.None;
            return false;
        }

        try
        {
            // Schritt 1: Boss-Cast Erkennung (frühester Trigger)
            var hostiles = DataCenter.AllHostileTargets;
            if (hostiles != null)
            {
                for (int i = 0, n = hostiles.Count; i < n; i++)
                {
                    var h = hostiles[i];
                    if (h == null || !h.IsCasting) continue;
                    switch (h.CastActionId)
                    {
                        case 46028: // Trophy Weapons (erste Phase)
                        case 46102: // Trophy Weapons (Ultimate)
                            _m11sTrophyCastTime = combatTime;
                            return true;
                        case 46037: // Raw Steel Trophy
                        case 46038:
                        case 46114:
                        case 46115:
                            return true;
                    }
                }
            }

            // Schritt 2: Trophy Weapon Adds
            var objects = Svc.Objects;
            if (objects != null)
            {
                _detectedTrophyWeapon = TrophyWeaponType.None;
                int count = objects.Length;
                for (int i = 0; i < count; i++)
                {
                    var obj = objects[i];
                    if (obj == null) continue;
                    uint id = obj.BaseId;
                    if (id == 0) id = obj.DataId;
                    switch (id)
                    {
                        case 0x4AF0: _detectedTrophyWeapon = TrophyWeaponType.Axe; _m11sTrophyCastTime = -1f; return true;
                        case 0x4AF1: _detectedTrophyWeapon = TrophyWeaponType.Scythe; _m11sTrophyCastTime = -1f; return true;
                        case 0x4AF2: _detectedTrophyWeapon = TrophyWeaponType.Sword; _m11sTrophyCastTime = -1f; return true;
                    }
                }
            }
        }
        catch (AccessViolationException) { }

        // Schritt 3: Lücke überbrücken, verfällt nach TrophyCastBridgeWindow Sekunden
        if (_m11sTrophyCastTime >= 0f && (combatTime - _m11sTrophyCastTime) < TrophyCastBridgeWindow)
            return true;

        _m11sTrophyCastTime = -1f;
        return false;
    }

    /// <summary>
    /// ENHANCED: Checks whether Necrotize or Fester should be spent.
    /// Now considers party buff windows for optimal burst alignment.
    /// </summary>
    private bool ShouldSpendFesterStack(IBaseAction action, bool inBigInvocation, bool inSolarUnique)
    {
        if (IsActionTargetBossDying(action)) return true;
        if (EnergyDrainPvE.Cooldown.WillHaveOneChargeGCD(2)) return true;

        // During Phoenix phase: HOLD charges for next Solar Bahamut burst
        if (InPhoenix && !HasSearingLight)
        {
            LogDecision("Necrotize: holding for burst (Phoenix phase)");
            return false;
        }

        // NEW: Pool charges for upcoming party buff window
        if (PoolFesterForBuffs && UsePartyBuffTracking && !inBigInvocation)
        {
            bool inBuffWindow = IsInPartyBurstWindow;
            float timeToNextWindow = EstimatedSecondsUntilNextBuffWindow;

            // If NOT in buff window and next window is close (< 15s), hold charges
            if (!inBuffWindow && timeToNextWindow is > 0 and < 15f)
            {
                LogDecision($"Necrotize: pooling for buff window in {timeToNextWindow:F0}s");
                return false;
            }

            // If IN buff window, spend aggressively
            if (inBuffWindow)
            {
                LogDecision($"Necrotize: spending in buff window ({_activeBuffCount} buffs)");
                return true;
            }
        }

        if (inBigInvocation) return true;
        if ((inSolarUnique && HasSearingLight) || !SearingLightPvE.EnoughLevel) return true;
        return false;
    }

    #endregion

    // ========================================================================
    // COUNTDOWN LOGIC
    // ========================================================================

    #region Countdown Logic

    protected override IAction? CountDownAction(float remainTime)
    {
        if (SummonCarbunclePvE.CanUse(out IAction? act)) return act;

        if (HasSummon)
        {
            float castTime = RuinIiiPvE.EnoughLevel ? RuinIiiPvE.Info.CastTime : RuinPvE.Info.CastTime;
            if (remainTime <= castTime + CountDownAhead)
            {
                if (RuinIiiPvE.EnoughLevel && RuinIiiPvE.CanUse(out act)) return act;
                if (RuinPvE.CanUse(out act)) return act;
            }
        }

        return base.CountDownAction(remainTime);
    }

    #endregion

    // ========================================================================
    // HEAL & DEFENSE ABILITIES
    // ========================================================================

    #region Heal & Defense Abilities

    [RotationDesc(ActionID.LuxSolarisPvE)]
    protected override bool HealAreaAbility(IAction nextGCD, out IAction? act)
    {
        if (LuxSolarisPvE.CanUse(out act)) return true;
        return base.HealAreaAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.RekindlePvE)]
    protected override bool HealSingleAbility(IAction nextGCD, out IAction? act)
    {
        if (RekindlePvE.CanUse(out act, targetOverride: TargetType.LowHP)) return true;
        return base.HealSingleAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.AddlePvE, ActionID.RadiantAegisPvE)]
    protected override bool DefenseAreaAbility(IAction nextGCD, out IAction? act)
    {
        if (ShouldPauseForDirection()) return base.DefenseAreaAbility(nextGCD, out act);

        if (AutoAddle && ShouldUseAddle())
        {
            if (AddlePvE.CanUse(out act)) return true;
        }

        if (SmartAegis && !IsLastAction(false, RadiantAegisPvE) && RadiantAegisPvE.CanUse(out act, usedUp: true))
            return true;

        return base.DefenseAreaAbility(nextGCD, out act);
    }

    #endregion

    // ========================================================================
    // oGCD LOGIC
    // ========================================================================

    #region oGCD Logic

    [RotationDesc(ActionID.LuxSolarisPvE)]
    protected override bool GeneralAbility(IAction nextGCD, out IAction? act)
    {
        if (ShouldPauseForDirection()) return base.GeneralAbility(nextGCD, out act);

        // Lux Solaris timing
        if (LuxSolarisPvE.CanUse(out act))
        {
            bool bigSummonEnding = (InBahamut || InPhoenix || InSolarBahamut) && SummonTime <= GCDTime(1) + 0.5f;
            bool statusExpiring = StatusHelper.PlayerWillStatusEndGCD(2, 0, true, StatusID.RefulgentLux);
            bool notInBigSummon = !InBahamut && !InPhoenix && !InSolarBahamut;
            bool safeToUseLux = notInBigSummon && (!HasAetherflowStacks || !EnergyDrainPvE.Cooldown.WillHaveOneChargeGCD(1));

            if (bigSummonEnding || statusExpiring || safeToUseLux)
            {
                LogDecision($"LuxSolaris: {(bigSummonEnding ? "endingSummon" : statusExpiring ? "statusExpiring" : "filler")}");
                return true;
            }
        }

        if (StatusHelper.PlayerWillStatusEndGCD(2, 0, true, StatusID.FirebirdTrance))
        {
            if (RekindlePvE.CanUse(out act)) return true;
        }
        if (StatusHelper.PlayerWillStatusEndGCD(3, 0, true, StatusID.FirebirdTrance))
        {
            if (RekindlePvE.CanUse(out act, targetOverride: TargetType.LowHP)) return true;
        }

        if (AegisOnCooldown && InCombat && !IsLastAction(false, RadiantAegisPvE)
            && RadiantAegisPvE.CanUse(out act, usedUp: true))
            return true;

        return base.GeneralAbility(nextGCD, out act);
    }

    protected override bool AttackAbility(IAction nextGCD, out IAction? act)
    {
        if (ShouldPauseForDirection()) return base.AttackAbility(nextGCD, out act);

        bool inBigInvocation = !SummonBahamutPvE.EnoughLevel || InBahamut || InPhoenix || InSolarBahamut;
        bool inSolarUnique = SummonSolarBahamutPvE.EnoughLevel ? !InBahamut && !InPhoenix && InSolarBahamut : InBahamut && !InPhoenix;
        bool burstInSolar = (SummonSolarBahamutPvE.EnoughLevel && InSolarBahamut) || (!SummonSolarBahamutPvE.EnoughLevel && InBahamut) || !SummonBahamutPvE.EnoughLevel;

        // Pot VOR Solar Bahamut: wenn Solar Bahamut der nächste GCD ist
        if (nextGCD.IsTheSameTo(true, SummonSolarBahamutPvE) && InCombat && UseBurstMedicine(out act))
            return true;

        // ENHANCED: Searing Light timing - prefer aligning with party buffs
        if (burstInSolar)
        {
            if (UsePartyBuffTracking && SearingLightPvE.CanUse(out act))
            {
                // If party buffs are already active or about to be active, fire immediately
                int partyBuffs = GetActivePartyBuffCount();
                if (partyBuffs >= 1 || EstimatedSecondsUntilNextBuffWindow < 3f)
                {
                    LogDecision($"SearingLight: aligned with {partyBuffs} party buffs");
                    return true;
                }
                // If no party buffs yet but we're in Solar, still fire (don't waste Solar)
                LogDecision($"SearingLight: burst in {(InSolarBahamut ? "Solar" : InBahamut ? "Bahamut" : "preBahamut")} (no party buffs yet)");
                return true;
            }
            else if (SearingLightPvE.CanUse(out act))
            {
                LogDecision($"SearingLight: burst in {(InSolarBahamut ? "Solar" : InBahamut ? "Bahamut" : "preBahamut")}");
                return true;
            }
        }

        if (MountainBusterPvE.CanUse(out act)) return true;

        if (OpenerAddle && inBigInvocation && CombatElapsedLess(OpenerAddleWindow)
            && HostileTarget != null && !HostileTarget.HasStatus(false, StatusID.Addle))
        {
            if (AddlePvE.CanUse(out act)) return true;
        }

        if (inBigInvocation)
        {
            bool summonActiveOrLowLevel = SummonTime > 0f || !SummonBahamutPvE.EnoughLevel;

            if (EnergySiphonPvE.CanUse(out act) && (IsActionTargetBossDying(EnergySiphonPvE) || summonActiveOrLowLevel)) return true;
            if (EnergyDrainPvE.CanUse(out act) && (IsActionTargetBossDying(EnergyDrainPvE) || summonActiveOrLowLevel)) return true;
            if (EnkindleBahamutPvE.CanUse(out act) && (IsActionTargetBossDying(EnkindleBahamutPvE) || summonActiveOrLowLevel)) return true;
            if (EnkindleSolarBahamutPvE.CanUse(out act) && (IsActionTargetBossDying(EnkindleSolarBahamutPvE) || summonActiveOrLowLevel)) return true;
            if (EnkindlePhoenixPvE.CanUse(out act) && (IsActionTargetBossDying(EnkindlePhoenixPvE) || summonActiveOrLowLevel)) return true;
            if (DeathflarePvE.CanUse(out act) && (IsActionTargetBossDying(DeathflarePvE) || summonActiveOrLowLevel)) return true;
            if (SunflarePvE.CanUse(out act) && (IsActionTargetBossDying(SunflarePvE) || summonActiveOrLowLevel)) return true;
            if (SearingFlashPvE.CanUse(out act) && (IsActionTargetBossDying(SearingFlashPvE) || summonActiveOrLowLevel)) return true;
        }

        if (PainflarePvE.CanUse(out act))
        {
            if ((inSolarUnique && HasSearingLight) || !SearingLightPvE.EnoughLevel) return true;
            if (IsActionTargetBossDying(PainflarePvE)) return true;
        }

        if (NecrotizePvE.CanUse(out act) && ShouldSpendFesterStack(NecrotizePvE, inBigInvocation, inSolarUnique))
            return true;
        if (FesterPvE.CanUse(out act) && ShouldSpendFesterStack(FesterPvE, inBigInvocation, inSolarUnique))
            return true;

        if (SearingFlashPvE.CanUse(out act) && IsActionTargetBossDying(SearingFlashPvE)) return true;
        return base.AttackAbility(nextGCD, out act);
    }

    protected override bool EmergencyAbility(IAction nextGCD, out IAction? act)
    {
        if (ShouldPauseForDirection()) return base.EmergencyAbility(nextGCD, out act);

        if (AutoAddle && ShouldUseAddle())
        {
            if (AddlePvE.CanUse(out act)) return true;
        }

        if (SmartAegis && IsDamageImminent()
            && !IsLastAction(false, RadiantAegisPvE) && RadiantAegisPvE.CanUse(out act, usedUp: true))
            return true;

        if (SwiftcastPvE.CanUse(out act))
        {
            if (AddSwiftcastOnRaise && nextGCD.IsTheSameTo(false, ResurrectionPvE)) return true;
            if (AddSwiftcastOnGaruda && nextGCD.IsTheSameTo(false, SlipstreamPvE)
                && ElementalMasteryTrait.EnoughLevel && !InBahamut && !InPhoenix && !InSolarBahamut) return true;

            // NEW: Preemptive Swiftcast when movement is predicted and we're about to hardcast
            if (PreemptiveSwiftcast && UseMovementPrediction && !InBahamut && !InPhoenix && !InSolarBahamut)
            {
                float urgency = GetMovementUrgency();
                bool nextIsHardCast = nextGCD.IsTheSameTo(false, SlipstreamPvE)
                    || nextGCD.IsTheSameTo(false, RuinIiiPvE);

                if (urgency >= 0.6f && nextIsHardCast)
                {
                    LogDecision($"PreemptiveSwiftcast: urgency={urgency:F1} ({_movementReason})");
                    return true;
                }
            }
        }

        return base.EmergencyAbility(nextGCD, out act);
    }

    #endregion

    // ========================================================================
    // GCD LOGIC
    // ========================================================================

    #region GCD Logic

    [RotationDesc(ActionID.CrimsonCyclonePvE)]
    protected override bool MoveForwardGCD(out IAction? act)
    {
        if (CrimsonCyclonePvE.CanUse(out act)) return true;
        return base.MoveForwardGCD(out act);
    }

    [RotationDesc(ActionID.PhysickPvE)]
    protected override bool HealSingleGCD(out IAction? act)
    {
        if (GCDHeal && PhysickPvE.CanUse(out act)) return true;
        return base.HealSingleGCD(out act);
    }

    [RotationDesc(ActionID.ResurrectionPvE)]
    protected override bool RaiseGCD(out IAction? act)
    {
        if (!InSolarBahamut || SBRaise)
        {
            if (ResurrectionPvE.CanUse(out act)) return true;
        }
        return base.RaiseGCD(out act);
    }

    protected override bool GeneralGCD(out IAction? act)
    {
        // Per-Frame Caches invalidieren
        _pauseForDirectionValid = false;
        _specialModeCacheValid = false;
        _m11sTrophyValid = false;
        _buffTrackingValid = false;       // NEW: reset buff tracking
        _movementPredictionValid = false;  // NEW: reset movement prediction

        // M12S: Rotation pausieren wenn Directed Grotesquerie aktiv
        if (ShouldPauseForDirection())
        {
            LogDecision("Paused: Directed Grotesquerie");
            act = null;
            return false;
        }

        if (SummonCarbunclePvE.CanUse(out act)) return true;

        // Big summon phase
        if (SummonBahamutPvE.CanUse(out act)) return true;
        if (!SummonBahamutPvE.Info.EnoughLevelAndQuest() && DreadwyrmTrancePvE.CanUse(out act)) return true;
        if (IsBurst && !SearingLightPvE.Cooldown.IsCoolingDown && SummonSolarBahamutPvE.CanUse(out act)) return true;

        // Garuda: Slipstream
        if (SlipstreamPvE.CanUse(out act, skipCastingCheck: AddSwiftcastOnGaruda && ((!SwiftcastPvE.Cooldown.IsCoolingDown && IsMoving) || HasSwift)))
            return true;

        // Ifrit: Crimson Cyclone + Strike
        if ((!IsMoving || AddCrimsonCycloneMoving) && CrimsonCyclonePvE.CanUse(out act)
            && (AddCrimsonCyclone || CrimsonCyclonePvE.Target.Target.DistanceToPlayer() <= CrimsonCycloneDistance))
            return true;
        if (CrimsonStrikePvE.CanUse(out act)) return true;

        // Gemshine
        if (PreciousBrillianceTime(out act)) return true;
        if (GemshineTime(out act)) return true;

        // Pre-Bahamut Aethercharge
        if (!DreadwyrmTrancePvE.Info.EnoughLevelAndQuest() && HasHostilesInRange && AetherchargePvE.CanUse(out act))
            return true;

        // Primal summon phase - ENHANCED WITH MOVEMENT PREDICTION + BUFF TRACKING
        if (!InBahamut && !InPhoenix && !InSolarBahamut)
        {
            // ENHANCED: Use movement prediction instead of just IsMoving
            bool needInstant = ShouldPreferInstants();

            if (MovementRuinIV && needInstant && HasFurtherRuin && SummonTimeEndAfterGCD() && AttunmentTimeEndAfterGCD()
                && RuinIvPvE.CanUse(out act, skipAoeCheck: true))
                return true;

            if (DynamicEgis)
            {
                if (DynamicPrimalSelection(out act)) return true;
            }
            else
            {
                if (TitanTime(out act)) return true;
                if (GarudaTime(out act)) return true;
                if (IfritTime(out act)) return true;
            }
        }

        // Big summon GCDs
        if (BrandOfPurgatoryPvE.CanUse(out act)) return true;
        if (UmbralFlarePvE.CanUse(out act)) return true;
        if (AstralFlarePvE.CanUse(out act)) return true;
        if (OutburstPvE.CanUse(out act)) return true;
        if (FountainOfFirePvE.CanUse(out act)) return true;
        if (UmbralImpulsePvE.CanUse(out act)) return true;
        if (AstralImpulsePvE.CanUse(out act)) return true;

        // ENHANCED Smart Ruin IV: Uses movement prediction
        if (!InBahamut && !InPhoenix && !InSolarBahamut && SummonTimeEndAfterGCD() && AttunmentTimeEndAfterGCD())
        {
            if (SmartRuinIV && HasFurtherRuin)
            {
                // ENHANCED: Use predicted movement instead of just IsMoving
                bool needInstant = ShouldPreferInstants();

                if (needInstant)
                {
                    if (RuinIvPvE.CanUse(out act, skipAoeCheck: true)) return true;
                }
                else
                {
                    if (RuinIiiPvE.EnoughLevel && RuinIiiPvE.CanUse(out act)) return true;
                    if (!RuinIiiPvE.Info.EnoughLevelAndQuest() && RuinIiPvE.EnoughLevel && RuinIiPvE.CanUse(out act)) return true;
                    if (!RuinIiPvE.Info.EnoughLevelAndQuest() && RuinPvE.CanUse(out act)) return true;
                    if (RuinIvPvE.CanUse(out act, skipAoeCheck: true)) return true;
                }
            }
            else
            {
                if (RuinIvPvE.CanUse(out act, skipAoeCheck: true)) return true;
            }
        }

        // Filler GCDs
        if (RuinIiiPvE.EnoughLevel && RuinIiiPvE.CanUse(out act)) return true;
        if (!RuinIiiPvE.Info.EnoughLevelAndQuest() && RuinIiPvE.EnoughLevel && RuinIiPvE.CanUse(out act)) return true;
        if (!RuinIiPvE.Info.EnoughLevelAndQuest() && RuinPvE.CanUse(out act)) return true;

        return base.GeneralGCD(out act);
    }

    #endregion

    // ========================================================================
    // DYNAMIC PRIMAL SELECTION (ENHANCED)
    // ========================================================================

    #region Dynamic Primal Selection

    /// <summary>
    /// ENHANCED: Dynamic Egi selection using Movement Prediction + Party Buff Tracking.
    /// - Movement Prediction: preemptively avoid Ifrit when movement is predicted
    /// - Party Buff Tracking: prioritize Titan (highest instant potency) during buff windows
    /// </summary>
    private bool DynamicPrimalSelection(out IAction? act)
    {
        act = null;

        // M11S Trophy Phase: Ifrit immer zuletzt
        if (M11SIfritLast && IsInM11STrophyPhaseCached())
        {
            LogDecision($"Primal: M11S Trophy ({_detectedTrophyWeapon}) -> Titan>Garuda>Ifrit");
            if (TitanTime(out act)) return true;
            if (GarudaTime(out act)) return true;
            if (IfritTime(out act)) return true;
            return false;
        }

        // NEW: Party buff window active -> Titan first (highest instant potency under buffs)
        if (UsePartyBuffTracking && IsInPartyBurstWindow)
        {
            LogDecision($"Primal: BURST ({_activeBuffCount} buffs, {_shortestBuffRemaining:F0}s rem) -> Titan>Garuda>Ifrit");
            if (TitanTime(out act)) return true;
            if (GarudaTime(out act)) return true;
            if (IfritTime(out act)) return true;
            return false;
        }

        // Searing Light active: Titan first
        if (HasSearingLight)
        {
            LogDecision("Primal: burst -> Titan>Garuda>Ifrit (Searing Light)");
            if (TitanTime(out act)) return true;
            if (GarudaTime(out act)) return true;
            if (IfritTime(out act)) return true;
            return false;
        }

        // ENHANCED: Use movement prediction instead of just IsMoving
        bool preferInstants = ShouldPreferInstants();

        LogDecision($"Primal: {(preferInstants ? "instants" : "casts")}, urgency={GetMovementUrgency():F1} ({_movementReason ?? "none"}), mode={GetCachedSpecialMode() ?? SpecialModes.Normal}");

        if (preferInstants)
        {
            if (TitanTime(out act)) return true;
            if (GarudaTime(out act)) return true;
            if (IfritTime(out act)) return true;
        }
        else
        {
            if (IfritTime(out act)) return true;
            if (TitanTime(out act)) return true;
            if (GarudaTime(out act)) return true;
        }

        return false;
    }

    #endregion

    // ========================================================================
    // DEFENSE HELPERS
    // ========================================================================

    #region Defense Helpers

    private bool IsDamageImminent()
    {
        if (_simEnabled && (_simRaidwideImminent || _simSharedImminent || _simTankbusterImminent))
        {
            LogDecision("IsDamageImminent=TRUE (SIM)");
            return true;
        }

        if (UseBossModIPC && BossModHints_IPCSubscriber.IsEnabled)
        {
            try
            {
                if (BossModHints_IPCSubscriber.Hints_IsRaidwideImminent?.Invoke(BossModLookahead) == true)
                { LogDecision("IsDamageImminent=TRUE (BossMod: Raidwide)"); return true; }
                if (BossModHints_IPCSubscriber.Hints_IsSharedImminent?.Invoke(BossModLookahead) == true)
                { LogDecision("IsDamageImminent=TRUE (BossMod: Shared)"); return true; }
                if (BossModHints_IPCSubscriber.Hints_IsTankbusterImminent?.Invoke(BossModLookahead) == true)
                { LogDecision("IsDamageImminent=TRUE (BossMod: TB)"); return true; }
            }
            catch (Exception ex)
            {
                if ((DateTime.Now - _lastIpcErrorLog).TotalSeconds > 10)
                { _lastIpcErrorLog = DateTime.Now; LogDecision($"IPC error: {ex.Message}"); }
            }
        }

        if (DataCenter.IsHostileCastingAOE) { LogDecision("IsDamageImminent=TRUE (RSR: HostileCastingAOE)"); return true; }
        if (DataCenter.IsMagicalDamageIncoming()) { LogDecision("IsDamageImminent=TRUE (RSR: MagicalDamageIncoming)"); return true; }
        return false;
    }

    private bool ShouldUseAddle()
    {
        if (_simEnabled && _simMagicalCast && _simRaidwideImminent)
        { LogDecision("ShouldUseAddle=TRUE (SIM)"); return true; }

        if (!DataCenter.IsMagicalDamageIncoming()) return false;
        if (HostileTarget == null || HostileTarget.HasStatus(false, StatusID.Addle)) return false;

        if (UseBossModIPC && BossModHints_IPCSubscriber.IsEnabled)
        {
            try
            {
                if (BossModHints_IPCSubscriber.Hints_IsRaidwideImminent?.Invoke(AddleLeadTime) == true)
                { LogDecision("ShouldUseAddle=TRUE (BossMod)"); return true; }
                if (BossModHints_IPCSubscriber.Hints_IsSharedImminent?.Invoke(AddleLeadTime) == true && !AddlePvE.Cooldown.IsCoolingDown)
                { LogDecision("ShouldUseAddle=TRUE (BossMod: Shared)"); return true; }
            }
            catch (Exception ex)
            {
                if ((DateTime.Now - _lastIpcErrorLog).TotalSeconds > 10)
                { _lastIpcErrorLog = DateTime.Now; LogDecision($"IPC error: {ex.Message}"); }
            }
        }

        if (!IsHostileCastRemainingWithin(AddleLeadTime)) return false;
        if (IsAnyHostileCastingKnownRaidwide()) { LogDecision("ShouldUseAddle=TRUE (RSR: Known Raidwide)"); return true; }
        if (DataCenter.IsHostileCastingAOE && !AddlePvE.Cooldown.IsCoolingDown) { LogDecision("ShouldUseAddle=TRUE (RSR: AOE)"); return true; }
        return false;
    }

    private static bool IsHostileCastRemainingWithin(float seconds)
    {
        if (DataCenter.AllHostileTargets == null) return false;
        for (int i = 0, n = DataCenter.AllHostileTargets.Count; i < n; i++)
        {
            var hostile = DataCenter.AllHostileTargets[i];
            if (hostile == null || !hostile.IsCasting || hostile.TotalCastTime <= 0) continue;
            float remaining = hostile.TotalCastTime - hostile.CurrentCastTime;
            if (remaining > 0 && remaining <= seconds) return true;
        }
        return false;
    }

    private static bool IsAnyHostileCastingKnownRaidwide()
    {
        if (DataCenter.AllHostileTargets == null) return false;
        for (int i = 0, n = DataCenter.AllHostileTargets.Count; i < n; i++)
        {
            var hostile = DataCenter.AllHostileTargets[i];
            if (hostile == null || !hostile.IsCasting) continue;
            if (DataCenter.IsHostileCastingArea(hostile)) return true;
        }
        return false;
    }

    #endregion

    // ========================================================================
    // PRIMAL SUMMON HELPERS
    // ========================================================================

    #region Primal Summon Helpers

    private bool TitanTime(out IAction? act)
    {
        if (SummonTitanIiPvE.CanUse(out act)) return true;
        if (!SummonTitanIiPvE.EnoughLevel && SummonTitanPvE.CanUse(out act)) return true;
        if (!SummonTitanPvE.Info.EnoughLevelAndQuest() && SummonTopazPvE.CanUse(out act)) return true;
        return false;
    }

    private bool GarudaTime(out IAction? act)
    {
        if (SummonGarudaIiPvE.CanUse(out act)) return true;
        if (!SummonGarudaIiPvE.EnoughLevel && SummonGarudaPvE.CanUse(out act)) return true;
        if (!SummonGarudaPvE.Info.EnoughLevelAndQuest() && SummonEmeraldPvE.CanUse(out act)) return true;
        return false;
    }

    private bool IfritTime(out IAction? act)
    {
        if (SummonIfritIiPvE.CanUse(out act)) return true;
        if (!SummonIfritIiPvE.EnoughLevel && SummonIfritPvE.CanUse(out act)) return true;
        if (!SummonIfritPvE.Info.EnoughLevelAndQuest() && SummonRubyPvE.CanUse(out act)) return true;
        return false;
    }

    private bool GemshineTime(out IAction? act)
    {
        if (RubyRitePvE.CanUse(out act)) return true;
        if (EmeraldRitePvE.CanUse(out act)) return true;
        if (TopazRitePvE.CanUse(out act)) return true;
        if (RubyRuinIiiPvE.CanUse(out act)) return true;
        if (EmeraldRuinIiiPvE.CanUse(out act)) return true;
        if (TopazRuinIiiPvE.CanUse(out act)) return true;
        if (RubyRuinIiPvE.CanUse(out act)) return true;
        if (EmeraldRuinIiPvE.CanUse(out act)) return true;
        if (TopazRuinIiPvE.CanUse(out act)) return true;
        if (!SummonIfritPvE.Info.EnoughLevelAndQuest() && RubyRuinPvE.CanUse(out act)) return true;
        if (!SummonGarudaPvE.Info.EnoughLevelAndQuest() && EmeraldRuinPvE.CanUse(out act)) return true;
        if (!SummonTitanPvE.Info.EnoughLevelAndQuest() && TopazRuinPvE.CanUse(out act)) return true;
        return false;
    }

    private bool PreciousBrillianceTime(out IAction? act)
    {
        if (RubyCatastrophePvE.CanUse(out act)) return true;
        if (EmeraldCatastrophePvE.CanUse(out act)) return true;
        if (TopazCatastrophePvE.CanUse(out act)) return true;
        if (RubyDisasterPvE.CanUse(out act)) return true;
        if (EmeraldDisasterPvE.CanUse(out act)) return true;
        if (TopazDisasterPvE.CanUse(out act)) return true;
        if (RubyOutburstPvE.CanUse(out act)) return true;
        if (EmeraldOutburstPvE.CanUse(out act)) return true;
        if (TopazOutburstPvE.CanUse(out act)) return true;
        return false;
    }

    #endregion

    // ========================================================================
    // DIAGNOSTICS & SIMULATION (ENHANCED)
    // ========================================================================

    #region Diagnostics

    public override void DisplayRotationStatus()
    {
        if (!ShowDiagnostics)
        {
            ImGui.TextWrapped("Enable 'Diagnostics' in rotation config to show real-time decision state.");
            return;
        }

        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "=== SMN Experimental Diagnostics ===");
        ImGui.Separator();

        DrawSimulationControls();
        ImGui.Separator();

        // NEW: Party Buff Tracker Panel
        DrawPartyBuffPanel();
        ImGui.Separator();

        // NEW: Movement Prediction Panel
        DrawMovementPredictionPanel();
        ImGui.Separator();

        DrawReactionPreview();
        ImGui.Separator();
        DrawLiveState();
        ImGui.Separator();
        DrawCastIdLookup();
        ImGui.Separator();
        DrawDecisionLog();
    }

    // ----- NEW: Party Buff Panel -----
    private void DrawPartyBuffPanel()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.4f, 1f, 0.4f, 1f), "[Party Buff Tracker]");

        if (!UsePartyBuffTracking)
        {
            ImGui.TextDisabled("  (disabled in config)");
            return;
        }

        int buffCount = GetActivePartyBuffCount();
        bool inBurst = IsInPartyBurstWindow;

        var burstColor = inBurst
            ? new System.Numerics.Vector4(0.2f, 1f, 0.2f, 1f)
            : new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 1f);
        ImGui.TextColored(burstColor, $"  Active Buffs: {buffCount}/{BuffWindowThreshold} | Burst: {(inBurst ? "YES" : "no")}");

        if (buffCount > 0 && _shortestBuffRemaining < float.MaxValue)
            ImGui.Text($"  Shortest remaining: {_shortestBuffRemaining:F1}s");

        if (_targetHasDebuff)
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.6f, 0.2f, 1f), "  Target has raid debuff (Dokumori/Chain)");

        float nextWindow = EstimatedSecondsUntilNextBuffWindow;
        ImGui.Text($"  Next window est: {nextWindow:F0}s | Avg interval: {_avgBuffWindowInterval:F0}s ({_buffWindowsSeen} seen)");

        // Show individual buffs on party members
        if (ImGui.TreeNode("Active Raid Buffs"))
        {
            var party = PartyMembers;
            if (party != null)
            {
                foreach (var member in party)
                {
                    if (member == null || member.IsDead) continue;
                    var statusList = member.StatusList;
                    if (statusList == null) continue;

                    foreach (var status in statusList)
                    {
                        for (int i = 0; i < RaidBuffStatusIds.Length; i++)
                        {
                            if (status.StatusId == RaidBuffStatusIds[i])
                            {
                                ImGui.Text($"    {member.Name}: {(StatusID)RaidBuffStatusIds[i]} ({status.RemainingTime:F1}s)");
                                break;
                            }
                        }
                    }
                }
            }
            ImGui.TreePop();
        }
    }

    // ----- NEW: Movement Prediction Panel -----
    private void DrawMovementPredictionPanel()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.4f, 0.8f, 1f, 1f), "[Movement Prediction]");

        if (!UseMovementPrediction)
        {
            ImGui.TextDisabled("  (disabled in config)");
            return;
        }

        float urgency = GetMovementUrgency();
        bool preferInstants = ShouldPreferInstants();

        var urgencyColor = urgency switch
        {
            >= 0.7f => new System.Numerics.Vector4(1f, 0.2f, 0.2f, 1f),   // red
            >= 0.4f => new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f),   // yellow
            _ => new System.Numerics.Vector4(0.2f, 1f, 0.2f, 1f),         // green
        };

        ImGui.TextColored(urgencyColor, $"  Urgency: {urgency:F2} | Prefer Instants: {(preferInstants ? "YES" : "no")}");
        ImGui.Text($"  Reason: {_movementReason ?? "none"}");
        ImGui.Text($"  IsMoving: {IsMoving} | Lookahead: {MovementLookahead:F1}s");
        ImGui.Text($"  SpecialMode: {GetCachedSpecialMode() ?? "Normal"}");
    }

    // ----- Simulation Controls -----
    private void DrawSimulationControls()
    {
        if (_simEnabled)
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.2f, 0.2f, 1f), "!! SIMULATION ACTIVE !!");

        bool simEnabled = _simEnabled;
        if (ImGui.Checkbox("Simulation aktivieren", ref simEnabled))
        {
            _simEnabled = simEnabled;
            if (!simEnabled)
            {
                _simRaidwideImminent = false; _simSharedImminent = false;
                _simTankbusterImminent = false; _simMagicalCast = false;
                _simMoving = false; _simSpecialModeIndex = 0;
            }
        }
        if (!_simEnabled) return;

        ImGui.Indent(10);
        bool rw = _simRaidwideImminent;
        if (ImGui.Checkbox("Raidwide", ref rw)) _simRaidwideImminent = rw;
        ImGui.SameLine();
        bool sh = _simSharedImminent;
        if (ImGui.Checkbox("Shared", ref sh)) _simSharedImminent = sh;
        ImGui.SameLine();
        bool tb = _simTankbusterImminent;
        if (ImGui.Checkbox("TB", ref tb)) _simTankbusterImminent = tb;
        bool mag = _simMagicalCast;
        if (ImGui.Checkbox("Magical", ref mag)) _simMagicalCast = mag;
        bool mov = _simMoving;
        if (ImGui.Checkbox("IsMoving", ref mov)) _simMoving = mov;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        int specIdx = _simSpecialModeIndex;
        if (ImGui.Combo("SpecialMode", ref specIdx, SpecialModeNames, SpecialModeNames.Length))
            _simSpecialModeIndex = specIdx;
        ImGui.Unindent(10);
    }

    // ----- Reaction Preview -----
    private void DrawReactionPreview()
    {
        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "[Reaction Preview]");

        bool damageImm = IsDamageImminent();
        bool shouldAddle = false;
        try { shouldAddle = ShouldUseAddle(); } catch { }

        ColoredBool("  IsDamageImminent", damageImm);
        ColoredBool("  ShouldUseAddle", shouldAddle);

        bool addleOnCD = AddlePvE.Cooldown.IsCoolingDown;
        bool targetHasAddle = HostileTarget?.HasStatus(false, StatusID.Addle) ?? false;
        ImGui.Text($"  Addle: {(addleOnCD ? $"CD ({AddlePvE.Cooldown.RecastTimeRemainOneCharge:F1}s)" : "Ready")} | Target: {(targetHasAddle ? "HAS ADDLE" : "no")}");

        bool aegisReady = !RadiantAegisPvE.Cooldown.IsCoolingDown;
        ColoredBool("  Aegis fires", damageImm && SmartAegis && aegisReady);

        // Primal Selection Preview
        ImGui.Spacing();
        bool preferInstants = ShouldPreferInstants();
        string primalOrder = preferInstants
            ? "Titan > Garuda > Ifrit (Instants)"
            : "Ifrit > Titan > Garuda (Casts)";

        if (IsInPartyBurstWindow)
            primalOrder = $"Titan > Garuda > Ifrit (BURST: {_activeBuffCount} buffs)";
        else if (M11SIfritLast && IsInM11STrophyPhaseCached())
            primalOrder = "Titan > Garuda > Ifrit (M11S Trophy)";

        ImGui.Text($"  Primal: {primalOrder}");
    }

    // ----- Live State -----
    private void DrawLiveState()
    {
        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "[Live State]");

        ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1f, 1f), "Hostile Casts:");
        if (DataCenter.AllHostileTargets != null)
        {
            bool anyCast = false;
            for (int i = 0, n = DataCenter.AllHostileTargets.Count; i < n; i++)
            {
                var h = DataCenter.AllHostileTargets[i];
                if (h == null || !h.IsCasting || h.TotalCastTime <= 0) continue;
                anyCast = true;
                float remaining = h.TotalCastTime - h.CurrentCastTime;
                uint castId = h.CastActionId;
                bool inList = OtherConfiguration.HostileCastingArea.Contains(castId);
                string actionName = GetActionName(castId);
                var castColor = inList
                    ? new System.Numerics.Vector4(0.2f, 1f, 0.2f, 1f)
                    : new System.Numerics.Vector4(1f, 1f, 1f, 1f);
                ImGui.TextColored(castColor, $"  {castId} {actionName} | {remaining:F1}s | {(inList ? "IN LIST" : "not listed")}");
            }
            if (!anyCast) ImGui.TextDisabled("  (no active casts)");
        }

        ImGui.Spacing();
        ImGui.Text($"  Territory: {DataCenter.TerritoryID} | InCombat: {InCombat}");
        ImGui.Text($"  Phase: {(InBahamut ? "Bahamut" : InPhoenix ? "Phoenix" : InSolarBahamut ? "SolarBahamut" : "Primal")} | SummonTime: {SummonTime:F1}s");
    }

    // ----- Cast ID Lookup -----
    private static void DrawCastIdLookup()
    {
        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "[Cast ID Lookup]");
        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("Cast ID", ref _lookupCastId, 0, 0);
        ImGui.SameLine();
        if (ImGui.Button("Lookup"))
        {
            uint id = (uint)_lookupCastId;
            if (id == 0) _lookupResult = "Invalid ID (0)";
            else
            {
                string name = GetActionName(id);
                bool inList = OtherConfiguration.HostileCastingArea.Contains(id);
                _lookupResult = $"{id}: {name} | {(inList ? "IN HostileCastingArea" : "NOT in list")}";
            }
        }
        if (_lookupCastId > 0)
        {
            uint lookupId = (uint)_lookupCastId;
            bool inList = OtherConfiguration.HostileCastingArea.Contains(lookupId);
            ImGui.SameLine();
            if (!inList)
            {
                if (ImGui.Button("+ Add"))
                {
                    OtherConfiguration.HostileCastingArea.Add(lookupId);
                    OtherConfiguration.Save();
                    _lookupResult = $"ADDED: {lookupId} ({GetActionName(lookupId)})";
                }
            }
            else
            {
                if (ImGui.Button("- Remove"))
                {
                    OtherConfiguration.HostileCastingArea.Remove(lookupId);
                    OtherConfiguration.Save();
                    _lookupResult = $"REMOVED: {lookupId} ({GetActionName(lookupId)})";
                }
            }
        }
        if (_lookupResult != null) ImGui.Text($"  {_lookupResult}");
    }

    // ----- Decision Log -----
    private static void DrawDecisionLog()
    {
        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "[Decision Log]");
        if (_decisionLogCount == 0)
        {
            ImGui.TextDisabled("  (no entries yet)");
            return;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear")) { _decisionLogCount = 0; _decisionLogIndex = 0; }

        int shown = 0;
        for (int i = 0; i < _decisionLogCount && shown < 15; i++)
        {
            int idx = (_decisionLogIndex - 1 - i + _decisionLog.Length) % _decisionLog.Length;
            if (_decisionLog[idx] != null) { ImGui.Text($"  {_decisionLog[idx]}"); shown++; }
        }
    }

    // ----- Helpers -----
    private static void ColoredBool(string label, bool value)
    {
        var color = value
            ? new System.Numerics.Vector4(0.2f, 1f, 0.2f, 1f)
            : new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 1f);
        ImGui.TextColored(color, $"{label}: {(value ? "TRUE" : "FALSE")}");
    }

    private static string GetActionName(uint actionId)
    {
        if (_actionNameCache.TryGetValue(actionId, out var cached)) return cached;
        try
        {
            var sheet = Service.GetSheet<Lumina.Excel.Sheets.Action>();
            if (sheet == null) return "?";
            var action = sheet.GetRow(actionId);
            if (action.RowId == 0) return "?";
            string name = action.Name.ToString();
            var result = string.IsNullOrEmpty(name) ? $"(id:{actionId})" : name;
            _actionNameCache[actionId] = result;
            return result;
        }
        catch { return "?"; }
    }

    #endregion

    // ========================================================================
    // HEAL OVERRIDE
    // ========================================================================

    #region Heal Override

    public override bool CanHealSingleSpell
    {
        get
        {
            int aliveHealerCount = 0;
            IEnumerable<IBattleChara> healers = PartyMembers.GetJobCategory(JobRole.Healer);
            foreach (IBattleChara h in healers)
            {
                if (!h.IsDead) aliveHealerCount++;
            }
            return base.CanHealSingleSpell && (GCDHeal || aliveHealerCount == 0);
        }
    }

    #endregion
}
