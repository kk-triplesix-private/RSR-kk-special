using RotationSolver.Basic.Configuration;
using RotationSolver.IPC;

namespace RotationSolver.ExtraRotations.Magical;

[Rotation("BLM Dynamic", CombatType.PvE, GameVersion = "7.45")]
[SourceCode(Path = "main/ExtraRotations/Magical/BLM_Dynamic.cs")]

public sealed class BLM_Dynamic : BlackMageRotation
{
    #region Config Options

    [RotationConfig(CombatType.PvE, Name = "Use BossMod IPC for raidwide detection (Addle/Manaward timing)")]
    public bool UseBossModIPC { get; set; } = true;

    [Range(1, 10, ConfigUnitType.Seconds)]
    [RotationConfig(CombatType.PvE, Name = "BossMod lookahead (seconds) for raidwide detection")]
    public float BossModLookahead { get; set; } = 5f;

    [RotationConfig(CombatType.PvE, Name = "Smart Ley Lines: Use on cooldown in combat")]
    public bool SmartLeyLines { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Smart Transpose: Use Transpose → Paradox → Fire III path from ice")]
    public bool SmartTranspose { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Smart Polyglot: Anti-overcap + use Xenoglossy for movement")]
    public bool SmartPolyglot { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Auto Addle: Use Addle before detected raidwides")]
    public bool AutoAddle { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Auto Manaward: Use Manaward before detected damage")]
    public bool AutoManaward { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Diagnostics: Show real-time decision state in Rotation Status panel")]
    public bool ShowDiagnostics { get; set; } = false;

    #endregion

    #region Helper Properties & Caching

    // Decision log ring buffer
    private readonly string[] _decisionLog = new string[30];
    private int _decisionIndex;

    private void LogDecision(string message)
    {
        _decisionLog[_decisionIndex % _decisionLog.Length] = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _decisionIndex++;
    }

    // Per-frame BossMod cache
    private bool _raidwideImminentCached;
    private bool _raidwideImminentValid;

    private bool IsRaidwideImminent()
    {
        if (_raidwideImminentValid) return _raidwideImminentCached;
        _raidwideImminentValid = true;
        _raidwideImminentCached = false;

        if (!UseBossModIPC) return false;

        try
        {
            if (BossModHints_IPCSubscriber.Hints_IsRaidwideImminent?.Invoke(BossModLookahead) == true)
            {
                LogDecision("Raidwide=TRUE (BossMod)");
                _raidwideImminentCached = true;
            }
        }
        catch { /* BossMod not loaded */ }

        return _raidwideImminentCached;
    }

    /// <summary>True if target is a boss (for resource planning).</summary>
    private static bool IsBossFight()
    {
        return CurrentTarget is not null && (CurrentTarget.IsBossFromIcon() || CurrentTarget.IsBossFromTTK());
    }

    /// <summary>How many enemies are in AoE range.</summary>
    private int GetAoeTargetCount()
    {
        if (AllHostileTargets == null) return 0;
        int count = 0;
        foreach (var t in AllHostileTargets)
        {
            if (t.DistanceToPlayer() < 25) count++;
        }
        return count;
    }

    /// <summary>True if Astral Fire timer is about to expire within given GCDs.</summary>
    private bool IsTimerCritical(uint gcds = 2) => InAstralFire && EnochianEndAfterGCD(gcds);

    /// <summary>True if we have enough MP for another Fire IV plus a Paradox refresh.</summary>
    private bool CanFireIvAndParadox => CurrentMp >= 3200; // 1600 + 1600

    /// <summary>True if we can enter fire phase (MP full, hearts full, ice stacks maxed).</summary>
    private bool ReadyForFirePhase =>
        UmbralIceStacks >= MaxSoulCount
        && (UmbralHearts >= 3 || !BlizzardIvPvE.EnoughLevel) // Hearts don't exist below Lv58
        && CurrentMp >= 9600;

    /// <summary>True if Fire IV is available at current level.</summary>
    private bool HasFireIv => FireIvPvE.EnoughLevel;

    /// <summary>True if Paradox is available at current level.</summary>
    private bool HasParadox => ParadoxPvE.EnoughLevel;

    /// <summary>True if Despair is available at current level.</summary>
    private bool HasDespair => DespairPvE.EnoughLevel;

    /// <summary>True if we should use Thunder (Thunderhead proc + DoT missing/expiring).</summary>
    private bool ShouldThunder()
    {
        if (!HasThunder) return false;
        if (IsLastGCD(ActionID.ThunderPvE, ActionID.ThunderIiPvE, ActionID.ThunderIiiPvE,
            ActionID.ThunderIvPvE, ActionID.HighThunderPvE, ActionID.HighThunderIiPvE)) return false;

        // Check if target DoT is missing or about to expire
        var target = CurrentTarget;
        if (target == null) return true; // No target to check, use if proc available

        bool hasDoT = target.HasStatus(true,
            StatusID.Thunder, StatusID.ThunderIi, StatusID.ThunderIii,
            StatusID.ThunderIv, StatusID.HighThunder_3872, StatusID.HighThunder);

        if (!hasDoT) return true;

        float remaining = target.StatusTime(true,
            StatusID.Thunder, StatusID.ThunderIi, StatusID.ThunderIii,
            StatusID.ThunderIv, StatusID.HighThunder_3872, StatusID.HighThunder);

        return remaining < 4f;
    }

    /// <summary>True if Polyglot stacks need spending to prevent overcap.</summary>
    private bool PolyglotWillOvercap(uint gcds = 3) =>
        IsPolyglotStacksMaxed && EnochianEndAfterGCD(gcds);

    #endregion

    #region Countdown

    protected override IAction? CountDownAction(float remainTime)
    {
        // Fire III pre-pull (~3.5s cast)
        if (remainTime < FireIiiPvE.Info.CastTime + CountDownAhead)
        {
            if (FireIiiPvE.CanUse(out IAction act))
            {
                LogDecision("Countdown: Fire III pre-pull");
                return act;
            }
        }
        return base.CountDownAction(remainTime);
    }

    #endregion

    #region oGCD Logic

    [RotationDesc]
    protected override bool EmergencyAbility(IAction nextGCD, out IAction? act)
    {
        act = null;

        // Manafont: extend fire phase when out of MP
        if (InAstralFire && CurrentMp < 800 && AstralSoulStacks < 6
            && ManafontPvE.CanUse(out act))
        {
            LogDecision($"Manafont: extending fire phase (Souls={AstralSoulStacks})");
            return true;
        }

        // Transpose: smart UI → AF transition (Transpose → Paradox → Fire III Firestarter)
        if (SmartTranspose && InUmbralIce && ReadyForFirePhase && !IsParadoxActive
            && (HasFire || NextGCDisInstant) && TransposePvE.CanUse(out act))
        {
            LogDecision("Transpose: optimized UI→AF transition");
            return true;
        }

        // Transpose: AoE phase transition AF → UI (after Flare Star or when stuck)
        if (InAstralFire && CurrentMp < 800 && ManafontPvE.Cooldown.IsCoolingDown
            && AstralSoulStacks < 6 && NextGCDisInstant && TransposePvE.CanUse(out act))
        {
            LogDecision("Transpose: AF→UI (low MP, Manafont on CD)");
            return true;
        }

        // Swiftcast for instant Blizzard III exit from fire phase
        if (InAstralFire && CurrentMp < 800 && ManafontPvE.Cooldown.IsCoolingDown
            && AstralSoulStacks < 6 && !NextGCDisInstant
            && nextGCD.IsTheSameTo(true, BlizzardIiiPvE)
            && SwiftcastPvE.CanUse(out act))
        {
            LogDecision("Swiftcast: instant Blizzard III exit");
            return true;
        }

        // Triplecast for instant Blizzard III if Swiftcast unavailable
        if (InAstralFire && !NextGCDisInstant
            && nextGCD.IsTheSameTo(true, BlizzardIiiPvE)
            && ManafontPvE.Cooldown.IsCoolingDown
            && !SwiftcastPvE.Cooldown.HasOneCharge
            && TriplecastPvE.CanUse(out act, usedUp: true))
        {
            LogDecision("Triplecast: instant Blizzard III exit");
            return true;
        }

        return base.EmergencyAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.TriplecastPvE, ActionID.AmplifierPvE, ActionID.LeyLinesPvE)]
    protected override bool AttackAbility(IAction nextGCD, out IAction? act)
    {
        act = null;

        if (!InCombat || !HasHostilesInRange) return base.AttackAbility(nextGCD, out act);

        // Ley Lines: use on cooldown during combat
        if (SmartLeyLines && LeyLinesPvE.CanUse(out act))
        {
            LogDecision("Ley Lines: burst window");
            return true;
        }

        // Triplecast: use during fire phase for instant Fire IVs
        if (InAstralFire && !NextGCDisInstant && TriplecastPvE.CanUse(out act, gcdCountForAbility: 5))
        {
            LogDecision("Triplecast: fire phase instant casts");
            return true;
        }

        // Amplifier: generate Polyglot stack (don't overcap)
        if (!IsPolyglotStacksMaxed && AmplifierPvE.CanUse(out act))
        {
            LogDecision($"Amplifier: Polyglot {PolyglotStacks}→{PolyglotStacks + 1}");
            return true;
        }

        // Swiftcast: for instant transition in ice phase
        if (InUmbralIce && UmbralIceStacks >= MaxSoulCount && !HasFire
            && !NextGCDisInstant && !IsLastGCD(ActionID.ParadoxPvE)
            && SwiftcastPvE.CanUse(out act))
        {
            LogDecision("Swiftcast: instant ice→fire transition");
            return true;
        }

        // Triplecast: backup for ice→fire transition
        if (InUmbralIce && UmbralIceStacks >= MaxSoulCount && !HasFire
            && !NextGCDisInstant && !SwiftcastPvE.Cooldown.HasOneCharge
            && TriplecastPvE.CanUse(out act, usedUp: true))
        {
            LogDecision("Triplecast: ice→fire transition backup");
            return true;
        }

        // Low level: Lucid Dreaming for MP recovery when no Umbral Hearts
        if (!BlizzardIvPvE.EnoughLevel && InAstralFire && CurrentMp < 5000
            && ManafontPvE.Cooldown.IsCoolingDown && LucidDreamingPvE.CanUse(out act))
        {
            LogDecision("Lucid Dreaming: MP recovery (low level)");
            return true;
        }

        // Medicine during burst
        if (IsBursting() && UseBurstMedicine(out act))
            return true;

        return base.AttackAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.RetracePvE)]
    protected override bool GeneralAbility(IAction nextGCD, out IAction? act)
    {
        act = null;

        // Triplecast for movement
        if (IsMoving && InCombat && HasHostilesInRange && !NextGCDisInstant
            && TriplecastPvE.CanUse(out act, usedUp: true))
        {
            LogDecision("Triplecast: movement");
            return true;
        }

        // Retrace back to Ley Lines
        if (InCombat && HasHostilesInRange && RetracePvE.CanUse(out act))
        {
            LogDecision("Retrace: returning to Ley Lines");
            return true;
        }

        return base.GeneralAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.AetherialManipulationPvE)]
    protected override bool MoveForwardAbility(IAction nextGCD, out IAction? act)
    {
        if (AetherialManipulationPvE.CanUse(out act)) return true;
        return base.MoveForwardAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.BetweenTheLinesPvE)]
    protected override bool MoveBackAbility(IAction nextGCD, out IAction? act)
    {
        if (BetweenTheLinesPvE.CanUse(out act)) return true;
        return base.MoveBackAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.ManawardPvE)]
    protected override bool DefenseSingleAbility(IAction nextGCD, out IAction? act)
    {
        if (AutoManaward && IsRaidwideImminent() && ManawardPvE.CanUse(out act))
        {
            LogDecision("Manaward: incoming raidwide");
            return true;
        }
        if (ManawardPvE.CanUse(out act)) return true;
        return base.DefenseSingleAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.AddlePvE, ActionID.ManawardPvE)]
    protected override bool DefenseAreaAbility(IAction nextGCD, out IAction? act)
    {
        if (AutoAddle && IsRaidwideImminent() && AddlePvE.CanUse(out act))
        {
            LogDecision("Addle: incoming raidwide");
            return true;
        }
        if (ManawardPvE.CanUse(out act)) return true;
        if (AddlePvE.CanUse(out act)) return true;
        return base.DefenseAreaAbility(nextGCD, out act);
    }

    #endregion

    #region GCD Logic

    protected override bool GeneralGCD(out IAction? act)
    {
        act = null;

        // Invalidate per-frame caches
        _raidwideImminentValid = false;

        // Priority 0: Flare Star — 0 MP, massive damage when 6 Astral Soul stacks
        if (FlareStarPvE.CanUse(out act))
        {
            LogDecision($"Flare Star: 6 Astral Soul stacks");
            return true;
        }

        int aoeCount = GetAoeTargetCount();

        // AoE rotation (3+ targets)
        if (aoeCount >= 3)
        {
            if (DoAoERotation(out act)) return true;
        }

        // Single target / 2-target rotation
        if (DoSingleTargetRotation(out act, aoeCount >= 2)) return true;

        // Out-of-combat maintenance
        if (DoMaintenance(out act)) return true;

        return base.GeneralGCD(out act);
    }

    #region Single Target Rotation

    private bool DoSingleTargetRotation(out IAction? act, bool twoTargets)
    {
        act = null;

        // Thunder: Thunderhead proc management
        if (DoThunder(out act, twoTargets)) return true;

        // Phase-specific logic
        if (InAstralFire)
        {
            if (DoFirePhase(out act)) return true;
        }

        if (InUmbralIce)
        {
            if (DoIcePhase(out act, twoTargets)) return true;
        }

        // Neutral state: enter rotation
        if (!InAstralFire && !InUmbralIce)
        {
            if (DoStartCombat(out act)) return true;
        }

        // Movement filler: Xenoglossy (instant, high damage)
        if (IsMoving && !NextGCDisInstant && SmartPolyglot)
        {
            if (XenoglossyPvE.CanUse(out act))
            {
                LogDecision("Xenoglossy: movement filler");
                return true;
            }
        }

        return false;
    }

    private bool DoFirePhase(out IAction? act)
    {
        act = null;

        // 1. Stack maintenance — restore AF3 if stacks dropped
        if (AstralFireStacks < MaxSoulCount)
        {
            if (HasFire && FireIiiPvE.CanUse(out act))
            {
                LogDecision("Fire III: Firestarter proc → AF max");
                return true;
            }
            if (IsParadoxActive && ParadoxPvE.CanUse(out act))
            {
                LogDecision("Paradox: restoring AF stacks");
                return true;
            }
            // Low level: Fire I to build stacks
            if (!HasParadox && FireIiiPvE.CanUse(out act))
            {
                LogDecision("Fire III: restoring AF stacks");
                return true;
            }
        }

        // 2. Timer critical — must refresh with Paradox/Fire I or exit
        if (IsTimerCritical(2))
        {
            // At 5 stacks, try to squeeze one more F4 for Flare Star
            if (AstralSoulStacks == 5 && !IsTimerCritical(1) && FireIvPvE.CanUse(out act))
            {
                LogDecision("Fire IV: 5→6 stacks (timer tight but fits)");
                return true;
            }
            if (IsParadoxActive && ParadoxPvE.CanUse(out act))
            {
                LogDecision("Paradox: timer refresh (critical)");
                return true;
            }
            // Pre-Paradox: Fire I refreshes AF timer
            if (!HasParadox && CurrentMp >= FirePvE.Info.MPNeed && FirePvE.CanUse(out act))
            {
                LogDecision("Fire I: timer refresh (no Paradox)");
                return true;
            }
            // Timer critical — finisher or exit
            if (DespairPvE.CanUse(out act))
            {
                LogDecision("Despair: forced exit (timer critical)");
                return true;
            }
            if (BlizzardIiiPvE.CanUse(out act))
            {
                LogDecision("Blizzard III: emergency exit (timer critical)");
                return true;
            }
            // Low level fallback
            if (BlizzardPvE.CanUse(out act))
            {
                LogDecision("Blizzard: emergency exit (timer critical, low level)");
                return true;
            }
        }

        // 3. Fire IV — main damage spell (Lv60+)
        if (HasFireIv && FireIvPvE.CanUse(out act))
        {
            // Reserve MP for Paradox if timer is getting low
            if (IsParadoxActive && IsTimerCritical(4) && !CanFireIvAndParadox)
            {
                // Skip F4, save MP for Paradox refresh below
            }
            else
            {
                LogDecision($"Fire IV: MP={CurrentMp}, Souls={AstralSoulStacks}");
                return true;
            }
        }

        // 4. Fire I / Paradox — timer refresh and filler
        if (IsParadoxActive && ParadoxPvE.CanUse(out act))
        {
            LogDecision($"Paradox: refresh/proc (Souls={AstralSoulStacks})");
            return true;
        }
        // Pre-Lv60: Fire I is the main damage spell (also refreshes timer)
        if (!HasFireIv && CurrentMp >= FirePvE.Info.MPNeed && FirePvE.CanUse(out act))
        {
            LogDecision($"Fire I: main damage (pre-Lv60, MP={CurrentMp})");
            return true;
        }

        // 5. Firestarter proc — use Fire III if available
        if (HasFire && FireIiiPvE.CanUse(out act))
        {
            LogDecision("Fire III: Firestarter proc");
            return true;
        }

        // 6. Despair — finisher when MP too low for Fire IV (Lv72+)
        if (DespairPvE.CanUse(out act))
        {
            LogDecision($"Despair: finisher (MP={CurrentMp}, Souls={AstralSoulStacks})");
            return true;
        }

        // 7. Flare — AoE finisher for soul stacks
        if (FlarePvE.CanUse(out act))
        {
            LogDecision("Flare: finisher for soul stacks");
            return true;
        }

        // 8. Exit fire phase — transition to ice
        if (BlizzardIiiPvE.CanUse(out act))
        {
            LogDecision("Blizzard III: fire→ice transition");
            return true;
        }
        // Low level: Transpose or Blizzard I
        if (TransposePvE.CanUse(out act))
        {
            LogDecision("Transpose: fire→ice transition");
            return true;
        }
        if (BlizzardPvE.CanUse(out act))
        {
            LogDecision("Blizzard: fire→ice (low level)");
            return true;
        }

        return false;
    }

    private bool DoIcePhase(out IAction? act, bool twoTargets)
    {
        act = null;

        // 1. Build Umbral Ice stacks to max
        if (UmbralIceStacks < MaxSoulCount)
        {
            if (BlizzardIiiPvE.CanUse(out act))
            {
                LogDecision($"Blizzard III: building UI stacks ({UmbralIceStacks}→{MaxSoulCount})");
                return true;
            }
            // Low level: Blizzard I to build stacks
            if (BlizzardPvE.CanUse(out act))
            {
                LogDecision($"Blizzard: building UI stacks ({UmbralIceStacks}, low level)");
                return true;
            }
        }

        // 2. Build Umbral Hearts to 3 (Lv58+)
        if (BlizzardIvPvE.EnoughLevel && UmbralHearts < 3
            && !IsLastGCD(ActionID.BlizzardIvPvE, ActionID.FreezePvE))
        {
            if (twoTargets && FreezePvE.CanUse(out act, skipAoeCheck: true))
            {
                LogDecision($"Freeze: building hearts ({UmbralHearts}/3) [2+ targets]");
                return true;
            }
            if (BlizzardIvPvE.CanUse(out act))
            {
                LogDecision($"Blizzard IV: building hearts ({UmbralHearts}/3)");
                return true;
            }
        }

        // 3. Polyglot anti-overcap (Lv70+)
        if (SmartPolyglot && PolyglotWillOvercap())
        {
            if (XenoglossyPvE.CanUse(out act))
            {
                LogDecision($"Xenoglossy: anti-overcap (Polyglot={PolyglotStacks})");
                return true;
            }
            if (FoulPvE.CanUse(out act))
            {
                LogDecision($"Foul: anti-overcap (Polyglot={PolyglotStacks})");
                return true;
            }
        }

        // 4. Paradox — instant filler in ice phase (Lv90+)
        if (IsParadoxActive && ParadoxPvE.CanUse(out act))
        {
            LogDecision("Paradox: ice phase filler");
            return true;
        }

        // 5. Ready to enter fire phase?
        if (ReadyForFirePhase)
        {
            // Firestarter proc: instant Fire III
            if (HasFire && FireIiiPvE.CanUse(out act))
            {
                LogDecision("Fire III: Firestarter → entering fire phase");
                return true;
            }

            // Standard: Fire III (Lv35+)
            if (FireIiiPvE.CanUse(out act))
            {
                LogDecision("Fire III: entering fire phase");
                return true;
            }

            // Low level: Transpose or Fire I
            if (TransposePvE.CanUse(out act))
            {
                LogDecision("Transpose: ice→fire transition");
                return true;
            }
            if (FirePvE.CanUse(out act))
            {
                LogDecision("Fire: entering fire phase (low level)");
                return true;
            }
        }

        // 6. Additional fillers while waiting for MP/hearts
        if (XenoglossyPvE.CanUse(out act) && PolyglotStacks >= 2)
        {
            LogDecision("Xenoglossy: ice phase filler (2+ stacks)");
            return true;
        }

        // Keep casting Blizzard spells for MP recovery
        if (UmbralIceStacks < MaxSoulCount)
        {
            if (BlizzardIiiPvE.CanUse(out act)) return true;
            if (BlizzardPvE.CanUse(out act)) return true;
        }

        // Low level: Blizzard I while waiting for MP
        if (!BlizzardIvPvE.EnoughLevel && CurrentMp < 9600 && BlizzardPvE.CanUse(out act))
        {
            LogDecision("Blizzard: MP recovery (low level)");
            return true;
        }

        return false;
    }

    private bool DoStartCombat(out IAction? act)
    {
        act = null;

        // Full MP: start with Fire III for immediate damage
        if (CurrentMp >= 9600)
        {
            if (FireIiiPvE.CanUse(out act))
            {
                LogDecision("Fire III: combat opener (full MP)");
                return true;
            }
            if (FirePvE.CanUse(out act))
            {
                LogDecision("Fire: combat opener (full MP, low level)");
                return true;
            }
        }

        // Low MP: start with Blizzard III to recover
        if (BlizzardIiiPvE.CanUse(out act))
        {
            LogDecision("Blizzard III: combat start (MP recovery)");
            return true;
        }
        if (BlizzardPvE.CanUse(out act))
        {
            LogDecision("Blizzard: combat start (low level)");
            return true;
        }

        return false;
    }

    #endregion

    #region AoE Rotation

    private bool DoAoERotation(out IAction? act)
    {
        act = null;

        // AoE Thunder: Thunderhead proc on multiple targets
        if (HasThunder)
        {
            if (HighThunderIiPvE.CanUse(out act))
            {
                LogDecision("High Thunder II: AoE Thunderhead proc");
                return true;
            }
            if (ThunderIiPvE.CanUse(out act))
            {
                LogDecision("Thunder II: AoE Thunderhead proc");
                return true;
            }
        }

        if (InAstralFire)
        {
            if (DoAoEFirePhase(out act)) return true;
        }

        if (InUmbralIce)
        {
            if (DoAoEIcePhase(out act)) return true;
        }

        // Neutral: enter AoE rotation
        if (!InAstralFire && !InUmbralIce)
        {
            if (CurrentMp >= 9600)
            {
                if (HighFireIiPvE.CanUse(out act, skipAoeCheck: true))
                {
                    LogDecision("High Fire II: AoE start");
                    return true;
                }
                if (FireIiPvE.CanUse(out act, skipAoeCheck: true))
                {
                    LogDecision("Fire II: AoE start (full MP)");
                    return true;
                }
            }
            if (HighBlizzardIiPvE.CanUse(out act, skipAoeCheck: true))
            {
                LogDecision("High Blizzard II: AoE start");
                return true;
            }
            if (BlizzardIiPvE.CanUse(out act, skipAoeCheck: true))
            {
                LogDecision("Blizzard II: AoE start");
                return true;
            }
        }

        return false;
    }

    private bool DoAoEFirePhase(out IAction? act)
    {
        act = null;

        // Flare: main AoE damage, +3 Astral Soul stacks each (Lv50+)
        if (FlarePvE.CanUse(out act, skipAoeCheck: true))
        {
            LogDecision($"Flare: AoE fire (Souls={AstralSoulStacks}, MP={CurrentMp})");
            return true;
        }

        // Pre-Flare (< Lv50): Fire II spam
        if (!FlarePvE.EnoughLevel)
        {
            if (CurrentMp >= FireIiPvE.Info.MPNeed && FireIiPvE.CanUse(out act, skipAoeCheck: true))
            {
                LogDecision($"Fire II: AoE fire (MP={CurrentMp}, low level)");
                return true;
            }
            // Out of MP: exit to ice
            if (TransposePvE.CanUse(out act))
            {
                LogDecision("Transpose: AoE fire→ice (low level)");
                return true;
            }
            return false;
        }

        // Out of MP for Flare — use filler before Transpose
        if (AstralSoulStacks == 0 && PolyglotStacks >= 2
            && FoulPvE.CanUse(out act, skipAoeCheck: true))
        {
            LogDecision("Foul: AoE filler before Transpose");
            return true;
        }

        // Transpose to ice when out of MP
        if (CurrentMp < 800 && TransposePvE.CanUse(out act))
        {
            LogDecision("Transpose: AoE fire→ice");
            return true;
        }

        return false;
    }

    private bool DoAoEIcePhase(out IAction? act)
    {
        act = null;

        // Transpose to fire when hearts are available (Lv58+)
        if (BlizzardIvPvE.EnoughLevel && UmbralHearts > 0 && TransposePvE.CanUse(out act))
        {
            LogDecision($"Transpose: AoE ice→fire (Hearts={UmbralHearts})");
            return true;
        }

        // After Freeze: use fillers
        if (IsLastAction(true, FreezePvE))
        {
            if (FoulPvE.CanUse(out act, skipAoeCheck: true))
            {
                LogDecision("Foul: AoE ice filler");
                return true;
            }
            if (IsParadoxActive && ParadoxPvE.CanUse(out act, skipAoeCheck: true))
            {
                LogDecision("Paradox: AoE ice filler");
                return true;
            }
        }

        // Freeze: build Umbral Hearts (Lv35+)
        if (FreezePvE.CanUse(out act, skipAoeCheck: true))
        {
            if (!BlizzardIvPvE.EnoughLevel || UmbralHearts == 0)
            {
                LogDecision("Freeze: building hearts/stacks for AoE");
                return true;
            }
        }

        // Pre-Freeze (< Lv35): Blizzard II
        if (!FreezePvE.EnoughLevel && BlizzardIiPvE.CanUse(out act, skipAoeCheck: true))
        {
            LogDecision("Blizzard II: AoE ice (low level)");
            return true;
        }

        // Ready to enter fire: Transpose
        if (CurrentMp >= 9600 && TransposePvE.CanUse(out act))
        {
            LogDecision("Transpose: AoE ice→fire (MP full)");
            return true;
        }

        return false;
    }

    #endregion

    #region Thunder Logic

    private bool DoThunder(out IAction? act, bool twoTargets)
    {
        act = null;

        if (!ShouldThunder()) return false;

        // Don't thunder if we're about to drop Enochian
        if ((InAstralFire || InUmbralIce) && EnochianEndAfterGCD(1)) return false;

        // AoE thunder
        if (twoTargets)
        {
            if (HighThunderIiPvE.CanUse(out act))
            {
                LogDecision("High Thunder II: DoT refresh");
                return true;
            }
            if (ThunderIiPvE.CanUse(out act))
            {
                LogDecision("Thunder II: DoT refresh");
                return true;
            }
        }

        // ST thunder
        if (HighThunderPvE.CanUse(out act))
        {
            LogDecision("High Thunder: DoT refresh");
            return true;
        }
        if (ThunderIiiPvE.CanUse(out act))
        {
            LogDecision("Thunder III: DoT refresh");
            return true;
        }
        if (ThunderPvE.CanUse(out act))
        {
            LogDecision("Thunder: DoT refresh");
            return true;
        }

        return false;
    }

    #endregion

    #region Maintenance

    private bool DoMaintenance(out IAction? act)
    {
        act = null;

        // Don't do maintenance in early combat
        if (CombatElapsedLess(6)) return false;

        // Umbral Soul: maintain ice phase out of combat
        if (UmbralSoulPvE.CanUse(out act))
        {
            LogDecision("Umbral Soul: maintenance");
            return true;
        }

        // Transpose from AF when no targets
        if (InAstralFire && !HasHostilesInRange && TransposePvE.CanUse(out act))
        {
            LogDecision("Transpose: no targets, AF→UI for maintenance");
            return true;
        }

        return false;
    }

    #endregion

    #endregion

    #region Diagnostics

    public override void DisplayRotationStatus()
    {
        if (!ShowDiagnostics)
        {
            base.DisplayRotationStatus();
            return;
        }

        ImGui.Text("=== BLM Dynamic Diagnostics ===");
        ImGui.Separator();

        // Phase state
        string phase = InAstralFire ? $"ASTRAL FIRE ({AstralFireStacks})"
            : InUmbralIce ? $"UMBRAL ICE ({UmbralIceStacks})"
            : "NEUTRAL";
        ImGui.Text($"Phase: {phase}");

        // Resources
        ImGui.Text($"MP: {CurrentMp}/10000 | Hearts: {UmbralHearts}/3 | Paradox: {(IsParadoxActive ? "YES" : "no")}");
        ImGui.Text($"Astral Soul: {AstralSoulStacks}/6 | Polyglot: {PolyglotStacks}/{(IsPolyglotStacksMaxed ? "MAX" : "ok")}");
        ImGui.Text($"Enochian: {EnochianTime:F1}s | Timer Critical: {IsTimerCritical()}");

        // Procs
        ImGui.Text($"Firestarter: {(HasFire ? "YES" : "no")} | Thunderhead: {(HasThunder ? "YES" : "no")}");
        ImGui.Text($"Instant Available: {(NextGCDisInstant ? "YES" : "no")} | Can Make Instant: {(CanMakeInstant ? "YES" : "no")}");

        // Conditions
        ImGui.Text($"Ready for Fire: {ReadyForFirePhase} | Should Thunder: {ShouldThunder()}");
        ImGui.Text($"Polyglot Overcap: {PolyglotWillOvercap()} | AoE Targets: {GetAoeTargetCount()}");

        // BossMod
        if (UseBossModIPC)
        {
            ImGui.Text($"Raidwide Imminent: {IsRaidwideImminent()}");
        }

        ImGui.Separator();
        ImGui.Text("=== Decision Log ===");

        // Show last 10 decisions
        for (int i = Math.Max(0, _decisionIndex - 10); i < _decisionIndex; i++)
        {
            string? entry = _decisionLog[i % _decisionLog.Length];
            if (entry != null)
            {
                ImGui.Text(entry);
            }
        }

        base.DisplayRotationStatus();
    }

    #endregion
}
