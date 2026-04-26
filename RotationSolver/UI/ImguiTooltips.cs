using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace RotationSolver.UI;

internal static class ImguiTooltips
{
	private const ImGuiWindowFlags TooltipFlag =
		  ImGuiWindowFlags.Tooltip |
		  ImGuiWindowFlags.NoMove |
		  ImGuiWindowFlags.NoSavedSettings |
		  ImGuiWindowFlags.NoBringToFrontOnFocus |
		  ImGuiWindowFlags.NoDecoration |
		  ImGuiWindowFlags.NoInputs |
		  ImGuiWindowFlags.AlwaysAutoResize;

	private const string TooltipId = "RotationSolverReborn Tooltips";

    /// <summary>
    /// Displays a tooltip when the item is hovered.
    /// </summary>
    public static void HoveredTooltip(string? text)
    {
        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(text))
        {
            ShowTooltip(() => ImGui.Text(text));
        }
    }

    /// <summary>
    /// Displays a tooltip with the specified text.
    /// </summary>
    public static void ShowTooltip(string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            ShowTooltip(() => ImGui.Text(text));
        }
    }

    /// <summary>
    /// Displays a tooltip with the specified action.
    /// </summary>
    public static void ShowTooltip(Action? act)
    {
        if (act == null || Service.Config.ShowTooltips != true)
        {
            return;
        }

        float globalScale = ImGuiHelpers.GlobalScale;

        // Dark elegant tooltip styling
        using var bgCol = ImRaii.PushColor(ImGuiCol.PopupBg, RSRStyle.TooltipBg);
        using var borderCol = ImRaii.PushColor(ImGuiCol.Border, RSRStyle.TooltipBorder);
        using var borderShadow = ImRaii.PushColor(ImGuiCol.BorderShadow, Vector4.Zero);
        using var textCol = ImRaii.PushColor(ImGuiCol.Text, RSRStyle.TextPrimary);
        using var borderSize = ImRaii.PushStyle(ImGuiStyleVar.WindowBorderSize, 1f);
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 6f * globalScale);
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(12, 10) * globalScale);

        ImGui.SetNextWindowSizeConstraints(new Vector2(150, 0) * globalScale, new Vector2(1200, 1500) * globalScale);
        ImGui.SetWindowPos(TooltipId, ImGui.GetIO().MousePos);

        if (ImGui.Begin(TooltipId, TooltipFlag))
        {
            act();
            ImGui.End();
        }
    }
}
