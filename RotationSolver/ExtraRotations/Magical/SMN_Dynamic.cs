using System.ComponentModel;
using System.Text.Json;
using ECommons.DalamudServices;
using RotationSolver.Basic.Configuration;
using RotationSolver.IPC;

namespace RotationSolver.ExtraRotations.Magical;

[Rotation("SMN Dynamic", CombatType.PvE, GameVersion = "7.45")]
[SourceCode(Path = "main/ExtraRotations/Magical/SMN_Dynamic.cs")]

public sealed class SMN_Dynamic : SummonerRotation
{
    #region Config Options

    [RotationConfig(CombatType.PvE, Name = "Dynamic Egi Selection (avoid Ifrit casts while moving)")]
    public bool DynamicEgis { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Auto Addle on raidwide casts")]
    public bool AutoAddle { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Smart Radiant Aegis: Use on any incoming damage (raidwide/stack/AoE)")]
    public bool SmartAegis { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Smart Potion (only use during Searing Light)")]
    public bool SmartPotion { get; set; } = true;

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

    [RotationConfig(CombatType.PvE, Name = "Opener Addle: Use Addle in opener (may miss early raidwides in M9S/M10S/M11S/M12S)")]
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

    #region Helper Properties

    private static bool HasFurtherRuin => StatusHelper.PlayerHasStatus(true, StatusID.FurtherRuin_2701);

    /// <summary>
    /// Returns true if the given action's target is a boss that is dying.
    /// Consolidates the repeated IsBossFromTTK/IsBossFromIcon/IsDying check.
    /// </summary>
    private static bool IsActionTargetBossDying(IBaseAction action)
    {
        var target = action.Target.Target;
        return (target.IsBossFromTTK() || target.IsBossFromIcon()) && target.IsDying();
    }

    /// <summary>
    /// Checks whether Necrotize or Fester should be spent given current conditions.
    /// Shared logic: big summon → immediate, solar+buff → spend, boss dying → dump, ED coming → avoid overcap.
    /// </summary>
    private bool ShouldSpendFesterStack(IBaseAction action, bool inBigInvocation, bool inSolarUnique)
    {
        if (inBigInvocation) return true;
        if ((inSolarUnique && HasSearingLight) || !SearingLightPvE.EnoughLevel) return true;
        if (IsActionTargetBossDying(action)) return true;
        if (EnergyDrainPvE.Cooldown.WillHaveOneChargeGCD(2)) return true;
        return false;
    }

    // SpecialMode string constants — avoid magic strings in comparisons
    private static class SpecialModes
    {
        public const string Normal = "Normal";
        public const string Pyretic = "Pyretic";
        public const string NoMovement = "NoMovement";
        public const string Freezing = "Freezing";
    }

    // Per-Frame Cache für teure Berechnungen (wird jede GCD neu berechnet)
    private bool _pauseForDirectionCached;
    private bool _pauseForDirectionValid;
    private string? _specialModeCache;
    private bool _specialModeCacheValid;
    private bool _m11sTrophyCached;
    private bool _m11sTrophyValid;

    // ForbiddenDirections JSON cache
    private (float Center, float HalfWidth)[]? _forbiddenArcsCached;
    private string? _forbiddenArcsJson;

    // === Simulation State ===
    // Ermöglicht das Testen defensiver Reaktionen ohne echte Boss-Angriffe.
    // Overrides werden nur im Diagnostics-Panel aktiviert und beeinflussen die echten Decision-Methoden.
    private static bool _simEnabled;
    private static bool _simRaidwideImminent;
    private static bool _simSharedImminent;
    private static bool _simTankbusterImminent;
    private static bool _simMagicalCast;
    private static bool _simMoving;
    private static int _simSpecialModeIndex; // 0=Normal, 1=Pyretic, 2=NoMovement, 3=Freezing

    // Cast-ID Lookup
    private static int _lookupCastId;
    private static string? _lookupResult;

    // Decision Log (Ring-Buffer, letzte 40 Einträge)
    private static readonly string[] _decisionLog = new string[40];
    private static int _decisionLogIndex;
    private static int _decisionLogCount;

    // IPC error throttle
    private DateTime _lastIpcErrorLog = DateTime.MinValue;

    private static readonly string[] SpecialModeNames = [SpecialModes.Normal, SpecialModes.Pyretic, SpecialModes.NoMovement, SpecialModes.Freezing];

    // Lumina lookup caches (static game data, never changes at runtime)
    private static readonly Dictionary<uint, string> _actionNameCache = new();
    private static readonly Dictionary<uint, string> _attackTypeCache = new();

    private static void LogDecision(string message)
    {
        _decisionLog[_decisionLogIndex] = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _decisionLogIndex = (_decisionLogIndex + 1) % _decisionLog.Length;
        if (_decisionLogCount < _decisionLog.Length) _decisionLogCount++;
    }

    /// <summary>
    /// Cached BossMod SpecialMode - wird nur 1x pro Frame abgefragt statt in jeder Methode.
    /// </summary>
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

    /// <summary>
    /// M12S: Gibt die Restdauer des _Gen_Direction Debuffs (Status ID 3558) zurück.
    /// Returns 0 wenn der Debuff nicht vorhanden ist.
    /// </summary>
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

    /// <summary>
    /// M12S: Prüft ob die Rotation wegen Directed Grotesquerie pausiert werden soll.
    /// Ergebnis wird pro Frame gecacht (5 Aufrufer pro Frame: GeneralGCD, Emergency, Attack, General, Defense).
    /// </summary>
    private bool ShouldPauseForDirection()
    {
        if (_pauseForDirectionValid) return _pauseForDirectionCached;
        _pauseForDirectionValid = true;
        _pauseForDirectionCached = ComputeShouldPauseForDirection();
        return _pauseForDirectionCached;
    }

    private bool ComputeShouldPauseForDirection()
    {
        if (!PauseOnDirectedGrotesquerie)
            return false;

        var remaining = DirectedGrotesquerieRemaining;
        if (remaining <= 0f || remaining > DirectionPauseLeadTime)
            return false;

        // Debuff läuft bald ab - prüfe ob BossMod ForbiddenDirections Smart-Modus möglich
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

        // Fallback: komplett pausieren wenn Debuff kurz vor Ablauf
        return true;
    }

    /// <summary>
    /// Parses forbidden direction arcs from JSON, caching the result.
    /// Only re-parses when the JSON string changes.
    /// </summary>
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

    /// <summary>
    /// Prüft ob eine Blickrichtung (in Radians) in einem der verbotenen Bögen liegt.
    /// </summary>
    private static bool IsDirectionForbidden(float directionRad, (float Center, float HalfWidth)[] arcs)
    {
        for (int i = 0; i < arcs.Length; i++)
        {
            var diff = MathF.IEEERemainder(directionRad - arcs[i].Center, 2f * MathF.PI);
            if (MathF.Abs(diff) < arcs[i].HalfWidth)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Cached M11S Trophy Phase check — scans all objects but only once per frame.
    /// </summary>
    private bool IsInM11STrophyPhaseCached()
    {
        if (_m11sTrophyValid) return _m11sTrophyCached;
        _m11sTrophyValid = true;
        _m11sTrophyCached = IsInM11STrophyPhase();
        return _m11sTrophyCached;
    }

    /// <summary>
    /// M11S Trophy Weapon Phase Erkennung.
    /// Trophy Weapon Adds DataId: Axe (0x4AF0), Scythe (0x4AF1), Sword (0x4AF2).
    /// Wenn diese Adds existieren, sind wir in einer Trophy-Weapon-Phase (regulär oder Ultimate).
    /// Scannt Svc.Objects direkt, da die Trophy Weapons nicht targetbar sind und daher
    /// nicht in AllHostileTargets erscheinen.
    /// </summary>
    private static bool IsInM11STrophyPhase()
    {
        if (!DataCenter.IsInM11S) return false;

        var objects = Svc.Objects;
        if (objects == null) return false;

        int count = objects.Length;
        for (int i = 0; i < count; i++)
        {
            var obj = objects[i];
            if (obj == null) continue;
            // Trophy Weapon Adds: Axe (0x4AF0), Scythe (0x4AF1), Sword (0x4AF2)
            if (obj.BaseId is 0x4AF0 or 0x4AF1 or 0x4AF2)
                return true;
        }
        return false;
    }

    #endregion

    #region Countdown Logic

    protected override IAction? CountDownAction(float remainTime)
    {
        if (SummonCarbunclePvE.CanUse(out IAction? act))
        {
            return act;
        }

        // Precast: Ruin III so timen, dass der Cast genau beim Pull-Ende fertig ist
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

    #region Heal & Defense Abilities

    [RotationDesc(ActionID.LuxSolarisPvE)]
    protected override bool HealAreaAbility(IAction nextGCD, out IAction? act)
    {
        if (LuxSolarisPvE.CanUse(out act))
        {
            return true;
        }
        return base.HealAreaAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.RekindlePvE)]
    protected override bool HealSingleAbility(IAction nextGCD, out IAction? act)
    {
        if (RekindlePvE.CanUse(out act, targetOverride: TargetType.LowHP))
        {
            return true;
        }
        return base.HealSingleAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.AddlePvE, ActionID.RadiantAegisPvE)]
    protected override bool DefenseAreaAbility(IAction nextGCD, out IAction? act)
    {
        if (ShouldPauseForDirection())
            return base.DefenseAreaAbility(nextGCD, out act);

        // Addle: nur wenn Ziel castet und noch kein Addle hat
        if (AutoAddle && ShouldUseAddle())
        {
            if (AddlePvE.CanUse(out act))
            {
                return true;
            }
        }

        // Radiant Aegis: Schild bei jeder Art von eingehendem Schaden
        if (SmartAegis && !IsLastAction(false, RadiantAegisPvE) && RadiantAegisPvE.CanUse(out act, usedUp: true))
        {
            return true;
        }
        return base.DefenseAreaAbility(nextGCD, out act);
    }

    #endregion

    #region oGCD Logic

    [RotationDesc(ActionID.LuxSolarisPvE)]
    protected override bool GeneralAbility(IAction nextGCD, out IAction? act)
    {
        if (ShouldPauseForDirection())
            return base.GeneralAbility(nextGCD, out act);

        // Lux Solaris: Am Ende der Big Summon Phase nutzen (nach letztem Umbral Impulse),
        // nicht sofort — so wird der oGCD-Slot nicht verschwendet wenn wichtigere Weaves anstehen.
        // Fallback: feuert trotzdem wenn RefulgentLux bald ausläuft (≤2 GCDs).
        if (LuxSolarisPvE.CanUse(out act))
        {
            bool bigSummonEnding = (InBahamut || InPhoenix || InSolarBahamut) && SummonTime <= GCDTime(1) + 0.5f;
            bool statusExpiring = StatusHelper.PlayerWillStatusEndGCD(2, 0, true, StatusID.RefulgentLux);
            bool notInBigSummon = !InBahamut && !InPhoenix && !InSolarBahamut;
            // Outside big summon: only use if no competing aetherflow oGCDs are imminent
            bool safeToUseLux = notInBigSummon && (!HasAetherflowStacks || !EnergyDrainPvE.Cooldown.WillHaveOneChargeGCD(1));

            if (bigSummonEnding || statusExpiring || safeToUseLux)
            {
                LogDecision($"LuxSolaris: {(bigSummonEnding ? "endingSummon" : statusExpiring ? "statusExpiring" : "filler")}");
                return true;
            }
        }

        if (StatusHelper.PlayerWillStatusEndGCD(2, 0, true, StatusID.FirebirdTrance))
        {
            if (RekindlePvE.CanUse(out act))
            {
                return true;
            }
        }

        if (StatusHelper.PlayerWillStatusEndGCD(3, 0, true, StatusID.FirebirdTrance))
        {
            if (RekindlePvE.CanUse(out act, targetOverride: TargetType.LowHP))
            {
                return true;
            }
        }

        // Smart Potion: only use during Searing Light
        if (SmartPotion)
        {
            if (HasSearingLight && InCombat && UseBurstMedicine(out act))
            {
                return true;
            }
        }
        else
        {
            if (InCombat && UseBurstMedicine(out act))
            {
                return true;
            }
        }

        // Radiant Aegis on cooldown: Charges frei nutzen wenn kein Schaden ansteht
        if (AegisOnCooldown && InCombat && !IsLastAction(false, RadiantAegisPvE)
            && RadiantAegisPvE.CanUse(out act, usedUp: true))
        {
            return true;
        }

        return base.GeneralAbility(nextGCD, out act);
    }

    protected override bool AttackAbility(IAction nextGCD, out IAction? act)
    {
        if (ShouldPauseForDirection())
            return base.AttackAbility(nextGCD, out act);

        bool inBigInvocation = !SummonBahamutPvE.EnoughLevel || InBahamut || InPhoenix || InSolarBahamut;
        bool inSolarUnique = SummonSolarBahamutPvE.EnoughLevel ? !InBahamut && !InPhoenix && InSolarBahamut : InBahamut && !InPhoenix;
        bool burstInSolar = (SummonSolarBahamutPvE.EnoughLevel && InSolarBahamut) || (!SummonSolarBahamutPvE.EnoughLevel && InBahamut) || !SummonBahamutPvE.EnoughLevel;

        if (burstInSolar)
        {
            if (SearingLightPvE.CanUse(out act))
            {
                LogDecision($"SearingLight: burst in {(InSolarBahamut ? "Solar" : InBahamut ? "Bahamut" : "preBahamut")}");
                return true;
            }
        }

        // Punkt 4: Opener Addle - im Opener als oGCD-Weave während Big Summon
        // Spieler entscheidet per Config ob Opener Addle sinnvoll ist (fight-abhängig)
        if (OpenerAddle && inBigInvocation && CombatElapsedLess(OpenerAddleWindow)
            && HostileTarget != null && !HostileTarget.HasStatus(false, StatusID.Addle))
        {
            if (AddlePvE.CanUse(out act))
            {
                return true;
            }
        }

        if (inBigInvocation)
        {
            bool summonActiveOrLowLevel = SummonTime > 0f || !SummonBahamutPvE.EnoughLevel;

            if (EnergySiphonPvE.CanUse(out act) && (IsActionTargetBossDying(EnergySiphonPvE) || summonActiveOrLowLevel))
                return true;

            if (EnergyDrainPvE.CanUse(out act) && (IsActionTargetBossDying(EnergyDrainPvE) || summonActiveOrLowLevel))
                return true;

            if (EnkindleBahamutPvE.CanUse(out act) && (IsActionTargetBossDying(EnkindleBahamutPvE) || summonActiveOrLowLevel))
                return true;

            if (EnkindleSolarBahamutPvE.CanUse(out act) && (IsActionTargetBossDying(EnkindleSolarBahamutPvE) || summonActiveOrLowLevel))
                return true;

            if (EnkindlePhoenixPvE.CanUse(out act) && (IsActionTargetBossDying(EnkindlePhoenixPvE) || summonActiveOrLowLevel))
                return true;

            if (DeathflarePvE.CanUse(out act) && (IsActionTargetBossDying(DeathflarePvE) || summonActiveOrLowLevel))
                return true;

            if (SunflarePvE.CanUse(out act) && (IsActionTargetBossDying(SunflarePvE) || summonActiveOrLowLevel))
                return true;

            // Searing Flash inside big summon: fire during active summon or boss dying
            if (SearingFlashPvE.CanUse(out act) && (IsActionTargetBossDying(SearingFlashPvE) || summonActiveOrLowLevel))
                return true;
        }

        if (MountainBusterPvE.CanUse(out act))
            return true;

        if (PainflarePvE.CanUse(out act))
        {
            if ((inSolarUnique && HasSearingLight) || !SearingLightPvE.EnoughLevel)
                return true;
            if (IsActionTargetBossDying(PainflarePvE))
                return true;
        }

        // Necrotize/Fester: aggressiver ausgeben in Big Summon Phase
        // FFLogs: Top-Spieler double-weaven 2x Necrotize nach Energy Drain in jedem Big Summon.
        if (NecrotizePvE.CanUse(out act) && ShouldSpendFesterStack(NecrotizePvE, inBigInvocation, inSolarUnique))
            return true;

        if (FesterPvE.CanUse(out act) && ShouldSpendFesterStack(FesterPvE, inBigInvocation, inSolarUnique))
            return true;

        // Searing Flash outside big summon: only dump on dying boss (safety net)
        if (SearingFlashPvE.CanUse(out act) && IsActionTargetBossDying(SearingFlashPvE))
            return true;
        return base.AttackAbility(nextGCD, out act);
    }

    protected override bool EmergencyAbility(IAction nextGCD, out IAction? act)
    {
        if (ShouldPauseForDirection())
            return base.EmergencyAbility(nextGCD, out act);

        // Addle: Entkoppelt von IsDamageImminent - ShouldUseAddle hat eigene Timing-Logik
        // (BossMod: IsRaidwideImminent(AddleLeadTime), RSR: Cast-Remaining ≤ AddleLeadTime)
        // So feuert Addle auch wenn IsDamageImminent den Cast nicht als AOE erkennt,
        // und wird nicht von Aegis verdrängt wenn das Timing noch nicht stimmt.
        if (AutoAddle && ShouldUseAddle())
        {
            if (AddlePvE.CanUse(out act))
            {
                return true;
            }
        }

        // Radiant Aegis: Schild bei drohendem Schaden (breiteres Fenster via IsDamageImminent)
        if (SmartAegis && IsDamageImminent()
            && !IsLastAction(false, RadiantAegisPvE) && RadiantAegisPvE.CanUse(out act, usedUp: true))
        {
            return true;
        }

        if (SwiftcastPvE.CanUse(out act))
        {
            if (AddSwiftcastOnRaise && nextGCD.IsTheSameTo(false, ResurrectionPvE))
            {
                return true;
            }
            if (AddSwiftcastOnGaruda && nextGCD.IsTheSameTo(false, SlipstreamPvE) && ElementalMasteryTrait.EnoughLevel && !InBahamut && !InPhoenix && !InSolarBahamut)
            {
                return true;
            }
        }

        return base.EmergencyAbility(nextGCD, out act);
    }

    #endregion

    #region GCD Logic

    [RotationDesc(ActionID.CrimsonCyclonePvE)]
    protected override bool MoveForwardGCD(out IAction? act)
    {
        if (CrimsonCyclonePvE.CanUse(out act))
        {
            return true;
        }
        return base.MoveForwardGCD(out act);
    }

    [RotationDesc(ActionID.PhysickPvE)]
    protected override bool HealSingleGCD(out IAction? act)
    {
        if (GCDHeal && PhysickPvE.CanUse(out act))
        {
            return true;
        }
        return base.HealSingleGCD(out act);
    }

    [RotationDesc(ActionID.ResurrectionPvE)]
    protected override bool RaiseGCD(out IAction? act)
    {
        // SBRaise = true: Raise jederzeit (auch in Solar Bahamut)
        // SBRaise = false: Raise nur außerhalb von Solar Bahamut
        if (!InSolarBahamut || SBRaise)
        {
            if (ResurrectionPvE.CanUse(out act))
            {
                return true;
            }
        }
        return base.RaiseGCD(out act);
    }

    protected override bool GeneralGCD(out IAction? act)
    {
        // Per-Frame Caches invalidieren (GeneralGCD ist der erste Aufruf pro Zyklus)
        _pauseForDirectionValid = false;
        _specialModeCacheValid = false;
        _m11sTrophyValid = false;

        // M12S: Rotation pausieren wenn Directed Grotesquerie aktiv
        if (ShouldPauseForDirection())
        {
            LogDecision("Paused: Directed Grotesquerie");
            act = null;
            return false;
        }

        // Summon Carbuncle if needed
        if (SummonCarbunclePvE.CanUse(out act))
        {
            return true;
        }

        // Big summon phase (Bahamut/Phoenix/Solar Bahamut)
        if (SummonBahamutPvE.CanUse(out act))
        {
            return true;
        }
        if (!SummonBahamutPvE.Info.EnoughLevelAndQuest() && DreadwyrmTrancePvE.CanUse(out act))
        {
            return true;
        }
        if (IsBurst && !SearingLightPvE.Cooldown.IsCoolingDown && SummonSolarBahamutPvE.CanUse(out act))
        {
            return true;
        }

        // Garuda: Slipstream
        if (SlipstreamPvE.CanUse(out act, skipCastingCheck: AddSwiftcastOnGaruda && ((!SwiftcastPvE.Cooldown.IsCoolingDown && IsMoving) || HasSwift)))
        {
            return true;
        }

        // Ifrit: Crimson Cyclone + Strike
        if ((!IsMoving || AddCrimsonCycloneMoving) && CrimsonCyclonePvE.CanUse(out act) && (AddCrimsonCyclone || CrimsonCyclonePvE.Target.Target.DistanceToPlayer() <= CrimsonCycloneDistance))
        {
            return true;
        }

        if (CrimsonStrikePvE.CanUse(out act))
        {
            return true;
        }

        // AoE Gemshine
        if (PreciousBrillianceTime(out act))
        {
            return true;
        }

        // ST Gemshine
        if (GemshineTime(out act))
        {
            return true;
        }

        // Pre-Bahamut Aethercharge
        if (!DreadwyrmTrancePvE.Info.EnoughLevelAndQuest() && HasHostilesInRange && AetherchargePvE.CanUse(out act))
        {
            return true;
        }

        // Primal summon phase - DYNAMIC EGI SELECTION
        if (!InBahamut && !InPhoenix && !InSolarBahamut)
        {
            // Moving: Ruin IV sofort nutzen (instant cast, perfekt für Movement)
            if (MovementRuinIV && IsMoving && HasFurtherRuin && SummonTimeEndAfterGCD() && AttunmentTimeEndAfterGCD()
                && RuinIvPvE.CanUse(out act, skipAoeCheck: true))
            {
                return true;
            }

            if (DynamicEgis)
            {
                if (DynamicPrimalSelection(out act))
                {
                    return true;
                }
            }
            else
            {
                // Default order: Titan > Garuda > Ifrit
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

        // Smart Ruin IV Logik: Ruin III vs Ruin IV Entscheidung
        // FFLogs-Analyse: Top-Spieler nutzen Ruin IV opportunistisch bei Bewegung,
        // nicht als erzwungenen Dump vor Big Summon. Ruin III wird bei Stillstand bevorzugt.
        if (!InBahamut && !InPhoenix && !InSolarBahamut && SummonTimeEndAfterGCD() && AttunmentTimeEndAfterGCD())
        {
            if (SmartRuinIV && HasFurtherRuin)
            {
                bool needInstant = IsMoving;

                // BossMod SpecialMode (gecacht): NoMovement → Casts OK, Freezing/Pyretic → Instants
                {
                    var ruinMode = GetCachedSpecialMode();
                    if (ruinMode == SpecialModes.NoMovement) needInstant = false;
                    else if (ruinMode is SpecialModes.Freezing or SpecialModes.Pyretic) needInstant = true;
                }

                if (needInstant)
                {
                    if (RuinIvPvE.CanUse(out act, skipAoeCheck: true)) return true;
                }
                else
                {
                    // Stationary: Ruin III bevorzugen (höherer Cast-Value), Ruin IV als Fallback
                    if (RuinIiiPvE.EnoughLevel && RuinIiiPvE.CanUse(out act)) return true;
                    if (!RuinIiiPvE.Info.EnoughLevelAndQuest() && RuinIiPvE.EnoughLevel && RuinIiPvE.CanUse(out act)) return true;
                    if (!RuinIiPvE.Info.EnoughLevelAndQuest() && RuinPvE.CanUse(out act)) return true;
                    if (RuinIvPvE.CanUse(out act, skipAoeCheck: true)) return true;
                }
            }
            else
            {
                // Smart Ruin IV aus: normales Verhalten
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

    #region Dynamic Primal Selection

    /// <summary>
    /// Dynamische Egi-Auswahl basierend auf Bewegung und BossMod SpecialMode.
    /// Pyretic/NoMovement → Casts bevorzugen (Ifrit), Freezing → Instants (Titan).
    /// </summary>
    private bool DynamicPrimalSelection(out IAction? act)
    {
        act = null;

        // M11S Trophy Phase: Ifrit immer zuletzt (viel Bewegung in dieser Phase)
        if (M11SIfritLast && IsInM11STrophyPhaseCached())
        {
            LogDecision("Primal: M11S Trophy → Titan>Garuda>Ifrit");
            if (TitanTime(out act)) return true;
            if (GarudaTime(out act)) return true;
            if (IfritTime(out act)) return true;
            return false;
        }

        bool preferInstants = _simEnabled ? _simMoving : IsMoving;

        // Simulation SpecialMode Override
        string? mode = _simEnabled && _simSpecialModeIndex > 0
            ? SpecialModeNames[_simSpecialModeIndex]
            : GetCachedSpecialMode();

        // BossMod SpecialMode: überschreibt Bewegungserkennung (gecacht)
        if (mode != null)
        {
            switch (mode)
            {
                case SpecialModes.Pyretic:
                case SpecialModes.Freezing:
                    preferInstants = true;
                    break;
                case SpecialModes.NoMovement:
                    preferInstants = false;
                    break;
            }
        }

        LogDecision($"Primal: {(preferInstants ? "instants" : "casts")}, mode={mode ?? SpecialModes.Normal}");

        if (preferInstants)
        {
            // Instants: Titan first (all instant), then Garuda (mostly instant), Ifrit last
            if (TitanTime(out act)) return true;
            if (GarudaTime(out act)) return true;
            if (IfritTime(out act)) return true;
        }
        else
        {
            // Casts OK: Ifrit first (highest potency with casts), then Titan, then Garuda
            if (IfritTime(out act)) return true;
            if (TitanTime(out act)) return true;
            if (GarudaTime(out act)) return true;
        }

        return false;
    }

    #endregion

    #region Defense Helpers

    /// <summary>
    /// Prüft ob Schaden bevorsteht - nutzt BossMod IPC wenn verfügbar, sonst RSR-eigene Erkennung.
    /// Breite Erkennung für Aegis: alle Schadenstypen (Raidwide, Stack, Tankbuster).
    /// RSR-Fallback: bekannte AOE-Casts, VFX-Erkennung UND magische Casts (fängt nicht-gelistete Raidwides auf).
    /// </summary>
    private bool IsDamageImminent()
    {
        // Simulation Override: faked BossMod-Events
        if (_simEnabled && (_simRaidwideImminent || _simSharedImminent || _simTankbusterImminent))
        {
            LogDecision("IsDamageImminent=TRUE (SIM: " +
                (_simRaidwideImminent ? "Raidwide " : "") +
                (_simSharedImminent ? "Shared " : "") +
                (_simTankbusterImminent ? "TB" : "") + ")");
            return true;
        }

        // BossMod IPC: alle Schadenstypen prüfen
        if (UseBossModIPC && BossModHints_IPCSubscriber.IsEnabled)
        {
            try
            {
                if (BossModHints_IPCSubscriber.Hints_IsRaidwideImminent?.Invoke(BossModLookahead) == true)
                {
                    LogDecision("IsDamageImminent=TRUE (BossMod: Raidwide)");
                    return true;
                }
                if (BossModHints_IPCSubscriber.Hints_IsSharedImminent?.Invoke(BossModLookahead) == true)
                {
                    LogDecision("IsDamageImminent=TRUE (BossMod: Shared)");
                    return true;
                }
                if (BossModHints_IPCSubscriber.Hints_IsTankbusterImminent?.Invoke(BossModLookahead) == true)
                {
                    LogDecision("IsDamageImminent=TRUE (BossMod: TB)");
                    return true;
                }
            }
            catch (Exception ex)
            {
                if ((DateTime.Now - _lastIpcErrorLog).TotalSeconds > 10)
                {
                    _lastIpcErrorLog = DateTime.Now;
                    LogDecision($"IPC error in IsDamageImminent: {ex.Message}");
                }
            }
        }

        // RSR Fallback 1: bekannte AOE-Casts oder VFX-Erkennung
        if (DataCenter.IsHostileCastingAOE)
        {
            LogDecision("IsDamageImminent=TRUE (RSR: HostileCastingAOE)");
            return true;
        }

        // RSR Fallback 2: jeder magische Cast (fängt Raidwides auf die nicht in der AOE-Liste sind)
        if (DataCenter.IsMagicalDamageIncoming())
        {
            LogDecision("IsDamageImminent=TRUE (RSR: MagicalDamageIncoming)");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Prüft ob Addle sinnvoll eingesetzt werden kann:
    /// - Eingehender Schaden muss MAGICAL sein
    /// - Ziel hat noch kein Addle
    /// - Timing: Addle erst ~AddleLeadTime Sekunden vor Cast-Ende / Schadenseinschlag
    /// - BossMod IPC: nutzt AddleLeadTime für präzises Timing
    /// - RSR Fallback: prüft verbleibende Cast-Zeit
    /// </summary>
    private bool ShouldUseAddle()
    {
        // Simulation Override: faked magical raidwide
        if (_simEnabled && _simMagicalCast && _simRaidwideImminent)
        {
            // Im Sim-Modus Target-Check überspringen (kein echtes Target nötig)
            LogDecision("ShouldUseAddle=TRUE (SIM: Magical+Raidwide)");
            return true;
        }

        // Nur bei magischem Schaden (RSR-eigene Erkennung)
        if (!DataCenter.IsMagicalDamageIncoming())
        {
            return false;
        }

        // Ziel muss existieren und darf noch kein Addle haben
        if (HostileTarget == null || HostileTarget.HasStatus(false, StatusID.Addle))
        {
            return false;
        }

        // BossMod IPC: nutze AddleLeadTime statt BossModLookahead für präzises Timing
        if (UseBossModIPC && BossModHints_IPCSubscriber.IsEnabled)
        {
            try
            {
                bool raidwide = BossModHints_IPCSubscriber.Hints_IsRaidwideImminent?.Invoke(AddleLeadTime) == true;
                if (raidwide)
                {
                    LogDecision("ShouldUseAddle=TRUE (BossMod: Raidwide within AddleLeadTime)");
                    return true;
                }

                bool shared = BossModHints_IPCSubscriber.Hints_IsSharedImminent?.Invoke(AddleLeadTime) == true;
                if (shared && !AddlePvE.Cooldown.IsCoolingDown)
                {
                    LogDecision("ShouldUseAddle=TRUE (BossMod: Shared, CD free)");
                    return true;
                }
            }
            catch (Exception ex)
            {
                if ((DateTime.Now - _lastIpcErrorLog).TotalSeconds > 10)
                {
                    _lastIpcErrorLog = DateTime.Now;
                    LogDecision($"IPC error in ShouldUseAddle: {ex.Message}");
                }
            }
        }

        // RSR Fallback: Timing-Check über verbleibende Cast-Zeit
        if (!IsHostileCastRemainingWithin(AddleLeadTime))
            return false;

        bool isRaidwideCast = IsAnyHostileCastingKnownRaidwide();
        if (isRaidwideCast)
        {
            LogDecision("ShouldUseAddle=TRUE (RSR: Known Raidwide Cast)");
            return true;
        }

        if (DataCenter.IsHostileCastingAOE && !AddlePvE.Cooldown.IsCoolingDown)
        {
            LogDecision("ShouldUseAddle=TRUE (RSR: AOE Cast, CD free)");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Prüft ob ein feindlicher Cast innerhalb der nächsten N Sekunden endet.
    /// Wird für Addle-Timing genutzt: Addle erst kurz vor Cast-Ende anwenden.
    /// </summary>
    private static bool IsHostileCastRemainingWithin(float seconds)
    {
        if (DataCenter.AllHostileTargets == null) return false;

        for (int i = 0, n = DataCenter.AllHostileTargets.Count; i < n; i++)
        {
            var hostile = DataCenter.AllHostileTargets[i];
            if (hostile == null || !hostile.IsCasting || hostile.TotalCastTime <= 0) continue;

            float remaining = hostile.TotalCastTime - hostile.CurrentCastTime;
            if (remaining > 0 && remaining <= seconds)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Prüft ob ein Feind einen bekannten Raidwide aus der HostileCastingArea-Liste castet.
    /// </summary>
    private static bool IsAnyHostileCastingKnownRaidwide()
    {
        if (DataCenter.AllHostileTargets == null)
            return false;

        for (int i = 0, n = DataCenter.AllHostileTargets.Count; i < n; i++)
        {
            var hostile = DataCenter.AllHostileTargets[i];
            if (hostile == null || !hostile.IsCasting)
                continue;

            if (DataCenter.IsHostileCastingArea(hostile))
                return true;
        }
        return false;
    }

    #endregion

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

    #region Diagnostics & Simulation

    public override void DisplayRotationStatus()
    {
        if (!ShowDiagnostics)
        {
            ImGui.TextWrapped("Enable 'Diagnostics' in rotation config to show real-time decision state.");
            return;
        }

        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "=== SMN Dynamic Diagnostics ===");
        ImGui.Separator();

        // ==================== SIMULATION CONTROLS ====================
        DrawSimulationControls();

        ImGui.Separator();

        // ==================== REACTION PREVIEW ====================
        DrawReactionPreview();

        ImGui.Separator();

        // ==================== LIVE STATE ====================
        DrawLiveState();

        ImGui.Separator();

        // ==================== CAST ID LOOKUP ====================
        DrawCastIdLookup();

        ImGui.Separator();

        // ==================== DECISION LOG ====================
        DrawDecisionLog();
    }

    // ----- Simulation Controls -----
    private void DrawSimulationControls()
    {
        // Header mit Warn-Banner wenn aktiv
        if (_simEnabled)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.2f, 0.2f, 1f),
                "!! SIMULATION ACTIVE - Overrides beeinflussen Rotation !!");
        }

        bool simEnabled = _simEnabled;
        if (ImGui.Checkbox("Simulation aktivieren", ref simEnabled))
        {
            _simEnabled = simEnabled;
            if (!simEnabled)
            {
                // Reset aller Overrides beim Deaktivieren
                _simRaidwideImminent = false;
                _simSharedImminent = false;
                _simTankbusterImminent = false;
                _simMagicalCast = false;
                _simMoving = false;
                _simSpecialModeIndex = 0;
                LogDecision("Simulation DEAKTIVIERT - alle Overrides reset");
            }
            else
            {
                LogDecision("Simulation AKTIVIERT");
            }
        }

        if (!_simEnabled) return;

        ImGui.Indent(10);

        ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1f, 1f), "Damage Events:");
        bool rw = _simRaidwideImminent;
        if (ImGui.Checkbox("Raidwide Imminent", ref rw)) _simRaidwideImminent = rw;
        ImGui.SameLine();
        bool sh = _simSharedImminent;
        if (ImGui.Checkbox("Shared/Stack", ref sh)) _simSharedImminent = sh;
        ImGui.SameLine();
        bool tb = _simTankbusterImminent;
        if (ImGui.Checkbox("Tankbuster", ref tb)) _simTankbusterImminent = tb;

        bool mag = _simMagicalCast;
        if (ImGui.Checkbox("Magical Damage (fuer Addle)", ref mag)) _simMagicalCast = mag;

        ImGui.Spacing();
        ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1f, 1f), "Movement & Mechanics:");
        bool mov = _simMoving;
        if (ImGui.Checkbox("IsMoving Override", ref mov)) _simMoving = mov;
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

        // IsDamageImminent
        bool damageImm = IsDamageImminent();
        bool shouldAddle = false;
        try { shouldAddle = ShouldUseAddle(); } catch { }

        ColoredBool("  IsDamageImminent", damageImm);
        ColoredBool("  ShouldUseAddle", shouldAddle);

        // Addle Status
        bool addleOnCD = AddlePvE.Cooldown.IsCoolingDown;
        bool targetHasAddle = HostileTarget?.HasStatus(false, StatusID.Addle) ?? false;
        ImGui.Text($"  Addle: {(addleOnCD ? $"CD ({AddlePvE.Cooldown.RecastTimeRemainOneCharge:F1}s)" : "Ready")} | Target Addle: {(targetHasAddle ? "YES" : "No")}");

        // Aegis Status
        bool aegisReady = !RadiantAegisPvE.Cooldown.IsCoolingDown;
        ColoredBool("  Aegis wuerde feuern", damageImm && SmartAegis && aegisReady);
        ImGui.Text($"  Aegis: {(aegisReady ? "Ready" : $"CD ({RadiantAegisPvE.Cooldown.RecastTimeRemainOneCharge:F1}s)")}");

        // Decision Chain Erklärung
        ImGui.Spacing();
        ImGui.TextColored(new System.Numerics.Vector4(0.8f, 0.8f, 0.8f, 1f), "  Decision Chain:");
        if (_simEnabled)
        {
            string source = _simEnabled ? "SIM" : "LIVE";
            if (_simRaidwideImminent || _simSharedImminent || _simTankbusterImminent)
                ImGui.Text($"    -> IsDamageImminent: {source} Override aktiv");
            if (_simMagicalCast && _simRaidwideImminent)
                ImGui.Text($"    -> ShouldUseAddle: {source} Magical+Raidwide");
        }
        else
        {
            bool bossModOk = UseBossModIPC && BossModHints_IPCSubscriber.IsEnabled;
            if (bossModOk)
                ImGui.Text("    -> Quelle: BossMod IPC");
            else if (DataCenter.IsHostileCastingAOE)
                ImGui.Text("    -> Quelle: RSR HostileCastingAOE");
            else if (DataCenter.IsMagicalDamageIncoming())
                ImGui.Text("    -> Quelle: RSR MagicalDamageIncoming");
            else
                ImGui.TextDisabled("    -> Kein Damage erkannt");
        }

        // Primal Selection Preview
        ImGui.Spacing();
        bool moving = _simEnabled ? _simMoving : IsMoving;
        string? activeMode = _simEnabled && _simSpecialModeIndex > 0
            ? SpecialModeNames[_simSpecialModeIndex]
            : GetCachedSpecialMode();

        bool preferInstants = moving;
        if (activeMode != null)
        {
            preferInstants = activeMode switch
            {
                SpecialModes.Pyretic => true,
                SpecialModes.NoMovement => false,
                SpecialModes.Freezing => true,
                _ => preferInstants
            };
        }

        string primalOrder = preferInstants
            ? "Titan > Garuda > Ifrit (Instants)"
            : "Ifrit > Titan > Garuda (Casts)";

        if (M11SIfritLast && IsInM11STrophyPhaseCached())
            primalOrder = "Titan > Garuda > Ifrit (M11S Trophy)";

        ImGui.Text($"  Primal Order: {primalOrder}");
        ImGui.Text($"  Moving: {moving} | SpecialMode: {activeMode ?? SpecialModes.Normal}");
    }

    // ----- Live State -----
    private void DrawLiveState()
    {
        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "[Live State]");

        // Hostile Casts
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
                string atkType = GetAttackTypeName(castId);
                string actionName = GetActionName(castId);
                var castColor = inList
                    ? new System.Numerics.Vector4(0.2f, 1f, 0.2f, 1f)
                    : new System.Numerics.Vector4(1f, 1f, 1f, 1f);
                ImGui.TextColored(castColor, $"  {castId} {actionName} | {remaining:F1}s | {atkType} | {(inList ? "IN LIST" : "not listed")}");
            }
            if (!anyCast) ImGui.TextDisabled("  (keine aktiven Casts)");
        }
        else
        {
            ImGui.TextDisabled("  (keine Targets)");
        }

        ImGui.Spacing();

        // BossMod IPC
        ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1f, 1f), "BossMod IPC:");
        bool bossModOk = UseBossModIPC && BossModHints_IPCSubscriber.IsEnabled;
        if (bossModOk)
        {
            try
            {
                bool rw = BossModHints_IPCSubscriber.Hints_IsRaidwideImminent?.Invoke(BossModLookahead) == true;
                bool rwLead = BossModHints_IPCSubscriber.Hints_IsRaidwideImminent?.Invoke(AddleLeadTime) == true;
                bool sh = BossModHints_IPCSubscriber.Hints_IsSharedImminent?.Invoke(BossModLookahead) == true;
                bool tb = BossModHints_IPCSubscriber.Hints_IsTankbusterImminent?.Invoke(BossModLookahead) == true;
                ColoredBool($"  Raidwide ({BossModLookahead:F0}s)", rw);
                ImGui.SameLine();
                ColoredBool($"| Addle-Fenster ({AddleLeadTime:F1}s)", rwLead);
                ColoredBool($"  Shared ({BossModLookahead:F0}s)", sh);
                ImGui.SameLine();
                ColoredBool($"| TB ({BossModLookahead:F0}s)", tb);
                ImGui.Text($"  SpecialMode: {GetCachedSpecialMode() ?? "Normal"}");
            }
            catch { ImGui.TextDisabled("  (IPC Fehler)"); }
        }
        else
        {
            ImGui.TextDisabled($"  Nicht verbunden (UseBossModIPC={UseBossModIPC})");
        }

        ImGui.Spacing();

        // Fight State
        ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1f, 1f), "Fight:");
        ImGui.Text($"  Territory: {DataCenter.TerritoryID} | InCombat: {InCombat}");
        ImGui.Text($"  Phase: {(InBahamut ? "Bahamut" : InPhoenix ? "Phoenix" : InSolarBahamut ? "SolarBahamut" : "Primal")} | SummonTime: {SummonTime:F1}s");
        if (HasFurtherRuin) ImGui.Text("  FurtherRuin: ACTIVE");
        if (DataCenter.IsInM11S)
        {
            bool trophy = IsInM11STrophyPhaseCached();
            ColoredBool("  M11S TrophyPhase", trophy);
        }
        float grotRemaining = DirectedGrotesquerieRemaining;
        if (grotRemaining > 0)
        {
            ImGui.Text($"  M12S Grotesquerie: {grotRemaining:F1}s");
            ColoredBool("  ShouldPause", ShouldPauseForDirection());
        }
    }

