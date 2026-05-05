using Dalamud.Interface.Colors;
using ECommons.GameFunctions;
using System.ComponentModel;
using CombatRole = ECommons.GameFunctions.CombatRole;


namespace RotationSolver.ExtraRotations.Ranged;

[Rotation("Churin DNC", CombatType.PvE, GameVersion = "7.5",
	Description =
		"Candles lit, runes drawn upon the floor, sacrifice prepared. Everything is ready for the summoning. I begin the incantation: \"Shakira, Shakira!\"")]
[SourceCode(Path = "main/ExtraRotations/Ranged/ChurinDNC.cs")]
[ExtraRotation]

public sealed class ChurinDNC : DancerRotation
{
    #region Enums

    private enum HoldStrategy
    {
        [Description("Hold Step only if no targets in range")]
        HoldStepOnly,

        [Description("Hold Finish only if no targets in range")]
        HoldFinishOnly,

        [Description("Hold Step and Finish if no targets in range")]
        HoldStepAndFinish,

        [Description("Don't hold Step and Finish if no targets in range")]
        DontHoldStepAndFinish
    }

    private enum DancerOpener
    {
        [Description("Standard Opener")]
        Standard,
        [Description("Tech Opener")]
        Tech,
    }

#endregion

    #region Properties

    #region Constants

    private const int SaberDanceEspritCost = 50;
    private const int HighEspritThreshold = 90;
    private const int MidEspritThreshold = 70;
    private const int DanceTargetRange = 15;

    #endregion

    #region Tracking

    public override void DisplayRotationStatus()
    {
        ImGui.Text($"Weapon Total: {WeaponTotal}");
        ImGui.Text($"Tech Hold Strategy: {TechHoldStrategy}");
        ImGui.Text($"Can Use Step Hold Check for Technical Step: {CanUseStepHoldCheck(TechHoldStrategy)}");
        ImGui.Text($"Standard Hold Strategy: {StandardHoldStrategy}");
        ImGui.Text($"Can Use Step Hold Check for Standard Step: {CanUseStepHoldCheck(StandardHoldStrategy)}");
        ImGui.Text($"Potion Usage Enabled: {PotionUsageEnabled}");
        ImGui.Text($"Potion Usage Presets: {PotionUsagePresets}");
        ImGui.Text($"Can Use Technical Step: {CanUseTechnicalStep} - Tech Step Ready?: {_techStepReady}");
        ImGui.Text($"Can Use Standard Step: {CanUseStandardStep} - Standard Step Ready?: {_standardReady}");
        ImGui.Text($"Saber Dance Primed?: {_saberDancePrimed}");
        ImGui.Text($"Completed Steps: {CompletedSteps}");
        ImGui.Text($"Potion Condition Met: {ChurinPotions.IsConditionMet()} | Can Use At Time: {ChurinPotions.CanUseAtTime()}");
    }

    #endregion

    #region Status Booleans

    private static bool HasTillana => StatusHelper.PlayerHasStatus(true, StatusID.FlourishingFinish) && !StatusHelper.PlayerWillStatusEnd(0, true, StatusID.FlourishingFinish);
    private static bool IsBurstPhase => HasDevilment && HasTechnicalFinish;
    private static bool IsMedicated => StatusHelper.PlayerHasStatus(true, StatusID.Medicated) && !StatusHelper.PlayerWillStatusEnd(0, true, StatusID.Medicated);
    private static bool HasAnyProc => StatusHelper.PlayerHasStatus(true, StatusID.SilkenFlow, StatusID.SilkenSymmetry, StatusID.FlourishingFlow, StatusID.FlourishingSymmetry);
    private static bool HasFinishingMove => StatusHelper.PlayerHasStatus(true, StatusID.FinishingMoveReady) && !StatusHelper.PlayerWillStatusEnd(0, true, StatusID.FinishingMoveReady);
    private static bool HasStarfall => HasFlourishingStarfall && !StatusHelper.PlayerWillStatusEnd(0, true, StatusID.FlourishingStarfall);

