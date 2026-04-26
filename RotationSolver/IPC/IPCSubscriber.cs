using ECommons.DalamudServices;
using ECommons.EzIpcManager;
using ECommons.Reflection;
#pragma warning disable CS0169 // Field is never used
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
#nullable disable

namespace RotationSolver.IPC
{
	using System.ComponentModel;

	internal static class BossModHints_IPCSubscriber
	{
		private static readonly EzIPCDisposalToken[] _disposalTokens = EzIPC.Init(typeof(BossModHints_IPCSubscriber), "BossMod", SafeWrapper.AnyException);

		internal static bool IsEnabled => IPCSubscriber_Common.IsReady("BossModReborn");

		// Predicted damage events as JSON: [{ "Players": <ulong>, "Activation": <ISO8601>, "Type": "Raidwide"|"Tankbuster"|"Shared"|"None" }]
		[EzIPC("Hints.PredictedDamage", true)] internal static readonly Func<string> Hints_PredictedDamage;

		// Convenience: is a raidwide predicted within N seconds?
		[EzIPC("Hints.IsRaidwideImminent", true)] internal static readonly Func<float, bool> Hints_IsRaidwideImminent;

		// Convenience: is a tankbuster predicted within N seconds?
		[EzIPC("Hints.IsTankbusterImminent", true)] internal static readonly Func<float, bool> Hints_IsTankbusterImminent;

		// Convenience: is a shared/stack predicted within N seconds?
		[EzIPC("Hints.IsSharedImminent", true)] internal static readonly Func<float, bool> Hints_IsSharedImminent;

		// Forbidden directions as JSON: [{ "Center": <float rad>, "HalfWidth": <float rad>, "Activation": <ISO8601> }]
		[EzIPC("Hints.ForbiddenDirections", true)] internal static readonly Func<string> Hints_ForbiddenDirections;

		// Current special mode: "Normal", "Pyretic", "NoMovement", "Freezing", "Misdirection"
		[EzIPC("Hints.SpecialMode", true)] internal static readonly Func<string> Hints_SpecialMode;

		// Activation time of special mode (ISO8601 string, null if Normal)
		[EzIPC("Hints.SpecialModeActivation", true)] internal static readonly Func<string> Hints_SpecialModeActivation;

		internal static void Dispose() => IPCSubscriber_Common.DisposeAll(_disposalTokens);
	}

	/// <summary>
	/// IPC subscriber for BossMod Timeline data (fight state machine, phases, mechanics)
	/// </summary>
	internal static class BossModTimeline_IPCSubscriber
	{
		private static readonly EzIPCDisposalToken[] _disposalTokens = EzIPC.Init(typeof(BossModTimeline_IPCSubscriber), "BossMod", SafeWrapper.AnyException);

		internal static bool IsEnabled => IPCSubscriber_Common.IsReady("BossModReborn");

		// Whether a boss module with an active state machine is currently loaded
		[EzIPC("Timeline.IsActive", true)] internal static readonly Func<bool> Timeline_IsActive;

		// JSON: { "OID": uint, "Name": string, "TotalDuration": float }
		[EzIPC("Timeline.GetEncounter", true)] internal static readonly Func<string> Timeline_GetEncounter;

		// JSON: [{ "Name", "StartTime", "Duration", "MaxTime" }]
		[EzIPC("Timeline.GetPhases", true)] internal static readonly Func<string> Timeline_GetPhases;

		// JSON: [{ "ID", "PhaseID", "Time", "Duration", "Name", "Comment", "IsRaidwide", "IsTankbuster", ... }]
		[EzIPC("Timeline.GetStates", true)] internal static readonly Func<string> Timeline_GetStates;

		// JSON: [{ "OID", "BossName", "GroupName", "Category", "Expansion", "GroupType", "SortOrder" }]
		[EzIPC("Encounters.GetAll", true)] internal static readonly Func<string> Encounters_GetAll;

		// Offline timeline data for a specific encounter OID (no active fight needed)
		[EzIPC("Encounters.GetPhasesForOID", true)] internal static readonly Func<uint, string> Encounters_GetPhasesForOID;
		[EzIPC("Encounters.GetStatesForOID", true)] internal static readonly Func<uint, string> Encounters_GetStatesForOID;
		[EzIPC("Encounters.GetTotalDuration", true)] internal static readonly Func<uint, float> Encounters_GetTotalDuration;

		internal static void Dispose() => IPCSubscriber_Common.DisposeAll(_disposalTokens);
	}

	public static class Wrath_IPCSubscriber
	{
		public enum CancellationReason
		{
			[Description("The Wrath user manually elected to revoke your lease.")]
			WrathUserManuallyCancelled,
			[Description("Your plugin was detected as having been disabled, " +
						 "not that you're likely to see this.")]
			LeaseePluginDisabled,
			[Description("The Wrath plugin is being disabled.")]
			WrathPluginDisabled,
			[Description("Your lease was released by IPC call, " +
						 "theoretically this was done by you.")]
			LeaseeReleased,
			[Description("IPC Services have been disabled remotely. " +
						 "Please see the commit history for /res/ipc_status.txt. \n " +
						 "https://github.com/PunishXIV/WrathCombo/commits/main/res/ipc_status.txt")]
			AllServicesSuspended,
		}

