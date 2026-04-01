using System.ComponentModel;
using ECommons.DalamudServices;
using RotationSolver.Basic.Configuration;
using RotationSolver.IPC;

namespace RotationSolver.ExtraRotations.Healer;

[Rotation("SGE Dynamic", CombatType.PvE, GameVersion = "7.45")]
[SourceCode(Path = "main/ExtraRotations/Healer/SGE_Dynamic.cs")]

public sealed class SGE_Dynamic : SageRotation
{
    #region Config Options

    [RotationConfig(CombatType.PvE, Name = "Use BossMod IPC for raidwide/tankbuster/stack detection (requires BossModReborn)")]
    public bool UseBossModIPC { get; set; } = true;

    [Range(1, 10, ConfigUnitType.Seconds)]
    [RotationConfig(CombatType.PvE, Name = "BossMod lookahead (seconds) for general damage prediction")]
    public float BossModLookahead { get; set; } = 5f;

    [Range(1, 10, ConfigUnitType.Seconds)]
    [RotationConfig(CombatType.PvE, Name = "Raidwide lead time: seconds before raidwide to apply group shields")]
    public float RaidwideLeadTime { get; set; } = 4.0f;

    [Range(1, 10, ConfigUnitType.Seconds)]
    [RotationConfig(CombatType.PvE, Name = "Tankbuster lead time: seconds before TB to apply tank shields")]
    public float TankbusterLeadTime { get; set; } = 3.5f;

