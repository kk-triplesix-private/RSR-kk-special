using System.ComponentModel;
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

    [RotationConfig(CombatType.PvE, Name = "Proactive Radiant Aegis on raidwide casts")]
    public bool ProactiveAegis { get; set; } = true;

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

    [RotationConfig(CombatType.PvE, Name = "Use second charge of Radiant Aegis on cooldown")]
    public bool RadiantOnCooldown { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use Radiant Aegis on cooldown (spam)")]
    public bool RadiantOnCooldownSpam { get; set; } = false;

    [RotationConfig(CombatType.PvE, Name = "Smart Ruin IV: Save for movement, prefer Ruin III when stationary")]
    public bool SmartRuinIV { get; set; } = true;

    [Range(0, 2000, ConfigUnitType.None)]
    [RotationConfig(CombatType.PvE, Name = "Max delay (ms) for big summon before forcing Ruin IV instead of Ruin III")]
    public int BigSummonDelayThreshold { get; set; } = 500;

    [RotationConfig(CombatType.PvE, Name = "Use BossMod IPC for raidwide/stack detection (requires BossModReborn)")]
    public bool UseBossModIPC { get; set; } = true;

    [Range(1, 10, ConfigUnitType.Seconds)]
    [RotationConfig(CombatType.PvE, Name = "BossMod lookahead (seconds) for raidwide/stack prediction")]
    public float BossModLookahead { get; set; } = 5f;

    #endregion

    #region Helper Properties

    private static bool HasFurtherRuin => StatusHelper.PlayerHasStatus(true, StatusID.FurtherRuin_2701);
    private static bool HasIfritFavor => StatusHelper.PlayerHasStatus(true, StatusID.IfritsFavor);
    private static bool HasGarudaFavor => StatusHelper.PlayerHasStatus(true, StatusID.GarudasFavor);
    private static bool HasTitanFavor => StatusHelper.PlayerHasStatus(true, StatusID.TitansFavor);
    private static bool NoPrimalReady => !IsIfritReady && !IsGarudaReady && !IsTitanReady;

    #endregion

    #region Countdown Logic

    protected override IAction? CountDownAction(float remainTime)
    {
        if (SummonCarbunclePvE.CanUse(out IAction? act))
        {
            return act;
        }
        if (HasSummon && remainTime <= RuinPvE.Info.CastTime + CountDownAhead
            && RuinPvE.CanUse(out act))
        {
            return act;
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
        // Addle: nur wenn Ziel castet und noch kein Addle hat
        if (AutoAddle && ShouldUseAddle())
        {
            if (AddlePvE.CanUse(out act))
            {
                return true;
            }
        }

        // Radiant Aegis: Schild vor Raidwide
        if (ProactiveAegis && !IsLastAction(false, RadiantAegisPvE) && RadiantAegisPvE.CanUse(out act, usedUp: true))
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
        if (StatusHelper.PlayerWillStatusEndGCD(3, 0, true, StatusID.RefulgentLux))
        {
            if (LuxSolarisPvE.CanUse(out act))
            {
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

        if (RadiantAegisPvE.Cooldown.CurrentCharges > 0 && !IsLastAction(false, RadiantAegisPvE) && InCombat)
        {
            if (RadiantOnCooldown && (RadiantAegisPvE.Cooldown.CurrentCharges == 2 || (RadiantAegisPvE.Cooldown.CurrentCharges == 1 && RadiantAegisPvE.Cooldown.WillHaveXChargesGCD(2, 1, 0))))
            {
                if (RadiantAegisPvE.CanUse(out act))
                {
                    return true;
                }
            }

            if (RadiantOnCooldownSpam)
            {
                if (RadiantAegisPvE.CanUse(out act))
                {
                    return true;
                }
            }
        }

        return base.GeneralAbility(nextGCD, out act);
    }

    protected override bool AttackAbility(IAction nextGCD, out IAction? act)
    {
        bool inBigInvocation = !SummonBahamutPvE.EnoughLevel || InBahamut || InPhoenix || InSolarBahamut;
        bool inSolarUnique = DataCenter.PlayerSyncedLevel() == 100 ? !InBahamut && !InPhoenix && InSolarBahamut : InBahamut && !InPhoenix;
        bool burstInSolar = (SummonSolarBahamutPvE.EnoughLevel && InSolarBahamut) || (!SummonSolarBahamutPvE.EnoughLevel && InBahamut) || !SummonBahamutPvE.EnoughLevel;

        if (burstInSolar)
        {
            if (SearingLightPvE.CanUse(out act))
            {
                return true;
            }
        }

        if (inBigInvocation)
        {
            if (EnergySiphonPvE.CanUse(out act))
            {
                if ((EnergySiphonPvE.Target.Target.IsBossFromTTK() || EnergySiphonPvE.Target.Target.IsBossFromIcon()) && EnergySiphonPvE.Target.Target.IsDying())
                {
                    return true;
                }
                if (SummonTime > 0f || !SummonBahamutPvE.EnoughLevel)
                {
                    return true;
                }
            }

            if (EnergyDrainPvE.CanUse(out act))
            {
                if ((EnergyDrainPvE.Target.Target.IsBossFromTTK() || EnergyDrainPvE.Target.Target.IsBossFromIcon()) && EnergyDrainPvE.Target.Target.IsDying())
                {
                    return true;
                }
                if (SummonTime > 0f || !SummonBahamutPvE.EnoughLevel)
                {
                    return true;
                }
            }

            if (EnkindleBahamutPvE.CanUse(out act))
            {
                if ((EnkindleBahamutPvE.Target.Target.IsBossFromTTK() || EnkindleBahamutPvE.Target.Target.IsBossFromIcon()) && EnkindleBahamutPvE.Target.Target.IsDying())
                {
                    return true;
                }
                if (SummonTime > 0f || !SummonBahamutPvE.EnoughLevel)
                {
                    return true;
                }
            }

            if (EnkindleSolarBahamutPvE.CanUse(out act))
            {
                if ((EnkindleSolarBahamutPvE.Target.Target.IsBossFromTTK() || EnkindleSolarBahamutPvE.Target.Target.IsBossFromIcon()) && EnkindleSolarBahamutPvE.Target.Target.IsDying())
                {
                    return true;
                }
                if (SummonTime > 0f || !SummonBahamutPvE.EnoughLevel)
                {
                    return true;
                }
            }

            if (EnkindlePhoenixPvE.CanUse(out act))
            {
                if ((EnkindlePhoenixPvE.Target.Target.IsBossFromTTK() || EnkindlePhoenixPvE.Target.Target.IsBossFromIcon()) && EnkindlePhoenixPvE.Target.Target.IsDying())
                {
                    return true;
                }
                if (SummonTime > 0f || !SummonBahamutPvE.EnoughLevel)
                {
                    return true;
                }
            }

            if (DeathflarePvE.CanUse(out act))
            {
                if ((DeathflarePvE.Target.Target.IsBossFromTTK() || DeathflarePvE.Target.Target.IsBossFromIcon()) && DeathflarePvE.Target.Target.IsDying())
                {
                    return true;
                }
                if (SummonTime > 0f || !SummonBahamutPvE.EnoughLevel)
                {
                    return true;
                }
            }

            if (SunflarePvE.CanUse(out act))
            {
                if ((SunflarePvE.Target.Target.IsBossFromTTK() || SunflarePvE.Target.Target.IsBossFromIcon()) && SunflarePvE.Target.Target.IsDying())
                {
                    return true;
                }
                if (SummonTime > 0f || !SummonBahamutPvE.EnoughLevel)
                {
                    return true;
                }
            }

            if (SearingFlashPvE.CanUse(out act))
            {
                if ((SearingFlashPvE.Target.Target.IsBossFromTTK() || SearingFlashPvE.Target.Target.IsBossFromIcon()) && SearingFlashPvE.Target.Target.IsDying())
                {
                    return true;
                }
                if (SummonTime > 0f || !SummonBahamutPvE.EnoughLevel)
                {
                    return true;
                }
            }
        }

        if (MountainBusterPvE.CanUse(out act))
        {
            return true;
        }

        if (PainflarePvE.CanUse(out act))
        {
            if ((inSolarUnique && HasSearingLight) || !SearingLightPvE.EnoughLevel)
            {
                return true;
            }
            if ((PainflarePvE.Target.Target.IsBossFromTTK() || PainflarePvE.Target.Target.IsBossFromIcon()) && PainflarePvE.Target.Target.IsDying())
            {
                return true;
            }
        }

        if (NecrotizePvE.CanUse(out act))
        {
            if ((inSolarUnique && HasSearingLight) || !SearingLightPvE.EnoughLevel)
            {
                return true;
            }
            if ((NecrotizePvE.Target.Target.IsBossFromTTK() || NecrotizePvE.Target.Target.IsBossFromIcon()) && NecrotizePvE.Target.Target.IsDying())
            {
                return true;
            }
            if (EnergyDrainPvE.Cooldown.WillHaveOneChargeGCD(2))
            {
                return true;
            }
        }

        if (FesterPvE.CanUse(out act))
        {
            if ((inSolarUnique && HasSearingLight) || !SearingLightPvE.EnoughLevel)
            {
                return true;
            }
            if ((FesterPvE.Target.Target.IsBossFromTTK() || FesterPvE.Target.Target.IsBossFromIcon()) && FesterPvE.Target.Target.IsDying())
            {
                return true;
            }
            if (EnergyDrainPvE.Cooldown.WillHaveOneChargeGCD(2))
            {
                return true;
            }
        }

        if (SearingFlashPvE.CanUse(out act))
        {
            if ((SearingFlashPvE.Target.Target.IsBossFromTTK() || SearingFlashPvE.Target.Target.IsBossFromIcon()) && SearingFlashPvE.Target.Target.IsDying())
            {
                return true;
            }
        }
        return base.AttackAbility(nextGCD, out act);
    }

    protected override bool EmergencyAbility(IAction nextGCD, out IAction? act)
    {
        // Höchste Priorität: Raidwide/Stack erkannt → Addle + Schild SOFORT
        // EmergencyAbility wird VOR allen anderen oGCDs aufgerufen (höchste Priorität im Framework)
        bool damageImminent = IsDamageImminent();

        if (damageImminent)
        {
            // Addle zuerst: reduziert den eingehenden Schaden
            if (AutoAddle && ShouldUseAddle())
            {
                if (AddlePvE.CanUse(out act))
                {
                    return true;
                }
            }

            // Radiant Aegis: Schild aktivieren bevor der Schaden eintrifft
            if (ProactiveAegis && !IsLastAction(false, RadiantAegisPvE) && RadiantAegisPvE.CanUse(out act))
            {
                return true;
            }
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
        if ((!InSolarBahamut && SBRaise) || !SBRaise)
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

        if ((HasSearingLight || SearingLightPvE.Cooldown.IsCoolingDown) && SummonBahamutPvE.CanUse(out act))
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
            if (IsMoving && HasFurtherRuin && SummonTimeEndAfterGCD() && AttunmentTimeEndAfterGCD()
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
        if (!InBahamut && !InPhoenix && !InSolarBahamut && SummonTimeEndAfterGCD() && AttunmentTimeEndAfterGCD())
        {
            if (SmartRuinIV && HasFurtherRuin)
            {
                // Prüfe ob genug Zeit für Ruin III + GCD bis große Beschwörung bereit ist
                // Ruin III hat ~2.5s Cast. Wenn Bahamut/etc. in weniger als 1 GCD bereit ist,
                // würde Ruin III die große Beschwörung zu lange verzögern → Ruin IV nehmen
                float bigSummonRemain = BigSummonCooldownRemain();
                float threshold = BigSummonDelayThreshold / 1000f;
                bool bigSummonSoon = bigSummonRemain <= GCDTime(1) + threshold && NoPrimalReady;

                if (IsMoving || bigSummonSoon)
                {
                    // Ruin IV: instant, keine Verzögerung der großen Beschwörung
                    if (RuinIvPvE.CanUse(out act, skipAoeCheck: true)) return true;
                }
                else
                {
                    // Genug Zeit: Ruin III bevorzugen, Ruin IV aufsparen
                    if (RuinIiiPvE.EnoughLevel && RuinIiiPvE.CanUse(out act)) return true;
                    if (!RuinIiiPvE.Info.EnoughLevelAndQuest() && RuinIiPvE.EnoughLevel && RuinIiPvE.CanUse(out act)) return true;
                    if (!RuinIiPvE.Info.EnoughLevelAndQuest() && RuinPvE.CanUse(out act)) return true;
                    // Fallback auf Ruin IV wenn Ruin III nicht verfügbar
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
    /// Dynamische Egi-Auswahl: Vermeidet Ifrit (Cast-Zeiten) bei Bewegung.
    /// Priorisiert Titan (Instant) und Garuda (1 Cast + Instants) wenn in Bewegung.
    /// </summary>
    private bool DynamicPrimalSelection(out IAction? act)
    {
        act = null;

        if (IsMoving)
        {
            // Moving: Titan first (all instant), then Garuda (mostly instant), Ifrit last
            if (TitanTime(out act)) return true;
            if (GarudaTime(out act)) return true;
            if (IfritTime(out act)) return true;
        }
        else
        {
            // Stationary: Ifrit first (highest potency with casts), then Titan, then Garuda
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
    /// </summary>
    private bool IsDamageImminent()
    {
        // BossMod IPC: präzise Vorhersage von Raidwides und Stacks
        if (UseBossModIPC && BossModHints_IPCSubscriber.IsEnabled)
        {
            try
            {
                if (BossModHints_IPCSubscriber.Hints_IsRaidwideImminent?.Invoke(BossModLookahead) == true)
                    return true;
                if (BossModHints_IPCSubscriber.Hints_IsSharedImminent?.Invoke(BossModLookahead) == true)
                    return true;
            }
            catch
            {
                // IPC-Fehler: Fallback auf RSR-Erkennung
            }
        }

        // Fallback: RSR-eigene VFX/Cast-basierte Erkennung
        return DataCenter.IsHostileCastingAOE;
    }

    /// <summary>
    /// Prüft ob Addle sinnvoll eingesetzt werden kann:
    /// - Nutzt BossMod IPC für präzise Raidwide-Erkennung wenn verfügbar
    /// - Eingehender Schaden muss MAGICAL sein
    /// - Ziel hat noch kein Addle
    /// - Bei Raidwides: immer Addle
    /// - Bei Stacks: nur wenn Addle-CD es erlaubt
    /// </summary>
    private bool ShouldUseAddle()
    {
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

        // BossMod IPC: präzise Unterscheidung Raidwide vs Stack
        if (UseBossModIPC && BossModHints_IPCSubscriber.IsEnabled)
        {
            try
            {
                bool raidwide = BossModHints_IPCSubscriber.Hints_IsRaidwideImminent?.Invoke(BossModLookahead) == true;
                if (raidwide)
                    return true; // Raidwide: Addle immer

                bool shared = BossModHints_IPCSubscriber.Hints_IsSharedImminent?.Invoke(BossModLookahead) == true;
                if (shared && !AddlePvE.Cooldown.IsCoolingDown)
                    return true; // Stack: nur wenn Addle nicht auf CD
            }
            catch
            {
                // Fallback auf RSR-Erkennung
            }
        }

        // Fallback: RSR-eigene Erkennung
        bool isRaidwideCast = IsAnyHostileCastingKnownRaidwide();
        if (isRaidwideCast)
            return true;

        if (DataCenter.IsHostileCastingAOE && !AddlePvE.Cooldown.IsCoolingDown)
            return true;

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

    #region Big Summon Helpers

    /// <summary>
    /// Gibt die verbleibende Cooldown-Zeit bis zur nächsten großen Beschwörung zurück.
    /// Prüft Bahamut, Phoenix und Solar Bahamut und gibt die kürzeste Zeit zurück.
    /// </summary>
    private float BigSummonCooldownRemain()
    {
        if (SummonBahamutPvE.EnoughLevel)
        {
            return SummonBahamutPvE.Cooldown.RecastTimeRemainOneCharge;
        }
        if (DreadwyrmTrancePvE.EnoughLevel)
        {
            return DreadwyrmTrancePvE.Cooldown.RecastTimeRemainOneCharge;
        }
        return float.MaxValue;
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