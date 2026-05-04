using System.ComponentModel;
using Dalamud.Interface.Colors;
using ECommons.GameFunctions;
using CombatRole = ECommons.GameFunctions.CombatRole;


namespace RotationSolver.ExtraRotations.Ranged;

[Rotation("Churin DNC", CombatType.PvE, GameVersion = "7.4",
	Description =
		"Candles lit, runes drawn upon the floor, sacrifice prepared. Everything is ready for the summoning. I begin the incantation: \"Shakira, Shakira!\"")]
[SourceCode(Path = "main/ExtraRotations/Ranged/ChurinDNC.cs")]
[ExtraRotation]

public sealed class ChurinDNC : DancerRotation
{
    #region Properties

    #region Enums
	/// <summary>
    /// Defines strategies for holding dance steps and finishes based on target presence and type.
    /// Each strategy determines whether to hold Step and/or Finish actions when no targets are in range, with specific conditions for Technical and Standard steps.
    /// </summary>
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

	///<summary>
	///Defines the available opener strategies for Dancer
	///</summary>
    public enum DancerOpener
    {
        [Description("Standard Opener")] Standard,
        [Description("Tech Opener")] Tech
    }

	///<summary>
	///Defines when to use potions in relation to dance steps during combat, allowing for strategic timing of potion effects either before or after executing dance steps.
	///</summary>
    private enum PotsDuringStepStrategy
    {
        [Description("Use potion before dance steps, right after Tech/Standard step is used")]
        BeforeStep,

        [Description("Use potion after dance steps, when the step finish is ready")]
        AfterStep
    }

    #endregion

    #region Constants

    private const int SaberDanceEspritCost = 50;
    private const int RiskyEspritThreshold = 40;
    private const int HighEspritThreshold = 80;
    private const int MidEspritThreshold = 70;
    private const int MaxEsprit = 100;
    private const int SafeEspritThreshold = 30;
    private const float DanceTargetRange = 15f;
    private const float DanceAllyRange = 30f;
    private const float MedicatedDuration = 30f;
    private const float SecondsToCompleteTech = 7f;
    private const float SecondsToCompleteStandard = 5f;
    private const float EstimatedAnimationLock = 0.6f;

    #endregion

    #region Player Status Checks

    /// <summary>
    /// Defines an array of StatusIDs that represent negative conditions (such as Weakness, Damage Down, Brink of Death) that would make a party member an invalid dance partner.
    /// </summary>
    private static readonly StatusID[] HasWeaknessOrDamageDown =
        [StatusID.Weakness, StatusID.DamageDown, StatusID.BrinkOfDeath, StatusID.DamageDown_2911];

    /// <summary>
    /// Defines arrays of StatusIDs that represent the procs for Silken Flow/Symmetry and Flourishing Flow/Symmetry, which are important for determining when to use certain dance finishers and procs during combat.
    /// </summary>
    private static readonly StatusID[] SilkenProcs = [StatusID.SilkenFlow, StatusID.SilkenSymmetry];
    private static readonly StatusID[] FlourishingProcs = [StatusID.FlourishingFlow, StatusID.FlourishingSymmetry];

    /// <summary>
    /// Checks if the player currently has any active status from the provided array of StatusIDs,
    /// and ensures that the status is not about to expire,
    /// </summary>
    /// <param name="id">
    /// An array of StatusIDs to check for active status on the player.
    /// The method will return true if the player has any of these statuses active,
    /// and they are not about to expire; otherwise, it will return false.
    /// </param>
    /// <returns>
    /// True if the player has any of the specified statuses active, and they are not about to expire; otherwise, false.
    /// </returns>
    private static bool HasActiveStatus(StatusID[] id)
    {
        return StatusHelper.PlayerHasStatus(true, id) && !StatusHelper.PlayerWillStatusEnd(0, true, id);
    }
    /// <summary>
    /// Checks if the player currently has an active status from the provided StatusID,
    /// </summary>
    /// <param name="id">
    /// A StatusID to check for active status on the player.
    /// </param>
    /// <returns>
    /// True if the player has the specified status active, and it is not about to expire; otherwise, false.
    /// </returns>
    private static bool HasActiveStatus(StatusID id)
    {
        return StatusHelper.PlayerHasStatus(true, id) && !StatusHelper.PlayerWillStatusEnd(0, true, id);
    }

    /// <summary>
	/// Checks if player can execute the requisite Dancer burst skills
    /// by verifying if they have the necessary level or if they are in a low-level burst scenario, and ensuring they have Devilment ready.
    /// </summary>
    private bool IsBurstPhase => ((HasEnoughLevelForBurst && HasTechnicalFinish) || IsLowLevelBurst) && HasDevilment;
    /// <summary>
    /// Determines if the player has the necessary level to execute both Devilment and Technical Finish.
    /// </summary>
    private bool HasEnoughLevelForBurst => DevilmentPvE.EnoughLevel && TechnicalStepPvE.EnoughLevel;
    /// <summary>
    /// Checks if the player is in a low-level burst scenario,
    /// </summary>
    private bool IsLowLevelBurst => !HasEnoughLevelForBurst && HasStandardFinish;
    private static bool HasTillana => HasActiveStatus(StatusID.FlourishingFinish);
    private static bool IsMedicated => HasActiveStatus(StatusID.Medicated);