    // ----- Cast ID Lookup & Management -----
    private static void DrawCastIdLookup()
    {
        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "[Cast ID Lookup]");

        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("Cast ID", ref _lookupCastId, 0, 0);
        ImGui.SameLine();

        if (ImGui.Button("Lookup"))
        {
            uint id = (uint)_lookupCastId;
            if (id == 0)
            {
                _lookupResult = "Ungueltige ID (0)";
            }
            else
            {
                string name = GetActionName(id);
                string atkType = GetAttackTypeName(id);
                bool inList = OtherConfiguration.HostileCastingArea.Contains(id);
                _lookupResult = $"{id}: {name} | {atkType} | {(inList ? "IN HostileCastingArea" : "NICHT in Liste")}";
            }
        }

        if (_lookupCastId > 0)
        {
            uint lookupId = (uint)_lookupCastId;
            bool inList = OtherConfiguration.HostileCastingArea.Contains(lookupId);

            ImGui.SameLine();
            if (!inList)
            {
                if (ImGui.Button("+ Add to List"))
                {
                    OtherConfiguration.HostileCastingArea.Add(lookupId);
                    OtherConfiguration.Save();
                    string name = GetActionName(lookupId);
                    _lookupResult = $"ADDED: {lookupId} ({name})";
                    LogDecision($"HostileCastingArea + {lookupId} ({name})");
                }
            }
            else
            {
                if (ImGui.Button("- Remove from List"))
                {
                    OtherConfiguration.HostileCastingArea.Remove(lookupId);
                    OtherConfiguration.Save();
                    string name = GetActionName(lookupId);
                    _lookupResult = $"REMOVED: {lookupId} ({name})";
                    LogDecision($"HostileCastingArea - {lookupId} ({name})");
                }
            }
        }