    private static bool AreDanceTargetsInRange
    {
        get
        {
            return AllHostileTargets.Any(target => target.DistanceToPlayer() <= DanceTargetRange);
        }
    }

    private static bool ShouldSwapDancePartner => CurrentDancePartner != null && (CurrentDancePartner.HasStatus(false, StatusID.Weakness, StatusID.DamageDown, StatusID.BrinkOfDeath, StatusID.DamageDown_2911) || CurrentDancePartner.IsDead);

    #endregion

    #region Conditionals

    private bool ShouldUseTechStep => TechnicalStepPvE.IsEnabled && TechnicalStepPvE.EnoughLevel  && MergedStatus.HasFlag(AutoStatus.Burst);
    private bool ShouldUseStandardStep => StandardStepPvE.IsEnabled && StandardStepPvE.EnoughLevel &&!HasLastDance;
    private bool ShouldUseFinishingMove => FinishingMovePvE.IsEnabled && FinishingMovePvE.EnoughLevel && !HasLastDance;

    private bool StandardSoon => (StandardStepPvE.Cooldown.WillHaveOneCharge(5)
                                  || HasFinishingMove && FinishingMovePvE.Cooldown.WillHaveOneCharge(5)
                                  || StandardStepPvE.Cooldown.HasOneCharge
                                  || HasFinishingMove && FinishingMovePvE.Cooldown.HasOneCharge) && CanUseStandardStep;

    private bool CanUseStandardBasedOnEsprit
    {
        get
        {
            if (!HasTechnicalFinish)
            {
                return Esprit <= HighEspritThreshold || !_saberDancePrimed;
            }

            if (DisableStandardInBurstCheck)
            {
                return Esprit < HighEspritThreshold || !_saberDancePrimed;
            }
            return false;
        }
    }

    private bool DisableStandardInBurstCheck
    {
        get
        {
            if (!HasTechnicalFinish || !DisableStandardInBurst)
            {
                return true;
            }

            return HasFinishingMove || !FinishingMovePvE.EnoughLevel;
        }
    }

    private bool CanUseStepHoldCheck(HoldStrategy strategy)
    {
        var isTech = strategy == TechHoldStrategy;
        var isStandard = strategy == StandardHoldStrategy;

        if (!isTech && !isStandard) return false;

        var shouldHoldStep = isTech
            ? strategy is HoldStrategy.HoldStepOnly && !HasTillana && !HasTechnicalStep
            : strategy is HoldStrategy.HoldStepOnly && !HasStandardStep && !HasFinishingMove;

        var shouldHoldFinish = isTech
            ? strategy is HoldStrategy.HoldFinishOnly && (HasTillana || HasTechnicalStep)
            : strategy is HoldStrategy.HoldFinishOnly && (HasFinishingMove || HasStandardStep);

        return strategy switch
        {
            HoldStrategy.DontHoldStepAndFinish => true,
            HoldStrategy.HoldStepAndFinish => AreDanceTargetsInRange,
            _ when shouldHoldStep || shouldHoldFinish => AreDanceTargetsInRange,
            _ => true,
        };
    }

    private bool _techStepReady;
    private bool _standardReady;

    private bool CanUseTechnicalStep
    {
        get
        {
            var technicalRemain = TechnicalStepPvE.Cooldown.RecastTimeRemain;
            var devilmentRemain = DevilmentPvE.Cooldown.RecastTimeRemain;
            var noFinishBuff = StandardStepPvE.CanUse(out _) && !HasStandardFinish;

            if (!ShouldUseTechStep
                || IsDancing && HasTechnicalStep
                || HasTillana
                || noFinishBuff
                || devilmentRemain - WeaponTotal >= 7f)
            {
                _techStepReady = false;
                return false;
            }

            if (TechnicalStepPvE.Cooldown.IsCoolingDown)
            {
                if (technicalRemain <= WeaponTotal && WeaponElapsed <= 1f)
                {
                    _techStepReady = true;
                }
            }

            if (TechnicalStepPvE.CanUse(out _) && !HasTillana)
            {
                _techStepReady = true;
            }

            return _techStepReady && CanUseStepHoldCheck(TechHoldStrategy);
        }
    }