		public enum AutoRotationConfigOption
		{
			InCombatOnly = 0, // bool
			DPSRotationMode = 1, // enum
			HealerRotationMode = 2, // enum
			FATEPriority = 3, // bool
			QuestPriority = 4, // bool
			SingleTargetHPP = 5, // int
			AoETargetHPP = 6, // int
			SingleTargetRegenHPP = 7, // int
			ManageKardia = 8, // bool
			AutoRez = 9, // bool
			AutoRezDPSJobs = 10, // bool
			AutoCleanse = 11, // bool
			IncludeNPCs = 12, // bool
			OnlyAttackInCombat = 13, //bool
		}

		public enum DPSRotationMode
		{
			Manual = 0,
			Highest_Max = 1,
			Lowest_Max = 2,
			Highest_Current = 3,
			Lowest_Current = 4,
			Tank_Target = 5,
			Nearest = 6,
			Furthest = 7,
		}

		public enum HealerRotationMode
		{
			Manual = 0,
			Highest_Current = 1,
			Lowest_Current = 2
		}

		public enum SetResult
		{
			[Description("A default value that shouldn't ever be seen.")]
			IGNORED = -1,
			[Description("The configuration was set successfully.")]
			Okay = 0,
			[Description("The configuration will be set, it is working asynchronously.")]
			OkayWorking = 1,
			[Description("IPC services are currently disabled.")]
			IPCDisabled = 10,
			[Description("Invalid lease.")]
			InvalidLease = 11,
			[Description("Blacklisted lease.")]
			BlacklistedLease = 12,
			[Description("Configuration you are trying to set is already set.")]
			Duplicate = 13,
			[Description("Player object is not available.")]
			PlayerNotAvailable = 14,
			[Description("The configuration you are trying to set is not available.")]
			InvalidConfiguration = 15,
			[Description("The value you are trying to set is invalid.")]
			InvalidValue = 16,
		}

		private static Guid? _curLease;

		internal static bool IsEnabled => IPCSubscriber_Common.IsReady("WrathCombo");

		private static readonly EzIPCDisposalToken[] _disposalTokens = EzIPC.Init(typeof(Wrath_IPCSubscriber), "WrathCombo", SafeWrapper.IPCException);

		[EzIPC] private static readonly Func<string, string, string, Guid?> RegisterForLeaseWithCallback;
		[EzIPC] internal static readonly Func<bool> GetAutoRotationState;
		[EzIPC] private static readonly Func<Guid, bool, SetResult> SetAutoRotationState;
		[EzIPC] private static readonly Action<Guid> ReleaseControl;

		public static bool DoThing(Func<SetResult> action)
		{
			SetResult result = action();
			bool check = result.CheckResult();
			if (!check && result == SetResult.InvalidLease)
				check = action().CheckResult();
			return check;
		}

		private static bool CheckResult(this SetResult result)
		{
			switch (result)
			{
				case SetResult.Okay:
				case SetResult.OkayWorking:
					return true;
				case SetResult.InvalidLease:
					_curLease = null;
					Register();
					return false;
				case SetResult.IPCDisabled:
				case SetResult.Duplicate:
				case SetResult.PlayerNotAvailable:
				case SetResult.InvalidConfiguration:
				case SetResult.InvalidValue:
				case SetResult.IGNORED:
					return false;
				default:
					throw new ArgumentOutOfRangeException(nameof(result), result, null);
			}
		}

		// Minimal API: only disabling Auto-Rotation
		internal static void DisableAutoRotation()
		{
			if (Register())
			{
				DoThing(() => SetAutoRotationState(_curLease!.Value, false));
			}
		}

		internal static void Release()
		{
			if (_curLease.HasValue)
			{
				ReleaseControl(_curLease.Value);
				_curLease = null;
			}
		}

		internal static void Dispose()
		{
			Release();
			IPCSubscriber_Common.DisposeAll(_disposalTokens);
		}

		// Callback name must be resolvable by Wrath; provide a no-op handler.
		// The callback signature is reflected by Wrath; keep it stable.
		public static void LeaseCancelled(CancellationReason reason, string info)
		{
			// Intentionally minimal: just clear our lease so subsequent calls re-register.
			_curLease = null;
		}

		private static bool Register()
		{
			if (_curLease.HasValue)
				return true;

			if (!IsEnabled)
				return false;

			// Use Dalamud plugin info for internal and display names where available.
			var internalName = Svc.PluginInterface.InternalName ?? "RotationSolver";
			var displayName = Svc.PluginInterface.Manifest?.Name ?? "Rotation Solver";
			var callbackName = $"{typeof(Wrath_IPCSubscriber).FullName}.LeaseCancelled";

			_curLease = RegisterForLeaseWithCallback(internalName, displayName, callbackName);
			return _curLease.HasValue;
		}
	}

	internal class IPCSubscriber_Common
	{
		internal static bool IsReady(string pluginName) => DalamudReflector.TryGetDalamudPlugin(pluginName, out _, false, true);

		internal static Version Version(string pluginName) => DalamudReflector.TryGetDalamudPlugin(pluginName, out var dalamudPlugin, false, true) ? dalamudPlugin.GetType().Assembly.GetName().Version : new Version(0, 0, 0, 0);

		internal static void DisposeAll(EzIPCDisposalToken[] _disposalTokens)
		{
			foreach (var token in _disposalTokens)
			{
				try
				{
					token.Dispose();
				}
				catch (Exception ex)
				{
					Svc.Log.Error($"Error while unregistering IPC: {ex}");
				}
			}
		}
	}
}