    /// <summary>
    /// Determines if the player's current medication status is due to using the correct Potion
    /// </summary>
    private bool IsBurstMedicine
    {
        get
        {
            if (Medicines.Length == 0) return false;

            if (!IsMedicated) return false;

            foreach (var medicine in Medicines)
            {
                if (medicine.Type != MedicineType)
                    return false;
                if (!IsLastAction(false, new IAction[medicine.ID]))
                    return false;
            }

            return true;
        }
    }
    private bool JustMedicated => IsMedicated && IsBurstMedicine;
    private static bool HasSilkenProcs => HasActiveStatus(SilkenProcs);
    private static bool HasFlourishingProcs => HasActiveStatus(FlourishingProcs);
    private static bool HasAnyProc => HasSilkenProcs || HasFlourishingProcs;
    private static bool HasFinishingMove => HasActiveStatus(StatusID.FinishingMoveReady);
    private static bool HasStarfall => HasActiveStatus(StatusID.FlourishingStarfall);
    private static bool HasDanceOfTheDawn => HasActiveStatus(StatusID.DanceOfTheDawnReady);

    /// <summary>
    /// Calculates the effective animation lock duration taking the maximum of the base AnimationLock and an estimated value,
    /// </summary>
    private static float CalculatedAnimationLock => Math.Max(AnimationLock, EstimatedAnimationLock);

    /// <summary>
    /// Calculates the remaining time on the weapon skill lock by subtracting the calculated animation lock from the total weapon lock duration,
    /// </summary>
    private static float WeaponLock => WeaponTotal - CalculatedAnimationLock;

    #endregion

    #region Job Gauge

    private static bool HasEnoughFeathers => Feathers > 3;
    private static bool HasFeatherProcs => HasThreefoldFanDance || HasFourfoldFanDance;
    private static bool CanStandardFinish => HasStandardStep && CompletedSteps > 1;
    private static bool CanTechnicalFinish => HasTechnicalStep && CompletedSteps > 3;
    private static bool CanSaberDance => Esprit >= SaberDanceEspritCost;
    private int EspritThreshold
    {
        get
        {
            if (!IsBurstPhase && !IsMedicated) return MidEspritThreshold;

            if (HasDanceOfTheDawn && (CanSaberDance || IsLastGCD(ActionID.TillanaPvE))) return SaberDanceEspritCost;

            if (HasLastDance || HasFinishingMove)
            {
                if (ActiveStandardWillHaveCharge) return MaxEsprit;
            }

            if (HasStarfall && StarfallEndingSoon) return HighEspritThreshold;

            return SaberDanceEspritCost;
        }
    }
    private bool CanSpendEspritNow => Esprit >= EspritThreshold;

    #endregion

    #region Target Info

    #region Hostiles
    /// <summary>
    /// Determines if there are any hostile targets within the effective range for dance steps,
    /// which is crucial for deciding whether to hold or use dance actions based on the presence of valid targets.
    /// </summary>
    /// <returns>
    /// True if there is at least one hostile target within the effective range for dance steps;
    /// otherwise, false.
    /// </returns>>
    private static bool AreDanceTargetsInRange
    {
        get
        {
            if (!InCombat && !IsDancing) return false;

            if (AllHostileTargets == null) return false;

            foreach (var target in AllHostileTargets)
            {
                if (target.DistanceToPlayer() <= DanceTargetRange) return true;
            }

            return false;
        }

    }

    #endregion

    #region Friendlies

    private bool ShouldUseTechStep => TechnicalStepPvE.IsEnabled && TechnicalStepPvE.EnoughLevel  && MergedStatus.HasFlag(AutoStatus.Burst);
    private bool ShouldUseStandardStep => StandardStepPvE.IsEnabled && StandardStepPvE.EnoughLevel &&!HasLastDance;
    private bool ShouldUseFinishingMove => FinishingMovePvE.IsEnabled && FinishingMovePvE.EnoughLevel && !HasLastDance;