    private bool CanUseStandardStep
    {
        get
        {
            var standardRemain = StandardStepPvE.Cooldown.RecastTimeRemain;
            var finishingRemain = FinishingMovePvE.Cooldown.RecastTimeRemain;
            var standardDisabled = !ShouldUseStandardStep && !HasFinishingMove;
            var finishingDisabled = !ShouldUseFinishingMove && HasFinishingMove;
            var noFinish = InCombat && HasStandardFinish && ShouldUseTechStep &&
                           TechnicalStepPvE.Cooldown.WillHaveOneCharge(5) && !HasTillana;

            if (IsDancing
                || standardDisabled
                || finishingDisabled
                || noFinish
                || !CanUseStandardBasedOnEsprit)
            {
                _standardReady = false;
                return false;
            }

            if (!HasFinishingMove && StandardStepPvE.Cooldown.IsCoolingDown
                || HasFinishingMove && FinishingMovePvE.Cooldown.IsCoolingDown)
            {
                if ((standardRemain <= WeaponTotal || finishingRemain <= WeaponTotal)  && (WeaponElapsed <= 0.5f || WeaponRemain >= 2f))
                {
                    _standardReady = true;
                }
            }

            if (!HasFinishingMove && StandardStepPvE.CanUse(out _)
                || HasFinishingMove && FinishingMovePvE.CanUse(out _))
            {
                _standardReady = true;
            }

            return _standardReady && CanUseStepHoldCheck(StandardHoldStrategy);
        }
    }

    private bool _saberDancePrimed;

    private void IsSaberDancePrimed()
    {
        var willHaveOneCharge = StandardStepPvE.Cooldown.WillHaveOneCharge(5);

        if ((IsLastGCD(ActionID.SaberDancePvE, ActionID.DanceOfTheDawnPvE)
        && Esprit < SaberDanceEspritCost)
        || Esprit < SaberDanceEspritCost)
        {
            _saberDancePrimed = false;
            return;
        }

        if (WeaponRemain < DataCenter.CalculatedActionAhead) return;

        if (IsBurstPhase)
        {
            if (willHaveOneCharge)
            {
                if (HasLastDance)
                {
                    _saberDancePrimed = Esprit >= HighEspritThreshold;
                    return;
                }

                if (StandardStepPvE.Cooldown.RecastTimeRemain < WeaponTotal)
                {
                    _saberDancePrimed = Esprit >= HighEspritThreshold && !HasLastDance;
                    return;
                }

                _saberDancePrimed = Esprit >= SaberDanceEspritCost
                                    && !StatusHelper.PlayerWillStatusEnd(7f, true, StatusID.FlourishingStarfall);
                return;
            }

            if (Esprit >= SaberDanceEspritCost)
            {
                _saberDancePrimed = true;
                return;
            }

            _saberDancePrimed = false;
            return;
        }

        if (Esprit >= MidEspritThreshold || IsMedicated && Esprit >= SaberDanceEspritCost)
        {
            _saberDancePrimed = true;
            return;
        }

        _saberDancePrimed = false;
    }

    #endregion

    #endregion

    #region Config Options

    [RotationConfig(CombatType.PvE, Name = "Technical Step, Technical Finish & Tillana Hold Strategy")]
    private HoldStrategy TechHoldStrategy { get; set; } = HoldStrategy.HoldStepAndFinish;

    [RotationConfig(CombatType.PvE, Name = "Standard Step, Standard Finish & Finishing Move Hold Strategy")]
    private HoldStrategy StandardHoldStrategy { get; set; } = HoldStrategy.HoldStepAndFinish;

    [RotationConfig(CombatType.PvE, Name = "Select an opener")]
    private DancerOpener ChosenOpener { get; set; } = DancerOpener.Standard;

    [Range(0,16, ConfigUnitType.Seconds, 0)]
    [RotationConfig(CombatType.PvE, Name = "How many seconds before combat starts to use Standard Step?",
        Parent = nameof(ChosenOpener), ParentValue = "Standard Opener")]
    private float OpenerStandardStepTime { get; set; } = 15.5f;