    [RotationConfig(CombatType.PvE, Name = "BossMod SpecialMode: Adapt rotation to Pyretic/NoMovement/Freezing")]
    public bool UseSpecialMode { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Proactive raidwide shields (Kerachole/Panhaima/Holos before damage)")]
    public bool SmartShieldsRaidwide { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Proactive tankbuster shields (Haima/Taurochole before buster)")]
    public bool SmartShieldsTankbuster { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Proactive stack mitigation (group shields for stack mechanics)")]
    public bool SmartShieldsStack { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Zoe Emergency: Zoe + heal when any party member HP critically low")]
    public bool ZoeEmergencyEnabled { get; set; } = true;

    [Range(0.1f, 0.8f, ConfigUnitType.Percent)]
    [RotationConfig(CombatType.PvE, Name = "Zoe Emergency HP threshold")]
    public float ZoeEmergencyThreshold { get; set; } = 0.30f;

    [RotationConfig(CombatType.PvE, Name = "Proactive Zoe: Amplify heals before raidwides/tankbusters and in boss fights")]
    public bool ZoeProactive { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Proactive Boss Healing: Cycle HoTs, shields & mitigation during boss fights")]
    public bool ProactiveBossHealing { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Smart Kardia: Auto-apply to tank, re-apply after death")]
    public bool SmartKardia { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Smart Physis: Save Physis II for raidwides instead of using on cooldown")]
    public bool SmartPhysis { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Smart Addersgall: Prevent overcap by dumping Druochole")]
    public bool SmartAddersgall { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "DPS Optimization: Phlegma charge management, Toxikon on movement")]
    public bool DpsOptimization { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use Swiftcast on Egeiro (Raise)")]
    public bool UseSwiftcastOnRaise { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Prepull: Apply Eukrasian Dosis during countdown")]
    public bool UseEukrasianDosisInCountdown { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Diagnostics: Show real-time decision state in Rotation Status panel")]
    public bool ShowDiagnostics { get; set; } = false;

    #endregion

    #region Helper Properties & Caching

    private static class SpecialModes
    {
        public const string Normal = "Normal";
        public const string Pyretic = "Pyretic";
        public const string NoMovement = "NoMovement";
        public const string Freezing = "Freezing";
    }

    // Per-frame IPC caches (invalidated in GeneralGCD)
    private bool _raidwideImminentCached;
    private bool _raidwideImminentValid;
    private bool _tankbusterImminentCached;
    private bool _tankbusterImminentValid;
    private bool _stackImminentCached;
    private bool _stackImminentValid;
    private bool _preyImminentCached;
    private bool _preyImminentValid;
    private bool _markerImminentCached;
    private bool _markerImminentValid;
    private string? _specialModeCache;
    private bool _specialModeCacheValid;

    // IPC error throttle
    private DateTime _lastIpcErrorLog = DateTime.MinValue;

    // Simulation state
    private static bool _simEnabled;
    private static bool _simRaidwideImminent;
    private static bool _simTankbusterImminent;
    private static bool _simSharedImminent;
    private static bool _simPreyImminent;
    private static bool _simMarkerImminent;
    private static int _simSpecialModeIndex;

    // Decision Log (Ring-Buffer, 40 entries)
    private static readonly string[] _decisionLog = new string[40];
    private static int _decisionLogIndex;
    private static int _decisionLogCount;

    private static readonly string[] SpecialModeNames = [SpecialModes.Normal, SpecialModes.Pyretic, SpecialModes.NoMovement, SpecialModes.Freezing];

    // Lumina lookup caches
    private static readonly Dictionary<uint, string> _actionNameCache = new();

    private static void LogDecision(string message)
    {
        _decisionLog[_decisionLogIndex] = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _decisionLogIndex = (_decisionLogIndex + 1) % _decisionLog.Length;
        if (_decisionLogCount < _decisionLog.Length) _decisionLogCount++;
    }

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
            ThrottledIpcLog($"IPC error in GetCachedSpecialMode: {ex.Message}");
        }
        return _specialModeCache;
    }

    private void ThrottledIpcLog(string message)
    {
        if ((DateTime.Now - _lastIpcErrorLog).TotalSeconds > 10)
        {
            _lastIpcErrorLog = DateTime.Now;
            LogDecision(message);
        }
    }

    /// <summary>Checks if any party member is below the given HP ratio.</summary>
    private bool IsAnyPartyMemberBelow(float threshold)
    {
        foreach (var m in PartyMembers)
        {
            if (!m.IsDead && m.GetHealthRatio() < threshold)
                return true;
        }
        return false;
    }

    /// <summary>Returns the lowest HP ratio among living party members.</summary>
    private float GetLowestPartyMemberHp()
    {
        float lowest = 1f;
        foreach (var m in PartyMembers)
        {
            if (!m.IsDead)
            {
                float hp = m.GetHealthRatio();
                if (hp < lowest) lowest = hp;
            }
        }
        return lowest;
    }

    /// <summary>Returns true if the current hostile target is a boss (icon-based detection).</summary>
    private bool IsBossFight()
    {
        var target = HostileTarget;
        return target != null && target.IsBossFromIcon();
    }

    /// <summary>Panic mode: tank HP dangerously low during trash pulls (big dungeon pulls).</summary>
    private bool IsPanicMode()
    {
        if (!InCombat || IsBossFight()) return false;
        return GetLowestPartyMemberHp() < 0.40f;
    }

    /// <summary>Sustained pull: tank taking continuous damage in trash (wall-to-wall pulls).
    /// Triggers proactive mitigation cycling between panic (40%) and normal thresholds.</summary>
    private bool IsSustainedPull()
    {
        if (!InCombat || IsBossFight()) return false;
        return GetLowestPartyMemberHp() < 0.75f;
    }

    #endregion

    #region Defense Helpers (BossMod IPC + RSR Fallback)

    private bool IsRaidwideImminent()
    {
        if (_raidwideImminentValid) return _raidwideImminentCached;
        _raidwideImminentValid = true;
        _raidwideImminentCached = ComputeRaidwideImminent();
        return _raidwideImminentCached;
    }

    private bool ComputeRaidwideImminent()
    {
        if (_simEnabled && _simRaidwideImminent)
        {
            LogDecision("Raidwide=TRUE (SIM)");
            return true;
        }

        if (UseBossModIPC && BossModHints_IPCSubscriber.IsEnabled)
        {
            try
            {
                if (BossModHints_IPCSubscriber.Hints_IsRaidwideImminent?.Invoke(RaidwideLeadTime) == true)
                {
                    LogDecision("Raidwide=TRUE (BossMod)");
                    return true;
                }
            }
            catch (Exception ex) { ThrottledIpcLog($"IPC error Raidwide: {ex.Message}"); }
        }

        if (DataCenter.IsHostileCastingAOE)
        {
            LogDecision("Raidwide=TRUE (RSR: AOE)");
            return true;
        }

        return false;
    }

    private bool IsTankbusterImminent()
    {
        if (_tankbusterImminentValid) return _tankbusterImminentCached;
        _tankbusterImminentValid = true;
        _tankbusterImminentCached = ComputeTankbusterImminent();
        return _tankbusterImminentCached;
    }

    private bool ComputeTankbusterImminent()
    {
        if (_simEnabled && _simTankbusterImminent)
        {
            LogDecision("Tankbuster=TRUE (SIM)");
            return true;
        }

        if (UseBossModIPC && BossModHints_IPCSubscriber.IsEnabled)
        {
            try
            {
                if (BossModHints_IPCSubscriber.Hints_IsTankbusterImminent?.Invoke(TankbusterLeadTime) == true)
                {
                    LogDecision("Tankbuster=TRUE (BossMod)");
                    return true;
                }
            }
            catch (Exception ex) { ThrottledIpcLog($"IPC error TB: {ex.Message}"); }
        }

        if (DataCenter.IsHostileCastingToTank)
        {
            LogDecision("Tankbuster=TRUE (RSR: CastToTank)");
            return true;
        }

        return false;
    }

    private bool IsStackImminent()
    {
        if (_stackImminentValid) return _stackImminentCached;
        _stackImminentValid = true;
        _stackImminentCached = ComputeStackImminent();
        return _stackImminentCached;
    }

    private bool ComputeStackImminent()
    {
        if (_simEnabled && _simSharedImminent)
        {
            LogDecision("Stack=TRUE (SIM)");
            return true;
        }

        if (UseBossModIPC && BossModHints_IPCSubscriber.IsEnabled)
        {
            try
            {
                if (BossModHints_IPCSubscriber.Hints_IsSharedImminent?.Invoke(BossModLookahead) == true)
                {
                    LogDecision("Stack=TRUE (BossMod)");
                    return true;
                }
            }
            catch (Exception ex) { ThrottledIpcLog($"IPC error Stack: {ex.Message}"); }
        }

        if (DataCenter.IsHostileCastingStack)
        {
            LogDecision("Stack=TRUE (RSR: CastStack)");
            return true;
        }

        return false;
    }

    private bool IsPreyImminent()
    {
        if (_preyImminentValid) return _preyImminentCached;
        _preyImminentValid = true;
        _preyImminentCached = ComputePreyImminent();
        return _preyImminentCached;
    }

    private bool ComputePreyImminent()
    {
        if (_simEnabled && _simPreyImminent)
        {
            LogDecision("Prey=TRUE (SIM)");
            return true;
        }

        if (DataCenter.IsHostileCastingPrey)
        {
            LogDecision("Prey=TRUE (RSR: CastPrey)");
            return true;
        }

        return false;
    }

    private bool IsMarkerImminent()
    {
        if (_markerImminentValid) return _markerImminentCached;
        _markerImminentValid = true;
        _markerImminentCached = ComputeMarkerImminent();
        return _markerImminentCached;
    }

    private bool ComputeMarkerImminent()
    {
        if (_simEnabled && _simMarkerImminent)
        {
            LogDecision("Marker=TRUE (SIM)");
            return true;
        }

        if (DataCenter.IsHostileCastingMarker)
        {
            LogDecision("Marker=TRUE (RSR: CastMarker)");
            return true;
        }

        return false;
    }

    /// <summary>Any group-wide damage imminent (raidwide, stack, spread marker).</summary>
    private bool IsGroupDamageImminent() => IsRaidwideImminent() || IsStackImminent() || IsMarkerImminent();

    private bool IsDamageImminent() => IsGroupDamageImminent() || IsTankbusterImminent() || IsPreyImminent();

    #endregion

    #region Countdown Logic

    protected override IAction? CountDownAction(float remainTime)
    {
        if (UseEukrasianDosisInCountdown)
        {
            // Opener: Toxikon II at ~1.5s (instant, long application delay → DoT lands faster)
            if (HasEukrasia && remainTime <= 1.8f + CountDownAhead)
            {
                if (EukrasianDosisIiiPvE.CanUse(out var act)) return act;
                if (EukrasianDosisIiPvE.CanUse(out var act2)) return act2;
                if (EukrasianDosisPvE.CanUse(out var act3)) return act3;
            }
            // Apply Eukrasia ~3s before pull
            if (remainTime <= 3f + CountDownAhead && !HasEukrasia)
            {
                if (EukrasiaPvE.CanUse(out var act)) return act;
            }
        }
        return base.CountDownAction(remainTime);
    }

    #endregion

    #region oGCD Logic

    protected override bool EmergencyAbility(IAction nextGCD, out IAction? act)
    {
        act = null;

        // Zoe: Proactive + Emergency usage
        if (!StatusHelper.PlayerHasStatus(true, StatusID.Zoe) && ZoePvE.CanUse(out act))
        {
            // Priority 1: Zoe + Pneuma — always great (no DPS loss, 900p AoE heal)
            if (nextGCD.IsTheSameTo(false, PneumaPvE))
            {
                LogDecision("Zoe: amplifying Pneuma");
                return true;
            }

            // Priority 2: Proactive Zoe before group damage — amplify AoE heal/shield GCDs
            if (ZoeProactive && IsGroupDamageImminent()
                && nextGCD.IsTheSameTo(false, EukrasianPrognosisIiPvE, EukrasianPrognosisPvE, PrognosisPvE))
            {
                LogDecision("Zoe: amplifying AoE heal for incoming group damage");
                return true;
            }

            // Priority 3: Proactive Zoe before tankbuster — amplify ST heal/shield GCDs
            if (ZoeProactive && IsTankbusterImminent()
                && nextGCD.IsTheSameTo(false, EukrasianDiagnosisPvE, DiagnosisPvE))
            {
                LogDecision("Zoe: amplifying ST heal for tankbuster");
                return true;
            }

            // Priority 4: Boss fight — amplify any significant heal GCD when party not topped
            if (ZoeProactive && IsBossFight() && PartyMembersAverHP < 0.85f
                && nextGCD.IsTheSameTo(false, PneumaPvE, PrognosisPvE,
                    EukrasianPrognosisIiPvE, EukrasianPrognosisPvE,
                    EukrasianDiagnosisPvE, DiagnosisPvE))
            {
                LogDecision($"Zoe: proactive boss fight heal (party HP={PartyMembersAverHP:P0})");
                return true;
            }

            // Priority 5: Emergency — any heal GCD when HP critically low
            if (ZoeEmergencyEnabled)
            {
                float lowestHp = GetLowestPartyMemberHp();
                if (lowestHp < ZoeEmergencyThreshold)
                {
                    LogDecision($"Zoe Emergency: lowest HP={lowestHp:P0}");
                    return true;
                }
            }

            act = null; // Reset if no Zoe condition was met
        }

        // Swiftcast on Raise
        if (UseSwiftcastOnRaise && nextGCD.IsTheSameTo(false, EgeiroPvE))
        {
            if (SwiftcastPvE.CanUse(out act))
                return true;
        }

        return base.EmergencyAbility(nextGCD, out act);
    }

    protected override bool AttackAbility(IAction nextGCD, out IAction? act)
    {
        act = null;

        // Psyche: DPS oGCD, use on cooldown
        if (PsychePvE.CanUse(out act))
            return true;

        // Smart Addersgall: dump before overcap
        // At 3 = timer stopped, wasting generation. At 2 with timer running low = about to overcap
        if (SmartAddersgall && (Addersgall == 3 || (Addersgall >= 2 && AddersgallTime < 8000f)))
        {
            if (DruocholePvE.CanUse(out act))
            {
                LogDecision($"Addersgall dump: {Addersgall}/3, timer={AddersgallTime / 1000f:F1}s");
                return true;
            }
        }

        return base.AttackAbility(nextGCD, out act);
    }

    protected override bool GeneralAbility(IAction nextGCD, out IAction? act)
    {
        act = null;

        // Smart Kardia: auto-apply when missing (handles initial + death recovery)
        if (SmartKardia && KardiaPvE.CanUse(out act))
        {
            LogDecision("Kardia: applying to tank");
            return true;
        }

        // Rhizomata: generate Addersgall when low (≤1), not just empty
        if (Addersgall <= 1 && RhizomataPvE.CanUse(out act))
        {
            LogDecision($"Rhizomata: Addersgall={Addersgall}");
            return true;
        }

        // Soteria: boost Kardia heals (+50% for 4 procs, free)
        // Boss: always / Sustained pull: always / Normal: when someone hurt
        if (InCombat && SoteriaPvE.CanUse(out act))
        {
            bool bossFight = IsBossFight();
            bool panic = IsPanicMode();
            bool sustained = IsSustainedPull();
            if ((bossFight && ProactiveBossHealing) || panic || sustained || IsAnyPartyMemberBelow(0.88f))
            {
                LogDecision(panic ? "Soteria: PANIC Kardia boost"
                    : sustained ? "Soteria: sustained pull Kardia boost"
                    : bossFight ? "Soteria: proactive Kardia boost"
                    : "Soteria: party member hurt");
                return true;
            }
        }

        // Krasis: +20% healing received on tank — amplifies all subsequent heals
        // Use in: panic, sustained pulls, TB, boss with damage
        if (InCombat && KrasisPvE.CanUse(out act))
        {
            bool panic = IsPanicMode();
            bool sustained = IsSustainedPull();
            if (panic || sustained || IsTankbusterImminent()
                || (ProactiveBossHealing && IsBossFight() && IsAnyPartyMemberBelow(0.85f)))
            {
                LogDecision(panic ? "Krasis: PANIC amplify"
                    : sustained ? "Krasis: sustained pull amplify"
                    : "Krasis: amplify incoming heals on tank");
                return true;
            }
        }

        // Philosophia: sustained healing via damage GCDs
        // Boss: proactive / Sustained/Panic pulls: always / Otherwise: low HP
        if (InCombat && PhilosophiaPvE.CanUse(out act))
        {
            bool panic = IsPanicMode();
            bool sustained = IsSustainedPull();
            if (IsGroupDamageImminent() || panic || sustained || PartyMembersAverHP < 0.75f
                || (ProactiveBossHealing && IsBossFight() && PartyMembersAverHP < 0.90f))
            {
                LogDecision(panic ? $"Philosophia: PANIC pull (HP={PartyMembersAverHP:P0})"
                    : sustained ? $"Philosophia: sustained pull regen (HP={PartyMembersAverHP:P0})"
                    : $"Philosophia: proactive regen (HP={PartyMembersAverHP:P0})");
                return true;
            }
        }

        // Proactive HoTs/Shields/Mitigation cycling
        if (ProactiveBossHealing && InCombat)
        {
            bool bossFight = IsBossFight();
            bool panic = IsPanicMode();
            bool sustained = IsSustainedPull();

            // Physis II: free HoT + 10% healing received buff (60s CD)
            // Boss: any chip / Sustained pull: always / Normal non-boss: moderate damage
            if (PhysisIiPvE.CanUse(out act))
            {
                if (panic || sustained || (bossFight && PartyMembersAverHP < 0.95f)
                    || (!bossFight && PartyMembersAverHP < 0.88f))
                {
                    LogDecision(panic ? "Physis II: PANIC HoT"
                        : sustained ? "Physis II: sustained pull HoT"
                        : $"Physis II: proactive HoT (HP={PartyMembersAverHP:P0})");
                    return true;
                }
            }
            if (!PhysisIiPvE.EnoughLevel && PhysisPvE.CanUse(out act))
            {
                if (panic || sustained || (bossFight && PartyMembersAverHP < 0.95f)
                    || (!bossFight && PartyMembersAverHP < 0.88f))
                {
                    LogDecision(panic ? "Physis: PANIC HoT" : "Physis: proactive HoT");
                    return true;
                }
            }

            // Kerachole: 10% mit + regen (30s CD, 1 Addersgall) — keep rolling every 30s in pulls
            // Boss: Addersgall >= 1 when taking damage / Sustained: >= 1 / Normal: >= 2
            if (KeracholePvE.CanUse(out act))
            {
                if (panic
                    || ((bossFight || sustained) && Addersgall >= 1 && PartyMembersAverHP < 0.92f)
                    || (Addersgall >= 2 && PartyMembersAverHP < 0.92f))
                {
                    LogDecision(panic ? "Kerachole: PANIC mit + regen"
                        : sustained ? $"Kerachole: sustained pull cycling (Addersgall={Addersgall})"
                        : $"Kerachole: proactive (Addersgall={Addersgall}, HP={PartyMembersAverHP:P0})");
                    return true;
                }
            }

            // Haima on tank: proactive in sustained pulls (1800p shield value, 120s CD)
            // Only in sustained/panic — don't waste on light damage
            if ((panic || sustained) && HaimaPvE.CanUse(out act))
            {
                LogDecision(panic ? "Haima: PANIC tank shield" : "Haima: sustained pull tank shield");
                return true;
            }
        }

        return base.GeneralAbility(nextGCD, out act);
    }

    #endregion

    #region Heal & Defense Abilities

    [RotationDesc(ActionID.KeracholePvE, ActionID.PhysisIiPvE, ActionID.PanhaimaPvE, ActionID.HolosPvE)]
    protected override bool DefenseAreaAbility(IAction nextGCD, out IAction? act)
    {
        act = null;

        bool raidwide = SmartShieldsRaidwide && IsRaidwideImminent();
        bool stack = SmartShieldsStack && IsStackImminent();
        bool prey = SmartShieldsStack && IsPreyImminent();
        bool marker = SmartShieldsStack && IsMarkerImminent();
        bool confirmedDamage = raidwide || stack || prey || marker;
        bool bossMitigation = ProactiveBossHealing && IsBossFight();
        bool panic = IsPanicMode();
        bool sustained = IsSustainedPull();

        if (confirmedDamage || bossMitigation || panic || sustained)
        {
            string reason = panic ? "PANIC" : raidwide ? "raidwide" : stack ? "stack"
                : prey ? "prey" : marker ? "marker" : sustained ? "sustained pull" : "boss";

            // Kerachole: 10% mitigation + regen, best value (1 Addersgall) — always first
            if (KeracholePvE.CanUse(out act))
            {
                LogDecision($"Kerachole: {reason}");
                return true;
            }

            // Krasis: +20% healing received — amplifies Physis II regen ticks and subsequent heals
            if ((confirmedDamage || panic) && KrasisPvE.CanUse(out act))
            {
                LogDecision($"Krasis: amplify before {reason}");
                return true;
            }

            // Physis II: regen + 10% healing received buff — snapshots Krasis amplification
            if ((confirmedDamage || panic) && SmartPhysis)
            {
                if (PhysisIiPvE.CanUse(out act))
                {
                    LogDecision($"Physis II: {reason}");
                    return true;
                }
                if (!PhysisIiPvE.EnoughLevel && PhysisPvE.CanUse(out act))
                {
                    LogDecision($"Physis: {reason}");
                    return true;
                }
            }

            // Panhaima: multi-hit shields (120s CD) — for raidwide/stack/sustained/panic
            if ((raidwide || stack || panic || sustained) && PanhaimaPvE.CanUse(out act))
            {
                LogDecision($"Panhaima: {reason}");
                return true;
            }

            // Holos: AoE shield + heal + 10% mit (120s CD)
            if (HolosPvE.CanUse(out act))
            {
                if (confirmedDamage || panic || sustained)
                {
                    LogDecision($"Holos: {reason}");
                    return true;
                }
                if (bossMitigation && PartyMembersAverHP < 0.9f)
                {
                    LogDecision($"Holos: proactive boss mitigation, HP={PartyMembersAverHP:P0}");
                    return true;
                }
            }
        }

        return base.DefenseAreaAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.HaimaPvE, ActionID.TaurocholePvE)]
    protected override bool DefenseSingleAbility(IAction nextGCD, out IAction? act)
    {
        act = null;

        bool tankbuster = SmartShieldsTankbuster && IsTankbusterImminent();
        bool prey = SmartShieldsStack && IsPreyImminent();
        bool bossMitigation = ProactiveBossHealing && IsBossFight();
        bool panic = IsPanicMode();
        bool sustained = IsSustainedPull();

        if (tankbuster || prey || bossMitigation || panic || sustained)
        {
            string reason = panic ? "PANIC pull" : tankbuster ? "tankbuster"
                : prey ? "prey" : sustained ? "sustained pull" : "boss";

            // Haima: strongest ST shield (120s CD)
            if (HaimaPvE.CanUse(out act))
            {
                LogDecision($"Haima: {reason}");
                return true;
            }

            // Taurochole: heal + 10% mit (1 Addersgall)
            if (TaurocholePvE.CanUse(out act))
            {
                LogDecision($"Taurochole: {reason}");
                return true;
            }
        }

        return base.DefenseSingleAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.ZoePvE, ActionID.PhysisIiPvE, ActionID.KeracholePvE, ActionID.IxocholePvE, ActionID.HolosPvE, ActionID.PepsisPvE)]
    protected override bool HealAreaAbility(IAction nextGCD, out IAction? act)
    {
        act = null;

        // Zoe: amplify AoE heals — proactive before group damage and in boss fights
        if (!StatusHelper.PlayerHasStatus(true, StatusID.Zoe) && ZoePvE.CanUse(out act))
        {
            if (ZoeProactive && IsGroupDamageImminent())
            {
                LogDecision("Zoe: amplifying AoE heal for incoming group damage");
                return true;
            }
            if (ZoeProactive && IsBossFight() && PartyMembersAverHP < 0.75f)
            {
                LogDecision($"Zoe: proactive AoE heal amplification (party HP={PartyMembersAverHP:P0})");
                return true;
            }
        }

        // Physis II FIRST: 10% healing buff snapshots onto all subsequent heals this window.
        // Must come before Kerachole/Ixochole to amplify them.
        if (SmartPhysis)
        {
            if ((IsGroupDamageImminent() || PartyMembersAverHP < 0.7f) && PhysisIiPvE.CanUse(out act))
            {
                LogDecision("Physis II: amplifying subsequent heals");
                return true;
            }
            if (!PhysisIiPvE.EnoughLevel && PhysisPvE.CanUse(out act))
                return true;
        }
        else
        {
            if (PhysisIiPvE.CanUse(out act)) return true;
            if (!PhysisIiPvE.EnoughLevel && PhysisPvE.CanUse(out act)) return true;
        }

        // Kerachole: regen + 10% mitigation (Addersgall) — best value AoE oGCD
        if (KeracholePvE.CanUse(out act))
            return true;

        // Ixochole: direct AoE heal (Addersgall) — when party needs immediate HP
        if (IxocholePvE.CanUse(out act))
            return true;

        // Pepsis: convert existing E.Prognosis shields into healing (450p/350p)
        if (StatusHelper.PlayerHasStatus(true, StatusID.EukrasianPrognosis) && PepsisPvE.CanUse(out act))
        {
            LogDecision("Pepsis: converting shields to heals");
            return true;
        }

        // Holos: emergency AoE shield + heal + 10% mit (don't waste heal component at full HP)
        if (PartyMembersAverHP < 0.65f && HolosPvE.CanUse(out act))
            return true;

        return base.HealAreaAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.ZoePvE, ActionID.KrasisPvE, ActionID.TaurocholePvE, ActionID.DruocholePvE, ActionID.SoteriaPvE)]
    protected override bool HealSingleAbility(IAction nextGCD, out IAction? act)
    {
        act = null;

        // Zoe: amplify ST heals — proactive for tankbusters and boss fights
        if (!StatusHelper.PlayerHasStatus(true, StatusID.Zoe) && ZoePvE.CanUse(out act))
        {
            // Proactive: amplify heal before incoming tankbuster
            if (ZoeProactive && IsTankbusterImminent())
            {
                LogDecision("Zoe: amplifying heal for incoming tankbuster");
                return true;
            }

            // Proactive: boss fight, someone needs healing (HealSingleAbility was called = someone is hurt)
            if (ZoeProactive && IsBossFight() && IsAnyPartyMemberBelow(0.60f))
            {
                LogDecision("Zoe: proactive boss fight ST heal amplification");
                return true;
            }

            // Emergency: any member critically low
            if (ZoeEmergencyEnabled && IsAnyPartyMemberBelow(ZoeEmergencyThreshold))
            {
                LogDecision("Zoe: amplifying next ST heal (emergency)");
                return true;
            }

            act = null; // Reset if no Zoe condition met
        }

        // Krasis FIRST: 20% healing received buff on target — snapshots onto Taurochole/Druochole.
        // Must come before the actual heal to amplify it.
        if (KrasisPvE.CanUse(out act))
        {
            LogDecision("Krasis: 20% heal buff before oGCD heal");
            return true;
        }

        // Taurochole: primary ST heal + 10% mit (1 Addersgall) — now amplified by Krasis
        if (TaurocholePvE.CanUse(out act))
            return true;

        // Druochole: fallback ST heal (1 Addersgall)
        if (DruocholePvE.CanUse(out act))
            return true;

        // Soteria: boost Kardia heals (50% stronger Kardion ticks)
        if (SoteriaPvE.CanUse(out act))
            return true;

        return base.HealSingleAbility(nextGCD, out act);
    }

    #endregion

    #region GCD Logic

    protected override bool GeneralGCD(out IAction? act)
    {
        // Per-frame cache invalidation
        _raidwideImminentValid = false;
        _tankbusterImminentValid = false;
        _stackImminentValid = false;
        _preyImminentValid = false;
        _markerImminentValid = false;
        _specialModeCacheValid = false;

        // SpecialMode: Pyretic/Freezing = stop all actions
        var mode = GetCachedSpecialMode();
        if (mode is SpecialModes.Pyretic or SpecialModes.Freezing)
        {
            LogDecision($"Paused: SpecialMode={mode}");
            act = null;
            return false;
        }

        // === Eukrasian DoT uptime ===
        // If Eukrasia is already active, fire the DoT
        if (HasEukrasia)
        {
            if (EukrasianDosisIiiPvE.CanUse(out act)) return true;
            if (EukrasianDosisIiPvE.CanUse(out act)) return true;
            if (EukrasianDosisPvE.CanUse(out act)) return true;
            // Eukrasia active but no DoT action available — use for AoE DoT or shield
            if (EukrasianDyskrasiaPvE.CanUse(out act)) return true;
        }

        // Apply Eukrasia for DoT refresh — only when DoT is missing or expiring
        // CanUse checks TargetStatusProvide + IsRestrictedDOT to verify the DoT actually needs refreshing
        if (EukrasianDosisIiiPvE.CanUse(out _) && EukrasiaPvE.CanUse(out act))
            return true;
        if (!EukrasianDosisIiiPvE.EnoughLevel && EukrasianDosisIiPvE.CanUse(out _) && EukrasiaPvE.CanUse(out act))
            return true;
        if (!EukrasianDosisIiPvE.EnoughLevel && EukrasianDosisPvE.CanUse(out _) && EukrasiaPvE.CanUse(out act))
            return true;

        // === DPS Priority ===

        // Phlegma: highest potency GCD — never sit on 2 charges (timer stops, wasted DPS)
        if (DpsOptimization)
        {
            // usedUp=true: dump when charges would overcap, moving, or in burst windows
            bool shouldDump = IsMoving || IsBurst;
            if (PhlegmaIiiPvE.CanUse(out act, usedUp: shouldDump)) return true;
            if (!PhlegmaIiiPvE.EnoughLevel && PhlegmaIiPvE.CanUse(out act, usedUp: shouldDump)) return true;
            if (!PhlegmaIiPvE.EnoughLevel && PhlegmaPvE.CanUse(out act, usedUp: shouldDump)) return true;
        }
        else
        {
            // Even without DPS optimization, use Phlegma to prevent overcap
            if (PhlegmaIiiPvE.CanUse(out act, usedUp: true)) return true;
            if (!PhlegmaIiiPvE.EnoughLevel && PhlegmaIiPvE.CanUse(out act, usedUp: true)) return true;
            if (!PhlegmaIiPvE.EnoughLevel && PhlegmaPvE.CanUse(out act, usedUp: true)) return true;
        }

        // Pneuma: damage + 600p AoE heal (damage-neutral = free heal)
        // Use proactively: always good when party is not full or damage is incoming
        if (PneumaPvE.CanUse(out act))
        {
            if (PartyMembersAverHP < 0.90f || IsGroupDamageImminent() || IsPanicMode())
                return true;
        }

        // Toxikon: instant cast during movement
        if (DpsOptimization && IsMoving && Addersting >= 1)
        {
            if (ToxikonIiPvE.CanUse(out act)) return true;
            if (!ToxikonIiPvE.EnoughLevel && ToxikonPvE.CanUse(out act)) return true;
        }

        // AoE: Dyskrasia
        if (DyskrasiaIiPvE.CanUse(out act)) return true;
        if (!DyskrasiaIiPvE.EnoughLevel && DyskrasiaPvE.CanUse(out act)) return true;

        // Eukrasian Prognosis GCD shield — LAST RESORT before damage
        // Every GCD heal = lost Dosis/Dyskrasia damage + lost Kardia healing
        // Only use when oGCDs are exhausted AND confirmed big hit incoming
        bool groupDmg = (SmartShieldsRaidwide && IsRaidwideImminent())
                     || (SmartShieldsStack && (IsStackImminent() || IsMarkerImminent()));
        bool oGCDsExhausted = !KeracholePvE.CanUse(out _) && !PanhaimaPvE.CanUse(out _) && !HolosPvE.CanUse(out _);
        if (groupDmg && oGCDsExhausted
            && !StatusHelper.PlayerHasStatus(true, StatusID.EukrasianPrognosis)
            && !HasEukrasia)
        {
            if (EukrasiaPvE.CanUse(out act))
            {
                LogDecision("EukrasianPrognosis: oGCDs exhausted, GCD shield as last resort");
                return true;
            }
        }
        if (HasEukrasia && groupDmg && oGCDsExhausted)
        {
            if (EukrasianPrognosisIiPvE.CanUse(out act)) return true;
            if (EukrasianPrognosisPvE.CanUse(out act)) return true;
        }

        // Filler: Dosis
        if (DosisIiiPvE.CanUse(out act)) return true;
        if (!DosisIiiPvE.EnoughLevel && DosisIiPvE.CanUse(out act)) return true;
        if (!DosisIiPvE.EnoughLevel && DosisPvE.CanUse(out act)) return true;

        return base.GeneralGCD(out act);
    }

    [RotationDesc(ActionID.PneumaPvE, ActionID.EukrasianPrognosisPvE, ActionID.PrognosisPvE)]
    protected override bool HealAreaGCD(out IAction? act)
    {
        // Pneuma: damage + AoE heal — best value
        if (PneumaPvE.CanUse(out act)) return true;

        // Eukrasian Prognosis: AoE shield
        if (HasEukrasia)
        {
            if (EukrasianPrognosisIiPvE.CanUse(out act)) return true;
            if (EukrasianPrognosisPvE.CanUse(out act)) return true;
        }
        if (!HasEukrasia && EukrasiaPvE.CanUse(out act)) return true;

        // Prognosis: plain AoE heal
        if (PrognosisPvE.CanUse(out act)) return true;

        return base.HealAreaGCD(out act);
    }

    [RotationDesc(ActionID.EukrasianDiagnosisPvE, ActionID.DiagnosisPvE)]
    protected override bool HealSingleGCD(out IAction? act)
    {
        // Eukrasian Diagnosis: ST shield
        if (HasEukrasia)
        {
            if (EukrasianDiagnosisPvE.CanUse(out act)) return true;
        }
        if (!HasEukrasia && IsTankbusterImminent() && EukrasiaPvE.CanUse(out act)) return true;

        // Diagnosis: plain ST heal
        if (DiagnosisPvE.CanUse(out act)) return true;

        return base.HealSingleGCD(out act);
    }

    [RotationDesc(ActionID.EgeiroPvE)]
    protected override bool RaiseGCD(out IAction? act)
    {
        if (EgeiroPvE.CanUse(out act)) return true;
        return base.RaiseGCD(out act);
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

        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "=== SGE Dynamic Diagnostics ===");
        ImGui.Separator();

        DrawSimulationControls();
        ImGui.Separator();
        DrawReactionPreview();
        ImGui.Separator();
        DrawLiveState();
        ImGui.Separator();
        DrawDecisionLog();
    }

    private void DrawSimulationControls()
    {
        if (_simEnabled)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.2f, 0.2f, 1f),
                "!! SIMULATION ACTIVE !!");
        }

        bool simEnabled = _simEnabled;
        if (ImGui.Checkbox("Simulation aktivieren", ref simEnabled))
        {
            _simEnabled = simEnabled;
            if (!simEnabled)
            {
                _simRaidwideImminent = false;
                _simTankbusterImminent = false;
                _simSharedImminent = false;
                _simPreyImminent = false;
                _simMarkerImminent = false;
                _simSpecialModeIndex = 0;
                LogDecision("Simulation OFF");
            }
            else
            {
                LogDecision("Simulation ON");
            }
        }

        if (!_simEnabled) return;

        ImGui.Indent(10);
        ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1f, 1f), "Damage Events:");
        bool rw = _simRaidwideImminent;
        if (ImGui.Checkbox("Raidwide", ref rw)) _simRaidwideImminent = rw;
        ImGui.SameLine();
        bool tb = _simTankbusterImminent;
        if (ImGui.Checkbox("Tankbuster", ref tb)) _simTankbusterImminent = tb;
        ImGui.SameLine();
        bool sh = _simSharedImminent;
        if (ImGui.Checkbox("Stack/Shared", ref sh)) _simSharedImminent = sh;
        ImGui.SameLine();
        bool pr = _simPreyImminent;
        if (ImGui.Checkbox("Prey", ref pr)) _simPreyImminent = pr;
        ImGui.SameLine();
        bool mk = _simMarkerImminent;
        if (ImGui.Checkbox("Marker/Spread", ref mk)) _simMarkerImminent = mk;

