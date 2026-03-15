using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace RotationSolver.UI;

/// <summary>
/// Centralized dark elegant theme for RSR with optional glassmorphism mode.
/// </summary>
internal static class RSRStyle
{
    // ── Glass mode toggle ──
    public static bool GlassEnabled { get; set; } = true;

    // ── Glass tuning ──
    private const float GlassAlpha        = 0.68f;  // Window background opacity
    private const float GlassChildAlpha   = 0.45f;  // Child/card opacity
    private const float GlassBorderAlpha  = 0.18f;  // Border brightness
    private const float GlassHighlight    = 0.08f;  // Top-edge highlight intensity
    private const float GlassShadowAlpha  = 0.35f;  // Drop shadow opacity
    private const float GlassShadowSize   = 8f;     // Drop shadow spread (px)
    private const float GlassRounding     = 12f;    // Corner radius

    // ── Background tiers (deep charcoal) ──
    public static readonly Vector4 BgDeep    = new(0.09f, 0.09f, 0.11f, 1.00f);
    public static readonly Vector4 BgMid     = new(0.13f, 0.13f, 0.15f, 1.00f);
    public static readonly Vector4 BgCard    = new(0.17f, 0.17f, 0.19f, 1.00f);
    public static readonly Vector4 BgRaised  = new(0.21f, 0.21f, 0.24f, 1.00f);

    // ── Accent (muted teal) ──
    public static readonly Vector4 Accent       = new(0.30f, 0.76f, 0.76f, 1.00f);
    public static readonly Vector4 AccentHover  = new(0.38f, 0.86f, 0.86f, 1.00f);
    public static readonly Vector4 AccentActive = new(0.22f, 0.62f, 0.62f, 1.00f);
    public static readonly Vector4 AccentDim    = new(0.30f, 0.76f, 0.76f, 0.45f);
    public static readonly Vector4 AccentSubtle = new(0.30f, 0.76f, 0.76f, 0.15f);

    // ── Text ──
    public static readonly Vector4 TextPrimary   = new(0.90f, 0.91f, 0.93f, 1.00f);
    public static readonly Vector4 TextSecondary  = new(0.58f, 0.59f, 0.63f, 1.00f);
    public static readonly Vector4 TextDisabled   = new(0.38f, 0.39f, 0.43f, 1.00f);

    // ── Sidebar ──
    public static readonly Vector4 SidebarBg     = new(0.07f, 0.07f, 0.09f, 1.00f);
    public static readonly Vector4 SidebarHover  = new(0.15f, 0.15f, 0.18f, 1.00f);
    public static readonly Vector4 SidebarActive = new(0.13f, 0.13f, 0.16f, 1.00f);

    // ── Separator ──
    public static readonly Vector4 SeparatorColor = new(0.22f, 0.22f, 0.26f, 0.60f);

    // ── Tooltip ──
    public static readonly Vector4 TooltipBg     = new(0.11f, 0.11f, 0.14f, 0.96f);
    public static readonly Vector4 TooltipBorder = new(0.30f, 0.76f, 0.76f, 0.35f);

    // ── Section header ──
    public static readonly Vector4 SectionHeaderBg      = new(0.15f, 0.15f, 0.18f, 1.00f);
    public static readonly Vector4 SectionHeaderHover    = new(0.18f, 0.18f, 0.22f, 1.00f);

    // ── Scrollbar ──
    public static readonly Vector4 ScrollBg        = new(0.10f, 0.10f, 0.12f, 0.50f);
    public static readonly Vector4 ScrollGrab      = new(0.26f, 0.26f, 0.30f, 1.00f);
    public static readonly Vector4 ScrollGrabHover = new(0.34f, 0.34f, 0.38f, 1.00f);
    public static readonly Vector4 ScrollGrabActive = new(0.40f, 0.40f, 0.44f, 1.00f);

    // ── Buttons ──
    public static readonly Vector4 Button       = new(0.20f, 0.20f, 0.24f, 1.00f);
    public static readonly Vector4 ButtonHover  = new(0.26f, 0.26f, 0.30f, 1.00f);
    public static readonly Vector4 ButtonActive = new(0.16f, 0.16f, 0.20f, 1.00f);