    [Range(0, 1, ConfigUnitType.Seconds, 0)]
    [RotationConfig(CombatType.PvE, Name = "How many seconds before combat starts to use Standard Finish?",
        Parent = nameof(ChosenOpener), ParentValue = "Standard Opener")]
    private float OpenerFinishTime { get; set; } = 0.5f;

    [Range(0, 16, ConfigUnitType.Seconds, 0)]
    [RotationConfig(CombatType.PvE, Name = "How many seconds before combat starts to use Technical Step?",
        Parent = nameof(ChosenOpener), ParentValue = "Tech Opener", Tooltip = "If countdown is set above 13 seconds, it will start with Standard Step before initiating Tech Step, please go out of range of any enemies before the cd reaches your configured time")]
    private float OpenerTechTime { get; set; } = 7f;

    [Range(0, 1, ConfigUnitType.Seconds, 0)]
    [RotationConfig(CombatType.PvE, Name = "How many seconds before combat starts to use Technical Finish?",
        Parent = nameof(ChosenOpener), ParentValue = "Tech Opener")]
    private float OpenerTechFinishTime { get; set; } = 0.5f;

    [RotationConfig(CombatType.PvE, Name = "Disable Standard Step in Burst")]
    private bool DisableStandardInBurst { get; set; } = true;

    private static readonly ChurinDNCPotions ChurinPotions = new();

    [RotationConfig(CombatType.PvE, Name = "Enable Potion Usage")]
    private static bool PotionUsageEnabled
    { get => ChurinPotions.Enabled; set => ChurinPotions.Enabled = value; }

    [RotationConfig(CombatType.PvE, Name = "Potion Usage Presets", Parent = nameof(PotionUsageEnabled))]
    private static PotionStrategy PotionUsagePresets
    { get => ChurinPotions.Strategy; set => ChurinPotions.Strategy = value; }

    [Range(0,20, ConfigUnitType.Seconds, 0)]
    [RotationConfig(CombatType.PvE, Name = "Use Opener Potion at minus (value in seconds)", Parent = nameof(PotionUsageEnabled))]
    private static float OpenerPotionTime { get => ChurinPotions.OpenerPotionTime; set => ChurinPotions.OpenerPotionTime = value; }

    [Range(0, 1200, ConfigUnitType.Seconds, 0)]
    [RotationConfig(CombatType.PvE, Name = "Use 1st Potion at (value in seconds - leave at 0 if using in opener)",
        Parent = nameof(PotionUsagePresets), ParentValue = "Use custom potion timings")]
    private float FirstPotionTiming
    {
        get;
        set
        {
            field = value;
            UpdateCustomTimings();
        }
    }

    [Range(0, 1200, ConfigUnitType.Seconds, 0)]
    [RotationConfig(CombatType.PvE, Name = "Use 2nd Potion at (value in seconds)", Parent = nameof(PotionUsagePresets),
        ParentValue = "Use custom potion timings")]
    private float SecondPotionTiming
    {
        get;
        set
        {
            field = value;
            UpdateCustomTimings();
        }
    }

    [Range(0, 1200, ConfigUnitType.Seconds, 0)]
    [RotationConfig(CombatType.PvE, Name = "Use 3rd Potion at (value in seconds)", Parent = nameof(PotionUsagePresets),
        ParentValue = "Use custom potion timings")]
    private float ThirdPotionTiming
    {
        get;
        set
        {
            field = value;
            UpdateCustomTimings();
        }
    }

    private void UpdateCustomTimings()
    {
        ChurinPotions.CustomTimings = new Potions.CustomTimingsData
        {
            Timings = [FirstPotionTiming, SecondPotionTiming, ThirdPotionTiming]
        };
    }

    #endregion

    #region Main Combat Logic

    #region Countdown Logic

    // Override the method for actions to be taken during the countdown phase of combat
    protected override IAction? CountDownAction(float remainTime)
    {
        if (ChurinPotions.ShouldUsePotion(this, out var potionAct))
        {
            return potionAct;
        }

        if (remainTime > OpenerStandardStepTime)
        {
            return base.CountDownAction(remainTime);
        }

        var act = ChosenOpener switch
        {
            DancerOpener.Standard => CountDownStandardOpener(remainTime),
            DancerOpener.Tech     => CountDownTechOpener(remainTime),
            _                     => null
        };

        return act ?? base.CountDownAction(remainTime);
    }