        ImGui.SetNextItemWidth(120);
        int specIdx = _simSpecialModeIndex;
        if (ImGui.Combo("SpecialMode", ref specIdx, SpecialModeNames, SpecialModeNames.Length))
            _simSpecialModeIndex = specIdx;
        ImGui.Unindent(10);
    }

    private void DrawReactionPreview()
    {
        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "[Reaction Preview]");

        ColoredBool("  Raidwide Imminent", IsRaidwideImminent());
        ColoredBool("  Tankbuster Imminent", IsTankbusterImminent());
        ColoredBool("  Stack Imminent", IsStackImminent());
        ColoredBool("  Prey Imminent", IsPreyImminent());
        ColoredBool("  Marker/Spread Imminent", IsMarkerImminent());
        ColoredBool("  Sustained Pull (tank < 75%)", IsSustainedPull());
        ColoredBool("  PANIC Mode (tank < 40%)", IsPanicMode());

        ImGui.Spacing();
        float lowestHp = GetLowestPartyMemberHp();
        bool zoeWouldFire = ZoeEmergencyEnabled && lowestHp < ZoeEmergencyThreshold && !StatusHelper.PlayerHasStatus(true, StatusID.Zoe);
        ImGui.Text($"  Lowest Party HP: {lowestHp:P0}");
        ColoredBool($"  Zoe Emergency (threshold={ZoeEmergencyThreshold:P0})", zoeWouldFire);

        ImGui.Spacing();
        ImGui.Text($"  SpecialMode: {GetCachedSpecialMode() ?? SpecialModes.Normal}");
    }

    private void DrawLiveState()
    {
        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "[Live State]");

        // Sage Gauge
        ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1f, 1f), "Gauge:");
        ImGui.Text($"  Addersgall: {Addersgall}/3 | Timer: {AddersgallTime / 1000f:F1}s");
        ImGui.Text($"  Addersting: {Addersting}/3 | Eukrasia: {(HasEukrasia ? "ACTIVE" : "off")}");

        // Kardia
        ImGui.Spacing();
        ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1f, 1f), "Kardia:");
        bool hasKardia = false;
        foreach (var m in PartyMembers)
        {
            if (m.HasStatus(true, StatusID.Kardion))
            {
                ImGui.Text($"  Kardion on: {m.Name} ({m.GetHealthRatio():P0} HP)");
                hasKardia = true;
                break;
            }
        }
        if (!hasKardia) ImGui.TextDisabled("  No Kardion active");

        // Party HP
        ImGui.Spacing();
        ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1f, 1f), "Party:");
        ImGui.Text($"  Average HP: {PartyMembersAverHP:P0} | Lowest: {GetLowestPartyMemberHp():P0}");

        // BossMod IPC
        ImGui.Spacing();
        ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1f, 1f), "BossMod IPC:");
        bool bossModOk = UseBossModIPC && BossModHints_IPCSubscriber.IsEnabled;
        if (bossModOk)
        {
            try
            {
                bool rw = BossModHints_IPCSubscriber.Hints_IsRaidwideImminent?.Invoke(BossModLookahead) == true;
                bool tb = BossModHints_IPCSubscriber.Hints_IsTankbusterImminent?.Invoke(TankbusterLeadTime) == true;
                bool sh = BossModHints_IPCSubscriber.Hints_IsSharedImminent?.Invoke(BossModLookahead) == true;
                ColoredBool($"  Raidwide ({RaidwideLeadTime:F0}s)", rw);
                ImGui.SameLine();
                ColoredBool($"| TB ({TankbusterLeadTime:F1}s)", tb);
                ColoredBool($"  Shared ({BossModLookahead:F0}s)", sh);
                ImGui.Text($"  SpecialMode: {GetCachedSpecialMode() ?? SpecialModes.Normal}");
            }
            catch { ImGui.TextDisabled("  (IPC Error)"); }
        }
        else
        {
            ImGui.TextDisabled($"  Not connected (UseBossModIPC={UseBossModIPC})");
        }

        // Combat
        // Boss Fight & Proactive State
        ImGui.Spacing();
        ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1f, 1f), "Proactive:");
        ColoredBool("  Boss Fight", IsBossFight());
        ImGui.SameLine();
        ColoredBool("| Proactive Boss Healing", ProactiveBossHealing);
        ImGui.SameLine();
        ColoredBool("| Proactive Zoe", ZoeProactive);

        ImGui.Spacing();
        ImGui.Text($"  Territory: {DataCenter.TerritoryID} | InCombat: {InCombat}");
    }

    private static void DrawDecisionLog()
    {
        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "[Decision Log]");

        if (_decisionLogCount == 0)
        {
            ImGui.TextDisabled("  (no entries yet)");
            return;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Clear"))
        {
            _decisionLogCount = 0;
            _decisionLogIndex = 0;
        }

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

    private static void ColoredBool(string label, bool value)
    {
        var color = value
            ? new System.Numerics.Vector4(0.2f, 1f, 0.2f, 1f)
            : new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 1f);
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

    #endregion
}