        if (_lookupResult != null)
        {
            ImGui.Text($"  {_lookupResult}");
        }

        ImGui.Text($"  HostileCastingArea Eintraege: {OtherConfiguration.HostileCastingArea.Count}");
    }

    // ----- Decision Log -----
    private static void DrawDecisionLog()
    {
        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "[Decision Log]");

        if (_decisionLogCount == 0)
        {
            ImGui.TextDisabled("  (noch keine Eintraege - Decisions werden bei Erkennung geloggt)");
            return;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Clear"))
        {
            _decisionLogCount = 0;
            _decisionLogIndex = 0;
        }

        // Zeige Einträge rückwärts (neuester zuerst)
        int shown = 0;
        for (int i = 0; i < _decisionLogCount && shown < 15; i++)
        {
            int idx = (_decisionLogIndex - 1 - i + _decisionLog.Length) % _decisionLog.Length;
            if (_decisionLog[idx] != null)
            {
                ImGui.Text($"  {_decisionLog[idx]}");
                shown++;
            }
        }
    }

    // ----- Helper Methods -----

    private static void ColoredBool(string label, bool value)
    {
        var color = value
            ? new System.Numerics.Vector4(0.2f, 1f, 0.2f, 1f)  // grün
            : new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 1f); // grau
        ImGui.TextColored(color, $"{label}: {(value ? "TRUE" : "FALSE")}");
    }

    private static string GetActionName(uint actionId)
    {
        if (_actionNameCache.TryGetValue(actionId, out var cached))
            return cached;

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

    private static string GetAttackTypeName(uint actionId)
    {
        if (_attackTypeCache.TryGetValue(actionId, out var cached))
            return cached;

        try
        {
            var sheet = Service.GetSheet<Lumina.Excel.Sheets.Action>();
            if (sheet == null) return "?";
            var action = sheet.GetRow(actionId);
            if (action.RowId == 0) return "?";
            var result = action.AttackType.RowId switch
            {
                0 => "None(0)",
                1 => "Slash(1)",
                2 => "Pierce(2)",
                3 => "Blunt(3)",
                5 => "Magic(5)",
                6 => "Dark(6)",
                7 => "Phys(7)",
                _ => $"Unk({action.AttackType.RowId})"
            };
            _attackTypeCache[actionId] = result;
            return result;
        }
        catch { return "?"; }
    }

    #endregion

    #region Heal Override

    public override bool CanHealSingleSpell
    {
        get
        {
            int aliveHealerCount = 0;
            IEnumerable<IBattleChara> healers = PartyMembers.GetJobCategory(JobRole.Healer);
            foreach (IBattleChara h in healers)
            {
                if (!h.IsDead)
                    aliveHealerCount++;
            }
            return base.CanHealSingleSpell && (GCDHeal || aliveHealerCount == 0);
        }
    }

    #endregion
}