    private IAction? CountDownStandardOpener(float remainTime)
    {
        if (TryUseClosedPosition(out var act)
            || remainTime <= OpenerStandardStepTime && StandardStepPvE.CanUse(out act)
            || ExecuteStepGCD(out act)
            || remainTime <= OpenerFinishTime && DoubleStandardFinishPvE.CanUse(out act))
        {
            return act;
        }

        return null;
    }

    private IAction? CountDownTechOpener(float remainTime)
    {
        if (TryUseClosedPosition(out var act)
            || remainTime > OpenerTechTime && remainTime > 13 && StandardStepPvE.CanUse(out act)
            || remainTime <= OpenerTechTime && TechnicalStepPvE.CanUse(out act)
            || ExecuteStepGCD(out act)
            || remainTime > OpenerTechTime && IsDancing && HasStandardStep && !AreDanceTargetsInRange &&
            DoubleStandardFinishPvE.CanUse(out act)
            || remainTime <= OpenerTechFinishTime && TryFinishTheDance(out act))
        {
            return act;
        }
        return null;
    }

    #endregion

    #region oGCD Logic

    /// Override the method for handling emergency abilities
    protected override bool EmergencyAbility(IAction nextGCD, out IAction? act)
    {
        IsSaberDancePrimed();
        if (TryUseDevilment(out act)) return true;
        if (SwapDancePartner(out act)) return true;
        if (TryUseClosedPosition(out act)) return true;

        if (!CanUseStandardStep && !CanUseTechnicalStep && !IsDancing)
        {
            return base.EmergencyAbility(nextGCD, out act);
        }

        act = null;
        return false;

    }

    /// Override the method for handling attack abilities
    protected override bool AttackAbility(IAction nextGCD, out IAction? act)
    {
        act = null;
        if (IsDancing || !CanWeave) return false;
        if (TryUseFlourish(out act)) return true;
        return TryUseFeathers(out act)
               || base.AttackAbility(nextGCD, out act);
    }

    #endregion

    #region GCD Logic

    /// Override the method for handling general Global Cooldown (GCD) actions
    protected override bool GeneralGCD(out IAction? act)
    {
        if (ChurinPotions.ShouldUsePotion(this, out act)) return true;

        if (IsDancing)
        {
            return TryFinishTheDance(out act);
        }

        if (TryUseStep(out act))
        {
            return true;
        }

        // During burst phase, prioritize burst GCDs
        if (IsBurstPhase && TryUseBurstGCD(out act))
        {
            return true;
        }

        return TryUseFillerGCD(out act) || base.GeneralGCD(out act);
    }

    #endregion

    #endregion

    #region Extra Methods

    #region Dance Partner Logic

    private bool TryUseClosedPosition(out IAction? act)
    {
        act = null;

        // Already have a dance partner or no party members
        if (StatusHelper.PlayerHasStatus(true, StatusID.ClosedPosition)
            || !PartyMembers.Any()
            || !ClosedPositionPvE.IsEnabled)
        {
            return false;
        }

        return ClosedPositionPvE.CanUse(out act);
    }

    private bool SwapDancePartner(out IAction? act)
    {
        act = null;
        if (!StatusHelper.PlayerHasStatus(true, StatusID.ClosedPosition)
        || !ShouldSwapDancePartner
        || !ClosedPositionPvE.IsEnabled)
        {
            return false;
        }

		return IsSaberDancePrimed && SaberDancePvE.CanUse(out act);
	}