    /// <summary>
    /// Checks if there is an available dance partner within range that meets the criteria for being a valid partner,
    /// optionally restricting to only DPS targets if specified.
    /// </summary>
    /// <param name="restrictToDps">
    /// A boolean value indicating whether to restrict the search for dance partners to only those with a DPS combat role.
    /// </param>
    /// <returns>
    /// True if there is at least one valid dance partner within range that meets the specified criteria; otherwise, false.
    /// </returns>
    private static bool HasAvailableDancePartner(bool restrictToDps)
    {
        if (PartyMembers == null) return false;

        foreach (var member in PartyMembers)
        {
            if (IsValidDancePartnerInRange(member)
                && (!restrictToDps || IsDPSinParty(member)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if the specified party member is a valid DPS target for dancing,
    /// ensuring they are alive, in the party, and have the appropriate combat role.
    /// </summary>
    /// <param name="p">
    /// The party member to check for validity as a DPS target.
    /// This should be an instance of IBattleChara representing a character in the player's party.
    /// </param>
    /// <returns>
    /// True if the specified party member is a valid DPS target for dance partner; otherwise, false.
    /// </returns>
    private static bool IsDPSinParty(IBattleChara? p)
    {
        if (p == null) return false;
        if (!p.IsParty()) return false;
        return p.GetRole() == CombatRole.DPS;
    }
    /// <summary>
    /// Checks if the specified party member is a valid dance partner,
    /// ensuring they are alive and do not have any negative statuses that would prevent them from being an effective partner.
    /// </summary>
    /// <param name="p">
    /// The party member to check for validity as a dance partner. This should be an instance of IBattleChara representing a character in the player's party.
    /// </param>
    /// <returns>
    /// True if the specified party member is a valid dance partner; otherwise, false.
    /// A valid dance partner is one who is alive and does not have any of the negative statuses defined in HasWeaknessOrDamageDown.
    /// </returns>
    private static bool IsValidDancePartner(IBattleChara? p)
    {
        if (p == null) return false;
        if (p.IsDead) return false;
        return !p.HasApplyStatus(HasWeaknessOrDamageDown);
    }
    /// <summary>
    /// Checks if the specified party member is within the effective range to receive dance buffs,
    /// </summary>
    /// <param name="p">
    /// The party member to check for being within dance buff range.
    /// This should be an instance of IBattleChara representing a character in the player's party.
    /// </param>
    /// <returns>
    /// True if the specified party member is within the effective range to receive dance buffs; otherwise, false.
    /// </returns>
    private static bool IsValidDancePartnerInRange(IBattleChara? p)
    {
        if (p == null) return false;
        if (!IsValidDancePartner(p)) return false;
        return p.DistanceToPlayer() <= DanceAllyRange;
    }

    #endregion

    #endregion

    #endregion

    #region Config Options

    private static readonly ChurinDNCPotions ChurinPotions = new();

    #region Dance Partner Configs

    [RotationConfig(CombatType.PvE, Name = "Restrict Dance Partner to only DPS targets if any")]
    private static bool RestrictDPTarget { get; set; } = true;

    #endregion

    #region Dance Configs

    #region Opener Step Configs

    [RotationConfig(CombatType.PvE, Name = "Select an opener")]
    public static DancerOpener ChosenOpener { get; set; } = DancerOpener.Standard;

    #endregion

    #region Tech Step Configs

    [RotationConfig(CombatType.PvE, Name = "Technical Step, Technical Finish & Tillana Hold Strategy")]
    private HoldStrategy TechHoldStrategy { get; set; } = HoldStrategy.HoldStepAndFinish;

    [Range(0, 16, ConfigUnitType.Seconds, 0)]
    [RotationConfig(CombatType.PvE, Name = "How many seconds before combat starts to use Technical Step?",
        Parent = nameof(ChosenOpener),
        ParentValue = "Tech Opener",
        Tooltip = "If countdown is set above 13 seconds, " +
                  "it will start with Standard Step before initiating Tech Step, " +
                  "please go out of range of any enemies before the countdown reaches your configured time")]
    private float OpenerTechTime { get; set; } = 7f;

    [Range(0, 1, ConfigUnitType.Seconds, 0)]
    [RotationConfig(CombatType.PvE, Name = "How many seconds before combat starts to use Technical Finish?",
        Parent = nameof(ChosenOpener),
        ParentValue = "Tech Opener")]
    private float OpenerTechFinishTime { get; set; } = 0.5f;

    #endregion

    #region Standard Step Configs

    [RotationConfig(CombatType.PvE, Name = "Standard Step, Standard Finish & Finishing Move Hold Strategy")]
    private HoldStrategy StandardHoldStrategy { get; set; } = HoldStrategy.HoldStepAndFinish;

    [Range(0, 16, ConfigUnitType.Seconds, 0)]
    [RotationConfig(CombatType.PvE, Name = "How many seconds before combat starts to use Standard Step?",
        Parent = nameof(ChosenOpener),
        ParentValue = "Standard Opener")]
    private float OpenerStandardStepTime { get; set; } = 15.5f;

    [Range(0, 1, ConfigUnitType.Seconds, 0)]
    [RotationConfig(CombatType.PvE, Name = "How many seconds before combat starts to use Standard Finish?",
        Parent = nameof(ChosenOpener),
        ParentValue = "Standard Opener")]
    private float OpenerStandardFinishTime { get; set; } = 0.5f;

    [RotationConfig(CombatType.PvE,
        Name = "Disable Standard Step in Burst - Ignored if not high enough level for Finishing Move")]
    private bool DisableStandardInBurst { get; set; } = true;

    #endregion

    #endregion

    #region Potion Configs

    [RotationConfig(CombatType.PvE, Name = "Enable Potion Usage")]
    private static bool PotionUsageEnabled
    {
        get => ChurinPotions.Enabled;
        set => ChurinPotions.Enabled = value;
    }

    [RotationConfig(CombatType.PvE, Name = "Define potion usage behavior for Dancer",
        Parent = nameof(PotionUsageEnabled))]
    private static PotsDuringStepStrategy PotsDuringStep { get; set; } = PotsDuringStepStrategy.BeforeStep;

    [RotationConfig(CombatType.PvE, Name = "Potion Usage Presets", Parent = nameof(PotionUsageEnabled))]
    private static PotionStrategy PotionUsagePresets
    {
        get => ChurinPotions.Strategy;
        set => ChurinPotions.Strategy = value;
    }

    [Range(0, 20, ConfigUnitType.Seconds, 0)]
    [RotationConfig(CombatType.PvE, Name = "Use Opener Potion at minus (value in seconds)",
        Parent = nameof(PotionUsageEnabled))]
    private static float OpenerPotionTime
    {
        get => ChurinPotions.OpenerPotionTime;
        set => ChurinPotions.OpenerPotionTime = value;
    }

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
    [RotationConfig(CombatType.PvE,
        Name = "Use 2nd Potion at (value in seconds)", Parent = nameof(PotionUsagePresets),
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

    #endregion

    #endregion

    #region Main Combat Logic

    #region Countdown Logic

    // Override the method for actions to be taken during the countdown phase of combat
    protected override IAction? CountDownAction(float remainTime)
    {
        if (!HasClosedPosition && TryUseClosedPosition(out var act)) return act;
        if (ChurinPotions.ShouldUsePotion(this, out var potionAct, false)) return potionAct;

        if (remainTime > OpenerStandardStepTime) return base.CountDownAction(remainTime);

        act = ChosenOpener switch
        {
            DancerOpener.Standard => CountDownStandardOpener(remainTime),
            DancerOpener.Tech => CountDownTechOpener(remainTime),
            _ => null
        };

        return act ?? base.CountDownAction(remainTime);
    }

    private bool ShouldStandardBeforeTech(float remainTime)
    {
        return remainTime > OpenerTechTime
               && remainTime > 13f;
    }

    private IAction? CountDownStandardOpener(float remainTime)
    {
        IAction? act;
        if (remainTime <= OpenerStandardStepTime && !IsDancing)
            if (StandardStepPvE.CanUse(out act))
                return act;

        if (!CanStandardFinish)
            if (ExecuteStepGCD(out act))
                return act;

        if (!(remainTime <= OpenerStandardFinishTime) || !CanStandardFinish) return null;

        return TryFinishDance(out act, false) ? act : null;
    }

    private IAction? CountDownTechOpener(float remainTime)
    {
        IAction? act;

        var preparingStandard = ShouldStandardBeforeTech(remainTime)
                                && !IsDancing
                                && HasStandardFinish;

        if (preparingStandard)
            if (StandardStepPvE.CanUse(out act))
                return act;

        var readyToTechStep = remainTime <= OpenerTechTime
                              && !IsDancing
                              && !HasTechnicalStep;
        if (readyToTechStep)
        {
            if (TechnicalStepPvE.CanUse(out act))
                return act;
        }

        if (IsDancing && !CanTechnicalFinish)
        {
            if (ExecuteStepGCD(out act))
                return act;
        }

        var finishStandard = remainTime > OpenerTechTime
                             && IsDancing
                             && HasStandardStep
                             && !AreDanceTargetsInRange;
        if (finishStandard)
        {
            if (DoubleStandardFinishPvE.CanUse(out act))
                return act;
        }

        var readyToTechFinish = CanTechnicalFinish
                                && remainTime <= OpenerTechFinishTime;

        if (!readyToTechFinish) return null;

        return TryFinishDance(out act, true) ? act : null;
    }

    #endregion

    #region Main oGCD Logic

    /// Override the method for handling emergency abilities
    protected override bool EmergencyAbility(IAction nextGCD, out IAction? act)
    {
        act = null;
        if (!IsDancing || !Showtime)
            return TryUseDevilment(out act)
                   || SwapDancePartner(out act)
                   || TryUseClosedPosition(out act);

        if (JustMedicated)
            return TryFinishDance(out act, true)
                   ||TryFinishDance(out act, false)
                   || base.EmergencyAbility(nextGCD, out act);

        if (!ChurinPotions.ShouldUsePotion(this, out var potionAct)) return false;

        act = potionAct;
        return true;
    }

    /// Override the method for handling attack abilities
    protected override bool AttackAbility(IAction nextGCD, out IAction? act)
    {
        act = null;
        if (TryUseFlourish(out act)) return true;

        return TryUseFeatherProcs(out act)
               || TryUseFeathers(out act)
               || base.AttackAbility(nextGCD, out act);
    }

    #endregion

    #region Main GCD Logic

    /// Override the method for handling general Global Cooldown (GCD) actions
    protected override bool GeneralGCD(out IAction? act)
    {
        if (IsDancing)
        {
            return TryFinishDance(out act, true)
                || TryFinishDance(out act, false);
        }
        if (TryUseStep(out act)) return true;
        if (IsBurstPhase) return TryUseBurstGCD(out act);
        return (!Showtime && TryUseFillerGCD(out act))
               || base.GeneralGCD(out act);
    }

    #endregion

    #endregion

    #region Extra Methods

    #region Action Helpers

    #region Dance Helpers

    private readonly ActionID[] _danceSteps = [ActionID.StandardStepPvE, ActionID.TechnicalStepPvE];
    private IBaseAction ActiveStandard => CanFinishingMove ? FinishingMovePvE : StandardStepPvE;
    private IAction UseActiveStandard => ActiveStandard;
    private bool AboutToDance => CanUseTechStep || CanUseActiveStandard;

	private bool TryUseProcs(out IAction? act)
	{
		act = null;

    #region Potions

	private bool TryUseProcs(out IAction? act)
	{
		act = null;

    #region Potions

	private bool TryUseProcs(out IAction? act)
	{
		act = null;

    #region Potions

	private bool TryUseProcs(out IAction? act)
	{
		act = null;

    #region Potions

    #endregion

    #region Potions

    #endregion

    #region Potions

    /// <summary>
    /// Determines if the conditions are met to use either
    /// Technical Step or Standard Step based on the player's current status,
    /// available resources, and configuration settings,
    /// </summary>
    private bool Showtime => IsDancing || IsLastGCD(_danceSteps) || AboutToDance;

    /// <summary>
    /// Determines if the player can use Finishing Move as a finisher for their dance
    /// based on various conditions such as level requirements, combat status, cooldowns,
    /// and whether they have the necessary buffs active.
    /// </summary>
    private bool CanFinishingMove
    {
        get
        {
            if (!FinishingMovePvE.EnoughLevel || !FinishingMovePvE.IsEnabled) return false;
            if (!InCombat || !FlourishPvE.IsEnabled) return false;
            if (FlourishPvE.Cooldown.IsCoolingDown && !HasFinishingMove) return false;
            return HasFinishingMove;
        }
    }

    /// <summary>
    /// Determines if the player needs to refresh their Standard Finish status based on
    /// various conditions such as combat status.
    /// </summary>
    private bool HasToRefreshStandardFinish
    {
        get
        {
            if (!InCombat && (IsDancing || HasStandardStep)) return false;

            if (HasStandardFinish && (IsDancing || !ActiveStandardWillHaveCharge || HasStandardStep)) return false;

            if (HasStandardFinish && TechnicalRecastRemain < SecondsToCompleteTech && ShouldUseTechStep) return false;

            return (StatusHelper.PlayerWillStatusEnd(ActiveStandardRecastRemain + WeaponTotal, true, StatusID.StandardFinish)
                    || !HasStandardFinish) && ActiveStandard.CanUse(out _);
        }
    }
    private float ActiveStandardRecastRemain => ActiveStandard.Cooldown.RecastTimeRemain;
    private float TechnicalRecastRemain => TechnicalStepPvE.Cooldown.RecastTimeRemain;
    private bool ActiveStandardWillHaveCharge =>
        ActiveStandard.Cooldown.WillHaveOneCharge(SecondsToCompleteStandard + WeaponLock);
    private bool CanUseStandardBasedOnEsprit => !HasLastDance && !CanSpendEspritNow;
    private bool CanUseStandardStepInBurst => !DisableStandardInBurst || HasFinishingMove;
    private bool DevilmentReady
    {
        get
        {
            var devilmentRemain = DevilmentPvE.Cooldown.RecastTimeRemain;
            if (!ShouldUseTechStep) return false;

            if (DevilmentPvE.Cooldown.IsCoolingDown
                && Math.Abs(devilmentRemain - TechnicalRecastRemain) > SecondsToCompleteTech + WeaponLock) return false;

            return DevilmentPvE.Cooldown.WillHaveOneCharge(SecondsToCompleteTech + WeaponLock);
        }
    }
    private bool ShouldUseTechStep => TechnicalStepPvE.IsEnabled && TechnicalStepPvE.EnoughLevel &&
                                      MergedStatus.HasFlag(AutoStatus.Burst);

    /// <summary>
    /// Decide if the recast timing is acceptable to attempt a step given weapon/animation locks.
    /// </summary>
    private static bool IsTimingOk(float recastRemain, IBaseAction action)
    {
        if (recastRemain > WeaponTotal && action.Cooldown.IsCoolingDown)
        {
            return false;
        }

		if ((StandardStepPvE.Cooldown.WillHaveOneCharge(3f)
		|| FinishingMovePvE.Cooldown.WillHaveOneCharge(3f)
		|| TechnicalStepPvE.Cooldown.WillHaveOneCharge(3f))
		&& ShouldSwapDancePartner)
		{
			return EndingPvE.CanUse(out act);
		}
		return false;
	}

	#endregion

	#region Dance Logic

	private bool TryUseStep(out IAction? act)
	{
		act = null;
		if (IsDancing) return false;

		if (CanUseTechnicalStep)
		{
			act = TechnicalStepPvE;
			return true;
		}


		switch (CanUseStandardStep)
		{
			case true when !HasFinishingMove:
				act = StandardStepPvE;
				return true;

			case true when HasFinishingMove:
				act = FinishingMovePvE;
				return true;
		}

		return false;
	}

	private bool TryFinishStandard(out IAction? act)
	{
		act = null;
		if (!HasStandardStep || HasFinishingMove || !IsDancing) return false;

		if (CompletedSteps < 2) return ExecuteStepGCD(out act);

		var shouldFinish = HasStandardStep && CompletedSteps == 2 && CanUseStepHoldCheck(StandardHoldStrategy);
		var aboutToTimeOut = StatusHelper.PlayerWillStatusEnd(1, true, StatusID.StandardStep);

		return (shouldFinish || aboutToTimeOut) && DoubleStandardFinishPvE.CanUse(out act, skipAoeCheck: true);
	}

	private bool TryFinishTech(out IAction? act)
	{
		act = null;
		if (!HasTechnicalStep || HasTillana || !IsDancing) return false;

		if (CompletedSteps < 4) return ExecuteStepGCD(out act);

		var shouldFinish = HasTechnicalStep && CompletedSteps == 4 && CanUseStepHoldCheck(TechHoldStrategy);
		var aboutToTimeOut = StatusHelper.PlayerWillStatusEnd(1, true, StatusID.TechnicalStep);

		return (shouldFinish || aboutToTimeOut) && QuadrupleTechnicalFinishPvE.CanUse(out act, skipAoeCheck: true);
	}

	private bool TryFinishTheDance(out IAction? act)
	{
		act = null;
		if (!IsDancing || HasFinishingMove || HasTillana) return false;

		return TryFinishStandard(out act) || TryFinishTech(out act);
	}

	#endregion

	#region Burst Logic

	private bool TryUseBurstGCD(out IAction? act)
	{
		act = null;
		if (TryUseStep(out act)) return true;

		if (TryUseTillana(out act)) return true;

		if (TryUseDanceOfTheDawn(out act)) return true;

		if (TryUseLastDance(out act)) return true;

		if (TryUseStarfallDance(out act)) return true;

		return TryUseSaberDance(out act) || TryUseFillerGCD(out act);
	}

	private bool TryUseDanceOfTheDawn(out IAction? act)
	{
		act = null;
		if (Esprit < SaberDanceEspritCost
			|| !StatusHelper.PlayerHasStatus(true, StatusID.DanceOfTheDawnReady)
			|| StandardStepPvE.Cooldown.WillHaveOneCharge(7.5f) && HasLastDance && Esprit < HighEspritThreshold) return false;

		return DanceOfTheDawnPvE.CanUse(out act);
	}

	private bool TryUseTillana(out IAction? act)
	{
		act = null;

		if (!HasTillana
			|| Esprit >= SaberDanceEspritCost)
		{
			return false;
		}

		var gcdsUntilStandard = 0;
		for (uint i = 1; i <= 5; i++)
		{
			if (StandardStepPvE.Cooldown.WillHaveOneChargeGCD(i, 0.5f))
			{
				gcdsUntilStandard = (int)i;
				break;
			}
		}

		if (TillanaPvE.CanUse(out act))
		{
			switch (gcdsUntilStandard)
			{
				case 5:
				case 4:
				case 3:
					if (Esprit < 20) return true;
					if (!HasLastDance) return Esprit < SaberDanceEspritCost;
					break;
				case 2:
				case 1:
					return Esprit < 10 && !HasLastDance;
			}

		}

		return Esprit < SaberDanceEspritCost && TillanaPvE.CanUse(out act);
	}

	private bool ShouldUseLastDance
	{
		get
		{
			var lastDanceEndingSoon = StatusHelper.PlayerWillStatusEnd(5, true, StatusID.LastDanceReady);
			var standardSoonish = StandardStepPvE.Cooldown.WillHaveOneCharge(10);

			if (lastDanceEndingSoon)
			{
				return true;
			}

			if (IsBurstPhase)
			{
				if (standardSoonish)
				{
					if (HasTillana && Esprit >= 20 || !TryUseDanceOfTheDawn(out _))
					{
						return Esprit < HighEspritThreshold || !_saberDancePrimed;
					}
				}
				else
				{
					if (!HasStarfall && (Esprit < SaberDanceEspritCost || !_saberDancePrimed))
					{
						return true;
					}
				}
			}
			else
			{
				if (Esprit < MidEspritThreshold
					&& !TechnicalStepPvE.Cooldown.WillHaveOneCharge(15f))
				{
					return true;
				}
			}
			return false;
		}
	}

	private bool TryUseLastDance(out IAction? act)
	{
		act = null;
		if (!HasLastDance) return false;

		return LastDancePvE.CanUse(out act) && ShouldUseLastDance;
	}

	private bool ShouldUseStarfallDance
	{
		get
		{
			var willHaveOneCharge = StandardStepPvE.Cooldown.WillHaveOneCharge(5);

			if (StatusHelper.PlayerWillStatusEnd(7f, true, StatusID.FlourishingStarfall))
			{
				return true;
			}

			if (HasLastDance && willHaveOneCharge
				|| Esprit >= HighEspritThreshold || _saberDancePrimed)
			{
				return false;
			}

			return Esprit < SaberDanceEspritCost || !_saberDancePrimed;
		}
	}

	private bool TryUseStarfallDance(out IAction? act)
	{
		act = null;
		if (!HasStarfall || CanUseStandardStep) return false;

		return ShouldUseStarfallDance && StarfallDancePvE.CanUse(out act);
	}

	#endregion

	#region GCD Skills

	private bool TryUseFillerGCD(out IAction? act)
	{
		act = null;
		if (TryUseStep(out act)) return true;
		if (TryUseSaberDance(out act)) return true;
		if (TryUseTillana(out act)) return true;
		if (TryUseProcs(out act)) return true;
		if (TryUseFeatherGCD(out act)) return true;
		return TryUseLastDance(out act) || TryUseBasicGCD(out act);
	}

	private bool TryUseBasicGCD(out IAction? act)
	{
		act = null;
		if (TryUseStep(out act)) return true;
		if (IsBurstPhase && !HasLastDance && Esprit >= SaberDanceEspritCost
			|| IsMedicated && Esprit >= SaberDanceEspritCost) return SaberDancePvE.CanUse(out act);

		if (Esprit > HighEspritThreshold) return false;
		if (BloodshowerPvE.CanUse(out act)) return true;
		if (FountainfallPvE.CanUse(out act)) return true;
		if (RisingWindmillPvE.CanUse(out act)) return true;
		if (ReverseCascadePvE.CanUse(out act)) return true;
		if (BladeshowerPvE.CanUse(out act)) return true;
		if (FountainPvE.CanUse(out act)) return true;
		return WindmillPvE.CanUse(out act) || CascadePvE.CanUse(out act);
	}

	private bool TryUseFeatherGCD(out IAction? act)
	{
		act = null;
		if (Feathers < 4 || CanUseStandardStep || CanUseTechnicalStep || IsDancing) return false;

		var hasSilkenProcs = HasSilkenFlow || HasSilkenSymmetry;
		var hasFlourishingProcs = HasFlourishingFlow || HasFlourishingSymmetry;

		if (Feathers > 3 && !hasSilkenProcs && hasFlourishingProcs && Esprit < SaberDanceEspritCost && !IsBurstPhase)
		{
			if (FountainPvE.CanUse(out act)) return true;
			if (CascadePvE.CanUse(out act)) return true;
		}

		if (Feathers > 3 && (hasSilkenProcs || hasFlourishingProcs) && Esprit > SaberDanceEspritCost)
		{
			return SaberDancePvE.CanUse(out act);
		}

		return false;
	}


	private bool TryUseSaberDance(out IAction? act)
	{
		act = null;
		var willHaveOneCharge = StandardStepPvE.Cooldown.WillHaveOneCharge(5);

		// Need at least 50 Esprit to use Saber Dance
		if (Esprit < SaberDanceEspritCost) return false;

		// Don't use if Technical Step is ready (prioritize starting Tech)
		if (CanUseTechnicalStep || IsDancing) return false;

		if (!SaberDancePvE.CanUse(out act) || !_saberDancePrimed)
		{
			return false;
		}

		if (!IsBurstPhase)
		{
			if (IsMedicated)
			{
				return Esprit >= SaberDanceEspritCost;
			}
			return Esprit >= MidEspritThreshold;
		}

		if (!willHaveOneCharge)
		{
			return Esprit >= SaberDanceEspritCost;
		}

		if (HasLastDance)
		{
			return Esprit >= HighEspritThreshold;
		}

		return false;

	}

	private bool TryUseProcs(out IAction? act)
	{
		act = null;
		if (IsBurstPhase || !ShouldUseTechStep || CanUseStandardStep || CanUseTechnicalStep || IsDancing) return false;

		var gcdsUntilTech = 0;
		for (uint i = 1; i <= 5; i++)
		{
			if (TechnicalStepPvE.Cooldown.WillHaveOneChargeGCD(i, 0.5f))
			{
				gcdsUntilTech = (int)i;
				break;
			}
		}

		if (gcdsUntilTech is 0 or > 5) return false;

		switch (gcdsUntilTech)
		{
			case 5:
			case 4:
				if (!HasAnyProc || Esprit < HighEspritThreshold) return TryUseBasicGCD(out act);
				if (Esprit >= HighEspritThreshold) return SaberDancePvE.CanUse(out act);
				break;
			case 3:
				if (HasAnyProc && Esprit < HighEspritThreshold) return TryUseBasicGCD(out act);
				return FountainPvE.CanUse(out act) || CascadePvE.CanUse(out act) || SaberDancePvE.CanUse(out act);
			case 2:
				if (Esprit >= SaberDanceEspritCost && !HasAnyProc) return SaberDancePvE.CanUse(out act);
				if (Esprit < SaberDanceEspritCost) return TryUseBasicGCD(out act);
				break;
			case 1:
				if (HasAnyProc && Esprit < HighEspritThreshold) return TryUseBasicGCD(out act);
				if (!HasAnyProc && Esprit < SaberDanceEspritCost && FountainPvE.CanUse(out act)) return true;
				if (!HasAnyProc && Esprit >= SaberDanceEspritCost) return SaberDancePvE.CanUse(out act);
				if (!HasAnyProc && Esprit < SaberDanceEspritCost) return LastDancePvE.CanUse(out act);
				break;
		}
		return false;
	}

	#endregion

	#region OGCD Abilities

	private bool TryUseDevilment(out IAction? act)
	{
		act = null;
		if (IsDancing || !DevilmentPvE.EnoughLevel || DevilmentPvE.Cooldown.IsCoolingDown) return false;

		if (HasTechnicalFinish
			|| IsLastGCD(true, QuadrupleTechnicalFinishPvE)
			|| HasTillana
			|| !TechnicalStepPvE.EnoughLevel && IsLastGCD(true, DoubleStandardFinishPvE))
		{
			act = DevilmentPvE;
			return true;
		}
		return false;
	}

	private bool TryUseFlourish(out IAction? act)
	{
		act = null;
		if (!InCombat || HasThreefoldFanDance || !FlourishPvE.IsEnabled || !FlourishPvE.EnoughLevel) return false;

		if (!FlourishPvE.CanUse(out act)) return false;

		if (IsBurstPhase)
		{
			return true;
		}

		switch (ShouldUseTechStep)
		{
			case true when TechnicalStepPvE.Cooldown.IsCoolingDown && !TechnicalStepPvE.Cooldown.WillHaveOneCharge(15):
			case false:
				act = FlourishPvE;
				return true;
		}
		return false;
	}

	private bool TryUseFeathers(out IAction? act)
	{
		act = null;
		var hasEnoughFeathers = Feathers > 3;

		if (hasEnoughFeathers && (HasAnyProc || FlourishPvE.Cooldown.WillHaveOneCharge(3)))
		{
			if (HasThreefoldFanDance && FanDanceIiiPvE.CanUse(out act)) return true;
			if (FanDanceIiPvE.CanUse(out act)) return true;
			if (FanDancePvE.CanUse(out act)) return true;
		}

		if (HasFourfoldFanDance && FanDanceIvPvE.CanUse(out act)) return true;
		if (HasThreefoldFanDance && FanDanceIiiPvE.CanUse(out act)) return true;

		if (!IsBurstPhase && (!hasEnoughFeathers || !HasAnyProc || CanUseTechnicalStep)
						  && (!IsMedicated || TechnicalStepPvE.Cooldown.WillHaveOneCharge(10)))
		{
			return false;
		}

		return FanDanceIiPvE.CanUse(out act)
			   || FanDancePvE.CanUse(out act);
	}

	#endregion

	#endregion

	/// <summary>
	/// DNC-specific potion manager that extends base potion logic with job-specific conditions.
	/// </summary>
	private class ChurinDNCPotions : Potions
	{
		private float _step4ReachedTime = -1f;

		public override bool IsConditionMet()
		{
			float now = DataCenter.CombatTimeRaw;

			// Detect when all 4 steps are complete — pot should fire before Tech Finish
			if (HasTechnicalStep && CompletedSteps > 3)
			{
				_step4ReachedTime = now;
				return true;
			}

			// Allow pot for up to 2s after step 4 was registered (catches missed single-frame windows)
			if (_step4ReachedTime > 0 && now - _step4ReachedTime <= 2f)
				return true;

			// Standard step equivalent
			if (HasStandardStep && CompletedSteps > 1)
				return true;

			_step4ReachedTime = -1f;
			return false;
		}

		protected override bool IsTimingValid(float timing)
		{
			if (timing > 0 && DataCenter.CombatTimeRaw >= timing &&
				DataCenter.CombatTimeRaw - timing <= TimingWindowSeconds) return true;

			// Check opener timing: OpenerPotionTime == 0 means disabled
			var countDown = Service.CountDownTime;
			if (IsOpenerPotion(timing))
			{
				if (ChurinDNC.OpenerPotionTime == 0f) return false;
				return countDown > 0f && countDown <= ChurinDNC.OpenerPotionTime;
			}
			return false;
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
            BoolRow("Tech Hold Check", CanUseStepHoldCheck(TechHoldStrategy, true));

            if (ImGui.TreeNode("Technical Step Blocking Reasons"))
            {
                var canUseTechStep = CanUseTechStep;
                ColoredTextRow("Can Use Technical Step", canUseTechStep);

                if (!canUseTechStep)
                {
                    ImGui.Indent();
                    ColoredTextRow("Should Use Tech Step", ShouldUseTechStep);
                    ColoredTextRow("Is Dancing", IsDancing);
                    ColoredTextRow("Has Tillana", HasTillana);
                    ColoredTextRow("Has To Refresh Standard", HasToRefreshStandardFinish);
                    ColoredTextRow("Devilment Ready", DevilmentReady);
                    ColoredTextRow("Timing OK", IsTimingOk(TechnicalRecastRemain, TechnicalStepPvE));
                    ImGui.Unindent();
                }
                ImGui.TreePop();
            }

            ImGui.Separator();

            ValueRow("Standard Hold Strategy", StandardHoldStrategy);
            BoolRow("Standard Hold Check", CanUseStepHoldCheck(StandardHoldStrategy, false));

            if (ImGui.TreeNode("Standard Step Blocking Reasons"))
            {
                var canUseStandard = CanUseActiveStandard;
                ColoredTextRow("Can Use Standard Step or Finishing Move", canUseStandard);

                if (!canUseStandard)
                {
                    ImGui.Indent();
                    ColoredTextRow("Active Standard Enabled", ActiveStandard.IsEnabled);
                    ColoredTextRow("In Burst Phase", IsBurstPhase);
                    ColoredTextRow("Can Use Standard In Burst", CanUseStandardStepInBurst);
                    ColoredTextRow("Can Use Based On Esprit", CanUseStandardBasedOnEsprit);
                    ColoredTextRow("Has Last Dance", HasLastDance);
                    ColoredTextRow("Can Spend Esprit Now", CanSpendEspritNow);
                    ValueRow("Esprit Threshold", EspritThreshold);
                    ValueRow("Current Esprit", Esprit);
                    ColoredTextRow("Timing OK", IsTimingOk(ActiveStandardRecastRemain, ActiveStandard));
                    ImGui.Unindent();
                }
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