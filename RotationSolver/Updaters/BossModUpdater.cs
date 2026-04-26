using ECommons.DalamudServices;
using RotationSolver.IPC;

namespace RotationSolver.Updaters;

internal static class BossModUpdater
{
	private static bool _checkedAvailability;
	private static bool _isAvailable;
	// When BMR is unavailable, retry IsEnabled at most once per this interval so that a late-loaded
	// BMR plugin (or a reload) starts feeding data without requiring a full RSR restart.
	private const float AvailabilityRecheckSeconds = 5f;
	private static DateTime _nextAvailabilityRecheck = DateTime.MinValue;
	// Window passed to BMR's Hints.IsXImminent(seconds) probes; covers the largest mit window we use,
	// so a true return means an event is within RSR's reaction horizon.
	private const float ImminentProbeSeconds = 15f;

	public static void Update()
	{
		if (!Service.Config.UseBmrTimeline)
		{
			if (DataCenter.BMRHasActiveModule)
				DataCenter.ResetBmrData();
			return;
		}

		DateTime now = DateTime.Now;
		if (!_checkedAvailability || (!_isAvailable && now >= _nextAvailabilityRecheck))
		{
			_isAvailable = BMRTimeline_IPCSubscriber.IsEnabled;
			_checkedAvailability = true;
			if (!_isAvailable)
				_nextAvailabilityRecheck = now.AddSeconds(AvailabilityRecheckSeconds);
		}

		if (!_isAvailable)
		{
			DataCenter.ResetBmrData();
			return;
		}

		try
		{
			DataCenter.BMRHasActiveModule = BMRTimeline_IPCSubscriber.HasActiveModule?.Invoke() ?? false;

			if (!DataCenter.BMRHasActiveModule)
			{
				DataCenter.ResetBmrData();
				return;
			}

			DataCenter.BMRActiveModuleName = BMRTimeline_IPCSubscriber.ActiveModuleName?.Invoke();

			// Store whether IPC Funcs are bound (null = BMR doesn't have that endpoint)
			DataCenter.BMRDebugTimelineRwFunc = BMRTimeline_IPCSubscriber.NextRaidwideIn != null;
			DataCenter.BMRDebugTimelineTbFunc = BMRTimeline_IPCSubscriber.NextTankbusterIn != null;
			DataCenter.BMRDebugHintsRwFunc = BMRTimeline_IPCSubscriber.NextRaidwideDamageIn != null;
			DataCenter.BMRDebugHintsTbFunc = BMRTimeline_IPCSubscriber.NextTankbusterDamageIn != null;
			DataCenter.BMRDebugHintsStackFunc = BMRTimeline_IPCSubscriber.NextSharedDamageIn != null;

			// Poll Timeline endpoints (state machine flags)
			float timelineRaidwide = SafeFloat(BMRTimeline_IPCSubscriber.NextRaidwideIn);
			float timelineTankbuster = SafeFloat(BMRTimeline_IPCSubscriber.NextTankbusterIn);
			DataCenter.BMRNextKnockbackIn = SafeFloat(BMRTimeline_IPCSubscriber.NextKnockbackIn);
			DataCenter.BMRNextDowntimeIn = SafeFloat(BMRTimeline_IPCSubscriber.NextDowntimeIn);
			DataCenter.BMRNextDowntimeEndIn = SafeFloat(BMRTimeline_IPCSubscriber.NextDowntimeEndIn);
			DataCenter.BMRNextVulnerableIn = SafeFloat(BMRTimeline_IPCSubscriber.NextVulnerableIn);
			DataCenter.BMRNextVulnerableEndIn = SafeFloat(BMRTimeline_IPCSubscriber.NextVulnerableEndIn);
			DataCenter.BMRDebugTimelineRaidwide = timelineRaidwide;
			DataCenter.BMRDebugTimelineTankbuster = timelineTankbuster;

			// Poll Hints endpoints (component-level damage predictions)
			float damageIn = SafeFloat(BMRTimeline_IPCSubscriber.NextDamageIn);
			int damageType = BMRTimeline_IPCSubscriber.NextDamageType?.Invoke() ?? 0;
			DataCenter.BMRNextDamageIn = damageIn;
			DataCenter.BMRNextDamageType = damageType;
			DataCenter.BMRDebugGenericDamageIn = damageIn;
			DataCenter.BMRDebugGenericDamageType = damageType;

			// Type-specific Hints endpoints (walk full prediction list, return first matching type)
			float hintsRaidwide = SafeFloat(BMRTimeline_IPCSubscriber.NextRaidwideDamageIn);
			float hintsTankbuster = SafeFloat(BMRTimeline_IPCSubscriber.NextTankbusterDamageIn);
			float hintsStack = SafeFloat(BMRTimeline_IPCSubscriber.NextSharedDamageIn);
			DataCenter.BMRDebugHintsRaidwide = hintsRaidwide;
			DataCenter.BMRDebugHintsTankbuster = hintsTankbuster;
			DataCenter.BMRDebugHintsStack = hintsStack;

			// Sanitize: <=0 means endpoint missing/SafeWrapper default or damage already resolved
			if (hintsRaidwide <= 0f) hintsRaidwide = float.MaxValue;
			if (hintsTankbuster <= 0f) hintsTankbuster = float.MaxValue;
			if (hintsStack <= 0f) hintsStack = float.MaxValue;
			if (timelineRaidwide <= 0f) timelineRaidwide = float.MaxValue;
			if (timelineTankbuster <= 0f) timelineTankbuster = float.MaxValue;

			// Generic NextDamageIn fallback: if BMR's type-specific endpoints are missing, use the
			// first predicted damage event when its type matches. Type 3 (Shared/stack) feeds the
			// raidwide bucket since stacks benefit from area mitigation.
			float genericRaidwide = ((damageType == 2 || damageType == 3) && damageIn > 0f) ? damageIn : float.MaxValue;
			float genericTankbuster = (damageType == 1 && damageIn > 0f) ? damageIn : float.MaxValue;
			float genericStack = (damageType == 3 && damageIn > 0f) ? damageIn : float.MaxValue;

			// Last-resort fallback for fights/clients where neither timeline nor type-specific hints
			// fire but the boolean Hints.IsXImminent probe sees the event. We fold a synthetic
			// "in-window" timestamp (= window/2) so RSR's window check downstream succeeds. Reuses
			// the existing BossModHints_IPCSubscriber bindings to avoid duplicating IPC plumbing.
			if (timelineRaidwide >= float.MaxValue && hintsRaidwide >= float.MaxValue && genericRaidwide >= float.MaxValue
				&& BossModHints_IPCSubscriber.Hints_IsRaidwideImminent != null
				&& SafeBool(() => BossModHints_IPCSubscriber.Hints_IsRaidwideImminent(ImminentProbeSeconds)))
			{
				hintsRaidwide = ImminentProbeSeconds * 0.5f;
			}
			if (timelineTankbuster >= float.MaxValue && hintsTankbuster >= float.MaxValue && genericTankbuster >= float.MaxValue
				&& BossModHints_IPCSubscriber.Hints_IsTankbusterImminent != null
				&& SafeBool(() => BossModHints_IPCSubscriber.Hints_IsTankbusterImminent(ImminentProbeSeconds)))
			{
				hintsTankbuster = ImminentProbeSeconds * 0.5f;
			}
			if (hintsStack >= float.MaxValue && genericStack >= float.MaxValue
				&& BossModHints_IPCSubscriber.Hints_IsSharedImminent != null
				&& SafeBool(() => BossModHints_IPCSubscriber.Hints_IsSharedImminent(ImminentProbeSeconds)))
			{
				hintsStack = ImminentProbeSeconds * 0.5f;
			}

			// Stack stays its own field so debug overlays / future logic can read it cleanly.
			DataCenter.BMRNextStackIn = Math.Min(hintsStack, genericStack);

			// Merge all sources into the canonical raidwide/tankbuster mit windows. Stacks are folded
			// into raidwide because area mitigation covers them; this is the fix for "stack imminent
			// triggered no shield" — previously type 3 fell through every branch.
			DataCenter.BMRNextRaidwideIn = Min4(timelineRaidwide, hintsRaidwide, genericRaidwide, DataCenter.BMRNextStackIn);
			DataCenter.BMRNextTankbusterIn = Math.Min(Math.Min(timelineTankbuster, hintsTankbuster), genericTankbuster);

			DataCenter.BMRSpecialModeIn = SafeFloat(BMRTimeline_IPCSubscriber.SpecialModeIn);
			DataCenter.BMRSpecialModeType = BMRTimeline_IPCSubscriber.SpecialModeType?.Invoke() ?? 0;
			DataCenter.BMRDebugTimelineWalk = BMRTimeline_IPCSubscriber.DebugTimelineWalk?.Invoke();
		}
		catch (Exception ex)
		{
			Svc.Log.Verbose($"BMR IPC poll failed, resetting BMR data: {ex.Message}");
			DataCenter.ResetBmrData();
			_checkedAvailability = false;
			_nextAvailabilityRecheck = DateTime.MinValue;
		}
	}

	private static float SafeFloat(Func<float>? f)
	{
		float v = f?.Invoke() ?? float.MaxValue;
		// Guard against NaN/Infinity/negative-overflow values an upstream IPC could theoretically
		// produce; downstream window checks treat MaxValue as "no event" so this is the safe default.
		return float.IsFinite(v) ? v : float.MaxValue;
	}

	private static bool SafeBool(Func<bool> f)
	{
		try
		{
			return f();
		}
		catch (Exception ex)
		{
			Svc.Log.Verbose($"BMR IsXImminent probe threw: {ex.Message}");
			return false;
		}
	}

	private static float Min4(float a, float b, float c, float d) => Math.Min(Math.Min(a, b), Math.Min(c, d));
}