	private bool TryUseProcs(out IAction? act)
	{
		act = null;

		if (IsBurstPhase || !ShouldUseTechStep || CanUseTechStep) return false;

		var gcdsUntilTech = 0;
		for (var i = 1; i <= 5; i++)
		{
			if (!TechnicalStepPvE.Cooldown.WillHaveOneChargeGCD((uint)i, 0.5f)) continue;
			gcdsUntilTech = i;
			break;
		}

		if (gcdsUntilTech == 0 || Showtime) return false;

		switch (gcdsUntilTech)
		{
			case 5:
			case 4:
			case 3:
				return IsSaberDancePrimed ? TryUseSaberDance(out act) : TryUseBasicGCD(out act);
			case 2:
			case 1:
				if (HasAnyProc) return TryUseBasicGCD(out act);
				if (CanSaberDance) return SaberDancePvE.CanUse(out act);
				if (HasLastDance) return LastDancePvE.CanUse(out act);
				break;
		}

		return false;
	}

	#endregion

	#endregion

	#region oGCD Abilities

	#region Burst oGCDs

	private bool TryUseDevilment(out IAction? act)
	{
		act = null;
		var canUseTech = TechnicalStepPvE.EnoughLevel && (HasTechnicalFinish
														  || IsLastGCD(ActionID.QuadrupleTechnicalFinishPvE));

		var cantUseTech = !TechnicalStepPvE.EnoughLevel &&
						  (HasStandardFinish || IsLastGCD(ActionID.DoubleStandardFinishPvE));

		if (!DevilmentPvE.EnoughLevel || DevilmentPvE.Cooldown.IsCoolingDown || HasDevilment) return false;

		if (!canUseTech && !cantUseTech) return false;

		act = DevilmentPvE;
		return true;
	}

	private bool TryUseFlourish(out IAction? act)
	{
		act = null;

		if (HasThreefoldFanDance || !EnoughWeaveTime || IsDancing) return false;

		if (!FlourishPvE.CanUse(out act)) return false;

		if (IsBurstPhase) return true;

		if (CanStandardFinish || CanTechnicalFinish) return false;

		if (!ShouldUseTechStep) return true;

		return TechnicalStepPvE.Cooldown.IsCoolingDown
			   && !TechnicalStepPvE.Cooldown.WillHaveOneCharge(35);
	}

	#endregion

	#region Feathers

	private bool TryUseFeatherProcs(out IAction? act)
	{
		act = null;
		if (!HasFeatherProcs) return false;

		if (!EnoughWeaveTime) return false;

		return (HasThreefoldFanDance && FanDanceIiiPvE.CanUse(out act))
			   || (HasFourfoldFanDance && FanDanceIvPvE.CanUse(out act));
	}

	private bool TryUseFeathers(out IAction? act)
	{
		act = null;
		if (Feathers <= 0 || !EnoughWeaveTime) return false;

		var overcapRisk = HasEnoughFeathers && (HasAnyProc || FlourishPvE.Cooldown.WillHaveOneChargeGCD(1)) &&
						  !CanUseTechStep;

		var medicatedOutsideBurst = IsMedicated
									&& !TechnicalStepPvE.Cooldown.WillHaveOneCharge(30)
									&& ShouldUseTechStep;

		var shouldDumpFeathers = IsBurstPhase || overcapRisk || medicatedOutsideBurst;


		return shouldDumpFeathers && (FanDanceIiPvE.CanUse(out act)
									  || FanDancePvE.CanUse(out act));
	}

	#endregion

	#region Dance Partner

	private bool TryUseClosedPosition(out IAction? act)
	{
		act = null;
		if (HasClosedPosition
			|| IsDancing
			|| !HasAvailableDancePartner(RestrictDPTarget))
			return false;

		return ClosedPositionPvE.CanUse(out act);
	}

	private bool SwapDancePartner(out IAction? act)
	{
		act = null;
		if (!HasClosedPosition
			|| !ShouldSwapDancePartner
			|| !ClosedPositionPvE.IsEnabled
			|| IsDancing)
			return false;
		return EndingPvE.CanUse(out act);
	}

	#endregion

	#endregion

	#endregion

	#region Potions

	/// <summary>
	/// DNC-specific potion manager that extends base potion logic with job-specific conditions.
	/// </summary>
	private class ChurinDNCPotions : Potions
	{
		private static bool IsOddMinuteWindow(float timing)
		{
			var minute = (int)(timing / 60f);
			return minute % 2 == 1;
		}

