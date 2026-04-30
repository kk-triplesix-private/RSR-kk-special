namespace RotationSolver.UI;

/// <summary>
/// No-op style stub. The custom glassmorphism theme has been removed; this class
/// remains as a compatibility shim so existing call sites still compile but use
/// vanilla ImGui defaults instead of any custom appearance.
/// </summary>
internal static class RSRStyle
{
    public static bool GlassEnabled { get; set; }

    // Default ImGui colors — used as fallbacks where call sites still reference style colors.
    public static readonly Vector4 BgDeep    = new(0.06f, 0.06f, 0.06f, 0.94f);
    public static readonly Vector4 BgMid     = new(0.10f, 0.10f, 0.10f, 1.00f);
    public static readonly Vector4 BgCard    = new(0.16f, 0.16f, 0.16f, 1.00f);
    public static readonly Vector4 Accent       = new(0.26f, 0.59f, 0.98f, 1.00f);
    public static readonly Vector4 AccentHover  = new(0.32f, 0.66f, 1.00f, 1.00f);
    public static readonly Vector4 AccentActive = new(0.06f, 0.53f, 0.98f, 1.00f);
    public static readonly Vector4 AccentDim    = new(0.26f, 0.59f, 0.98f, 0.45f);
    public static readonly Vector4 TextPrimary   = new(1.00f, 1.00f, 1.00f, 1.00f);
    public static readonly Vector4 TextSecondary = new(0.70f, 0.70f, 0.70f, 1.00f);
    public static readonly Vector4 TextDisabled  = new(0.50f, 0.50f, 0.50f, 1.00f);
    public static readonly Vector4 SidebarBg     = new(0.08f, 0.08f, 0.08f, 1.00f);
    public static readonly Vector4 SeparatorColor = new(0.43f, 0.43f, 0.50f, 0.50f);
    public static readonly Vector4 TooltipBg     = new(0.20f, 0.20f, 0.20f, 1.00f);
    public static readonly Vector4 TooltipBorder = new(0.43f, 0.43f, 0.50f, 0.50f);
    public static readonly Vector4 SectionHeaderBg    = new(0.20f, 0.25f, 0.30f, 0.55f);
    public static readonly Vector4 SectionHeaderHover = new(0.26f, 0.59f, 0.98f, 0.40f);

    public static uint AccentU32          => ImGui.ColorConvertFloat4ToU32(Accent);
    public static uint AccentDimU32       => ImGui.ColorConvertFloat4ToU32(AccentDim);
    public static uint SectionHeaderBgU32 => ImGui.ColorConvertFloat4ToU32(SectionHeaderBg);
    public static uint SeparatorU32       => ImGui.ColorConvertFloat4ToU32(SeparatorColor);

    public static IDisposable PushTheme(float scale = 1f) => NoopScope.Instance;

    public static void ThemedSeparator() => ImGui.Separator();

    public static void DrawAccentBar(Vector2 pos, float height) { /* no-op */ }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }
}