    // ── Frame (inputs, checkboxes, sliders) ──
    public static readonly Vector4 FrameBg       = new(0.14f, 0.14f, 0.17f, 1.00f);
    public static readonly Vector4 FrameHover    = new(0.19f, 0.19f, 0.22f, 1.00f);
    public static readonly Vector4 FrameActive   = new(0.22f, 0.50f, 0.50f, 0.60f);

    // ── Tabs ──
    public static readonly Vector4 Tab          = new(0.13f, 0.13f, 0.15f, 1.00f);
    public static readonly Vector4 TabHovered   = new(0.26f, 0.60f, 0.60f, 0.80f);
    public static readonly Vector4 TabActive    = new(0.22f, 0.50f, 0.50f, 1.00f);

    // ── Title bar ──
    public static readonly Vector4 TitleBg       = new(0.08f, 0.08f, 0.10f, 1.00f);
    public static readonly Vector4 TitleBgActive = new(0.10f, 0.10f, 0.13f, 1.00f);

    // ── Cached uint colors ──
    private static uint? _accentU32;
    public static uint AccentU32 => _accentU32 ??= ImGui.ColorConvertFloat4ToU32(Accent);

    private static uint? _accentDimU32;
    public static uint AccentDimU32 => _accentDimU32 ??= ImGui.ColorConvertFloat4ToU32(AccentDim);

    private static uint? _accentSubtleU32;
    public static uint AccentSubtleU32 => _accentSubtleU32 ??= ImGui.ColorConvertFloat4ToU32(AccentSubtle);

    private static uint? _sectionHeaderBgU32;
    public static uint SectionHeaderBgU32 => _sectionHeaderBgU32 ??= ImGui.ColorConvertFloat4ToU32(SectionHeaderBg);

    private static uint? _separatorU32;
    public static uint SeparatorU32 => _separatorU32 ??= ImGui.ColorConvertFloat4ToU32(SeparatorColor);

    private static uint? _sidebarBgU32;
    public static uint SidebarBgU32 => _sidebarBgU32 ??= ImGui.ColorConvertFloat4ToU32(SidebarBg);