		public override bool IsConditionMet()
		{
			if (!IsDancing && !InCombat) return false;

			var timing = GetTimingsArray();
			if (timing.Length == 0) return false;

			return PotsDuringStep switch
			{
				PotsDuringStepStrategy.BeforeStep => HasTechnicalStep || HasStandardStep,
				PotsDuringStepStrategy.AfterStep => CanTechnicalFinish || CanStandardFinish,
				_ => false
			};
		}

		protected override bool IsTimingValid(float timing)
		{
			var lateTiming = DataCenter.CombatTimeRaw >= timing;
			var lateTimingDiff = DataCenter.CombatTimeRaw - timing;

			const float earlyTimingWindow = 15f;

			if (timing > 0)
			{
				var timingDiff = MathF.Abs(DataCenter.CombatTimeRaw - timing);

				switch (ChosenOpener)
				{
					case DancerOpener.Standard:
					default:
						{
							if (!IsOddMinuteWindow(timing)) return lateTiming && lateTimingDiff <= TimingWindowSeconds;

							// Odd-minute special handling: allow both sides within earlyTimingWindow.
							return timingDiff <= earlyTimingWindow;
						}

					case DancerOpener.Tech:
						{
							return timingDiff <= earlyTimingWindow;
						}
				}
			}

			// Check opener timing: OpenerPotionTime == 0 means disabled
			var countDown = Service.CountDownTime;

			if (!IsOpenerPotion(timing)) return false;
			if (ChurinDNC.OpenerPotionTime == 0f) return false;
			return countDown > 0f && countDown <= ChurinDNC.OpenerPotionTime;
		}
	}

	private void UpdateCustomTimings()
	{
		ChurinPotions.CustomTimings = new Potions.CustomTimingsData
		{
			Timings = [FirstPotionTiming, SecondPotionTiming, ThirdPotionTiming]
		};
	}

	#endregion

	#region Debug Tracking