    // ── Helpers: alpha-blend a color ──
    private static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    /// <summary>
    /// Push the full RSR theme. When GlassEnabled is true, all backgrounds become
    /// semi-transparent so the game world shines through with a frosted-glass feel.
    /// </summary>
    public static ThemeScope PushTheme(float scale = 1f)
    {
        bool glass = GlassEnabled;
        float rounding = glass ? GlassRounding : 8f;

        // ── Style vars ──
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 4) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 14) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(6, 3) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(6, 4) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, 22f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 12f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabMinSize, 11f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, glass ? 1.5f : 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, glass ? 1f : 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, rounding * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, (glass ? 10f : 6f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, (glass ? 8f : 5f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, (glass ? 10f : 6f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 6f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, (glass ? 6f : 4f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, (glass ? 8f : 5f) * scale);

        // ── Colors ──
        if (glass)
        {
            // Glass backgrounds: semi-transparent with slight teal tint
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.09f, 0.12f, GlassAlpha));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.10f, 0.11f, 0.14f, GlassChildAlpha));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.10f, 0.11f, 0.14f, 0.88f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1.0f, 1.0f, 1.0f, GlassBorderAlpha));
            ImGui.PushStyleColor(ImGuiCol.BorderShadow, new Vector4(0f, 0f, 0f, 0.20f));
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.WindowBg, BgDeep);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, BgMid);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, BgMid);
            ImGui.PushStyleColor(ImGuiCol.Border, SeparatorColor);
            ImGui.PushStyleColor(ImGuiCol.BorderShadow, Vector4.Zero);
        }

        if (glass)
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.14f, 0.15f, 0.19f, 0.50f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.20f, 0.22f, 0.26f, 0.60f));
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, FrameBg);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, FrameHover);
        }
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, FrameActive);

        if (glass)
        {
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.06f, 0.07f, 0.10f, 0.80f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.08f, 0.09f, 0.13f, 0.85f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, new Vector4(0.06f, 0.07f, 0.10f, 0.50f));
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.TitleBg, TitleBg);
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, TitleBgActive);
            ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, TitleBg);
        }

        ImGui.PushStyleColor(ImGuiCol.Text, TextPrimary);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, TextDisabled);

        if (glass)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.22f, 0.23f, 0.28f, 0.45f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.28f, 0.30f, 0.36f, 0.60f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.18f, 0.19f, 0.24f, 0.70f));
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Button);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ButtonHover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ButtonActive);
        }

        if (glass)
        {
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.20f, 0.21f, 0.26f, 0.40f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.26f, 0.28f, 0.34f, 0.55f));
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Header, BgCard);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, BgRaised);
        }
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, AccentActive);

        ImGui.PushStyleColor(ImGuiCol.Separator, glass ? new Vector4(1f, 1f, 1f, 0.10f) : SeparatorColor);
        ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, AccentDim);
        ImGui.PushStyleColor(ImGuiCol.SeparatorActive, Accent);

        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, glass ? new Vector4(0.10f, 0.10f, 0.12f, 0.20f) : ScrollBg);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, glass ? WithAlpha(ScrollGrab, 0.60f) : ScrollGrab);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, ScrollGrabHover);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, ScrollGrabActive);

        ImGui.PushStyleColor(ImGuiCol.CheckMark, Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, AccentActive);

        if (glass)
        {
            ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.14f, 0.15f, 0.19f, 0.45f));
            ImGui.PushStyleColor(ImGuiCol.TabHovered, WithAlpha(TabHovered, 0.65f));
            ImGui.PushStyleColor(ImGuiCol.TabActive, WithAlpha(TabActive, 0.75f));
            ImGui.PushStyleColor(ImGuiCol.TabUnfocusedActive, WithAlpha(TabActive, 0.60f));
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Tab, Tab);
            ImGui.PushStyleColor(ImGuiCol.TabHovered, TabHovered);
            ImGui.PushStyleColor(ImGuiCol.TabActive, TabActive);
            ImGui.PushStyleColor(ImGuiCol.TabUnfocusedActive, TabActive);
        }

        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, AccentSubtle);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, AccentDim);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, Accent);

        return new ThemeScope();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Glassmorphism drawing helpers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Draw a glass card effect behind the current window. Call this at the very start
    /// of Draw() BEFORE any content, using the background draw list.
    /// Renders: outer shadow -> frosted fill -> top-edge highlight -> inner glow border.
    /// </summary>
    public static void DrawGlassWindowBackground()
    {
        if (!GlassEnabled) return;

        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var max = pos + size;
        var drawList = ImGui.GetWindowDrawList();
        float rounding = GlassRounding;

        // 1) Drop shadow (layered for soft falloff)
        for (int i = 3; i >= 1; i--)
        {
            float spread = GlassShadowSize * i * 0.4f;
            float alpha = GlassShadowAlpha * (1f - i * 0.25f);
            var shadowOffset = new Vector2(0, 2f * i);
            drawList.AddRectFilled(
                pos + shadowOffset - new Vector2(spread),
                max + shadowOffset + new Vector2(spread),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, alpha)),
                rounding + spread * 0.5f);
        }

        // 2) Top-edge highlight (simulates light hitting the glass from above)
        float highlightHeight = MathF.Min(size.Y * 0.35f, 60f);
        drawList.AddRectFilledMultiColor(
            pos,
            new Vector2(max.X, pos.Y + highlightHeight),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, GlassHighlight)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, GlassHighlight)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0f)));

        // 3) Inner glow border (accent-tinted, subtle)
        drawList.AddRect(
            pos + Vector2.One,
            max - Vector2.One,
            ImGui.ColorConvertFloat4ToU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.06f)),
            rounding - 1f, ImDrawFlags.RoundCornersAll, 1f);
    }

    /// <summary>
    /// Draw a glass card effect for a child region / panel.
    /// Call after BeginChild() to overlay glass effects on the child.
    /// </summary>
    public static void DrawGlassPanel(Vector2 pos, Vector2 size, float rounding = -1f)
    {
        if (!GlassEnabled) return;
        if (rounding < 0) rounding = GlassRounding - 2f;

        var max = pos + size;
        var drawList = ImGui.GetWindowDrawList();

        // Subtle shadow beneath the panel
        drawList.AddRectFilled(
            pos + new Vector2(0, 2),
            max + new Vector2(0, 4),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.18f)),
            rounding);

        // Top highlight gradient
        float hlHeight = MathF.Min(size.Y * 0.3f, 30f);
        drawList.AddRectFilledMultiColor(
            pos,
            new Vector2(max.X, pos.Y + hlHeight),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.05f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.05f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0f)));

        // Bright top-edge line (light refraction)
        drawList.AddLine(
            pos + new Vector2(rounding, 0),
            new Vector2(max.X - rounding, pos.Y),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.12f)),
            1f);

        // Inner accent glow
        drawList.AddRect(pos, max,
            ImGui.ColorConvertFloat4ToU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.04f)),
            rounding, ImDrawFlags.RoundCornersAll, 1f);
    }

    /// <summary>
    /// Draw a glowing glass button background. Use in combination with InvisibleButton
    /// for fully custom button styling.
    /// </summary>
    public static void DrawGlassButton(Vector2 pos, Vector2 size, bool hovered, bool active)
    {
        if (!GlassEnabled) return;

        var max = pos + size;
        var drawList = ImGui.GetWindowDrawList();
        float rounding = 8f;

        // Button fill
        float alpha = active ? 0.55f : hovered ? 0.40f : 0.25f;
        drawList.AddRectFilled(pos, max,
            ImGui.ColorConvertFloat4ToU32(new Vector4(Accent.X, Accent.Y, Accent.Z, alpha * 0.3f)),
            rounding);

        // Top highlight
        drawList.AddRectFilledMultiColor(
            pos,
            new Vector2(max.X, pos.Y + size.Y * 0.45f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, hovered ? 0.10f : 0.05f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, hovered ? 0.10f : 0.05f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0f)));

        // Border
        drawList.AddRect(pos, max,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, hovered ? 0.22f : 0.10f)),
            rounding, ImDrawFlags.RoundCornersAll, 1f);
    }

    /// <summary>
    /// Draw a glass-styled separator with a subtle glow line.
    /// </summary>
    public static void GlassSeparator()
    {
        if (!GlassEnabled)
        {
            ThemedSeparator();
            return;
        }

        ImGui.Dummy(new Vector2(0, 3));
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();

        // Glow line (wider, faded)
        drawList.AddLine(
            pos + new Vector2(width * 0.1f, 0),
            new Vector2(pos.X + width * 0.9f, pos.Y),
            ImGui.ColorConvertFloat4ToU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.12f)),
            3f);

        // Sharp center line
        drawList.AddLine(
            pos + new Vector2(width * 0.05f, 0),
            new Vector2(pos.X + width * 0.95f, pos.Y),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.08f)),
            1f);

        ImGui.Dummy(new Vector2(0, 4));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Original helpers (unchanged)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Draws a vertical accent bar on the left edge (for active sidebar items / section headers).
    /// </summary>
    public static void DrawAccentBar(Vector2 screenPos, float height, float thickness = 3f)
    {
        ImGui.GetWindowDrawList().AddRectFilled(
            screenPos,
            new Vector2(screenPos.X + thickness, screenPos.Y + height),
            AccentU32,
            thickness * 0.5f);
    }

    /// <summary>
    /// Draws a subtle horizontal separator line with theme colors.
    /// </summary>
    public static void ThemedSeparator()
    {
        ImGui.Dummy(new Vector2(0, 2));
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.GetWindowDrawList().AddLine(
            pos,
            new Vector2(pos.X + width, pos.Y),
            SeparatorU32);
        ImGui.Dummy(new Vector2(0, 4));
    }

    public readonly struct ThemeScope : IDisposable
    {
        private const int StyleVarCount = 18;
        private const int StyleColorCount = 36;

        public void Dispose()
        {
            ImGui.PopStyleColor(StyleColorCount);
            ImGui.PopStyleVar(StyleVarCount);
        }
    }
}