	public override void DisplayRotationStatus()
	{
		if (ImGui.CollapsingHeader("Core"))
		{
			ValueRow("Weapon Total", $"{WeaponTotal:F2}");
			ValueRow("Completed Steps", CompletedSteps);
			ValueRow("Esprit", Esprit);
			ValueRow("Feathers", Feathers);

			ColoredTextRow("Is Burst Phase", IsBurstPhase);
			ColoredTextRow("Is Dancing", IsDancing);
			ColoredTextRow("Can Weave", CanWeave);
		}

		if (ImGui.CollapsingHeader("Step Logic"))
		{
			ValueRow("Tech Hold Strategy", TechHoldStrategy);
			BoolRow("Tech Hold Check", CanUseStepHoldCheck(TechHoldStrategy));

			if (ImGui.TreeNode("Technical Step Blocking Reasons"))
			{
				var canUseTechStep = CanUseTechStep;
				ColoredTextRow("Can Use Technical Step", canUseTechStep);
				ColoredTextRow("Should Use Tech Step", ShouldUseTechStep);
				ColoredTextRow("Is Dancing", IsDancing);
				ColoredTextRow("Has Tillana", HasTillana);
				ColoredTextRow("Has To Refresh Standard", HasToRefreshStandardFinish);
				ColoredTextRow("Devilment Ready", DevilmentReady);
				ColoredTextRow("Timing OK", IsTimingOk(TechnicalRecastRemain, TechnicalStepPvE));
				ImGui.TreePop();
			}

			ImGui.Separator();

			ValueRow("Standard Hold Strategy", StandardHoldStrategy);
			BoolRow("Standard Hold Check", CanUseStepHoldCheck(StandardHoldStrategy));
			ValueRow("Esprit Threshold", EspritThreshold);
			ValueRow("Current Esprit", Esprit);
			if (ImGui.TreeNode("Standard Step Blocking Reasons"))
			{
				var canUseStandard = CanUseActiveStandard;
				ColoredTextRow("Can Use Standard Step or Finishing Move", canUseStandard);
				ColoredTextRow("Active Standard Enabled", ActiveStandard.IsEnabled);
				ColoredTextRow("In Burst Phase", IsBurstPhase);
				ColoredTextRow("Can Use Standard In Burst", CanUseStandardStepInBurst);
				ColoredTextRow("Can Use Based On Esprit", CanUseStandardBasedOnEsprit);
				ColoredTextRow("Has Last Dance", HasLastDance);
				ColoredTextRow("Can Spend Esprit Now", CanSpendEspritNow);
				ColoredTextRow("Timing OK", IsTimingOk(ActiveStandardRecastRemain, ActiveStandard));
				ImGui.TreePop();
			}
		}

		if (ImGui.CollapsingHeader("Saber Dance Blocking"))
		{
			var isSaberPrimed = IsSaberDancePrimed;
			ColoredTextRow("Saber Dance Primed", isSaberPrimed);

			if (!isSaberPrimed)
			{
				ImGui.Indent();
				BoolRow("Can Spend Esprit Now", CanSpendEspritNow);
				BoolRow("Can Saber Dance", CanSaberDance);
				BoolRow("Is Last GCD Tillana", IsLastGCD(ActionID.TillanaPvE));
				BoolRow("Active Standard Will Have Charge", ActiveStandardWillHaveCharge);
				BoolRow("Has Last Dance", HasLastDance);
				ImGui.Unindent();
			}

			var showtime = Showtime;
			if (showtime)
			{
				ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
				ImGui.Text("Saber Dance blocked by Showtime (active dance/recent dance action)");
				ImGui.PopStyleColor();
			}
		}

		if (ImGui.CollapsingHeader("Burst / Proc"))
		{
			BoolRow("Saber Dance Primed", IsSaberDancePrimed);
			BoolRow("Has Any Proc", HasAnyProc);
			BoolRow("Has Enough Feathers", HasEnoughFeathers);

			ImGui.Separator();
			BoolRow("TryUseSaberDance - Enough Esprit", Esprit >= SaberDanceEspritCost);
			BoolRow("TryUseSaberDance - Blocked (Tech/Dancing)", CanUseTechStep || IsDancing);
		}

		if (ImGui.CollapsingHeader("Potions"))
		{
			BoolRow("Potion Usage Enabled", PotionUsageEnabled);
			ValueRow("Potion Usage Preset", PotionUsagePresets);
			try
			{
				ColoredTextRow("Potion Condition Met", ChurinPotions.IsConditionMet());
				ColoredTextRow("Potion Can Use At Time", ChurinPotions.CanUseAtTime());
			}
			catch (Exception ex)
			{
				ImGui.Text($"Error evaluating potion conditions: {ex.Message}");
			}
		}

		if (ImGui.CollapsingHeader("Method Checks"))
		{
			ColoredTextRow("GeneralGCD -> Burst Path", IsBurstPhase);
			ColoredTextRow("GeneralGCD -> Step Path", !IsDancing && (CanUseTechStep || CanUseActiveStandard));
			ColoredTextRow("GeneralGCD -> Finish Dance Path", IsDancing);
			ColoredTextRow("GeneralGCD -> Filler Path", !IsBurstPhase && !IsDancing && !CanUseTechStep && !CanUseActiveStandard);
		}

		ImGui.Separator();

		ColoredTextRow("TryUseStep - Can Tech", CanUseTechStep);
		ColoredTextRow("TryUseStep - Can Standard", CanUseActiveStandard);
		ColoredTextRow("TryUseStep - Has Finishing Move", HasFinishingMove);
	}

	private static void BoolRow(string label, bool value)
	{
		ImGui.Text($"{label}: {(value ? "Yes" : "No")}");
	}
	private static void ColoredTextRow(string label, bool value)
	{
		var color = value ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
		ImGui.PushStyleColor(ImGuiCol.Text, color);
		ImGui.Text($"{label}: {(value ? "Yes" : "No")}");
		ImGui.PopStyleColor();
	}
	private static void ValueRow<T>(string label, T value)
	{
		if (value == null)
		{
			ImGui.Text($"{label}: N/A");
			return;
		}

		ImGui.Text($"{label}: {value}");
	}

	#endregion

}