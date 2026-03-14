using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ECommons.GameHelpers;
using RotationSolver.ActionTimeline;
using RotationSolver.RotationPlanner;

namespace RotationSolver.UI;

/// <summary>
/// Fight-specific rotation timeline planner with GCD/oGCD action placement
/// and BossMod mechanic integration
/// </summary>
internal class RotationPlannerWindow : Window
{
    private const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoScrollbar;

    // Timeline display
    private float _pixelsPerSecond = 12f;
    private float _scrollX;
    private const float MinPPS = 2f;
    private const float MaxPPS = 120f;
    private const float PaletteWidth = 200f;
    private const float ToolbarHeight = 32f;
    private const float ToolbarTotalHeight = 68f;
    private const float MechanicLaneHeight = 40f;
    private const float GCDLaneHeight = 48f;
    private const float OGCDLaneHeight = 36f;
    private const float PropertiesHeight = 0f; // Collapsed by default
    private const float GCDIconSize = 40f;
    private const float OGCDIconSize = 30f;

    // State
    private RotationPlan _currentPlan = new();
    private List<RotationPlan> _allPlans = [];
    private int _selectedPlanIndex = -1;
    private string _newPlanName = "";
    private string _paletteFilter = "";
    private Guid? _selectedActionId;
    private bool _isPanning;
    private float _panStartX;
    private float _panStartScrollX;

    // Encounter selection
    private List<EncounterInfo> _encounters = [];
    private int _selectedEncounterIndex = -1;
    private string _encounterFilter = "";

    // Manual drag state (cross-window drag-drop)
    private bool _isDraggingFromPalette;
    private uint _draggingActionId;
    private uint _draggingIconId;
    private string _draggingActionName = "";
    private bool _draggingIsGCD;

    // Precast colors
    private static readonly Vector4 PrecastZoneColor = new(0.15f, 0.12f, 0.20f, 0.50f);
    private static readonly Vector4 PullMarkerColor = new(0.20f, 1.0f, 0.30f, 0.90f);
    private static readonly Vector4 CastBarColor = new(1.0f, 0.85f, 0.30f, 0.60f);
    private static readonly Vector4 CastBarBorderColor = new(1.0f, 0.85f, 0.30f, 0.90f);
    private static readonly Vector4 MechanicLinkColor = new(1.0f, 0.50f, 0.30f, 0.50f);

    // Mechanic colors
    private static readonly Vector4 RaidwideColor = new(0.90f, 0.30f, 0.30f, 0.70f);
    private static readonly Vector4 TankbusterColor = new(0.95f, 0.60f, 0.20f, 0.70f);
    private static readonly Vector4 StackColor = new(0.30f, 0.50f, 0.90f, 0.70f);
    private static readonly Vector4 KnockbackColor = new(0.80f, 0.80f, 0.20f, 0.70f);
    private static readonly Vector4 DowntimeColor = new(0.50f, 0.50f, 0.50f, 0.50f);
    private static readonly Vector4 PositioningColor = new(0.60f, 0.40f, 0.80f, 0.70f);
    private static readonly Vector4 VulnerableColor = new(0.30f, 0.85f, 0.40f, 0.70f);
    private static readonly Vector4 BossCastColor = new(0.85f, 0.85f, 0.40f, 0.50f);
    private static readonly Vector4 CustomColor = new(0.70f, 0.70f, 0.70f, 0.50f);

    public RotationPlannerWindow() : base("Rotation Planner", BaseFlags)
    {
        Size = new Vector2(1200, 500);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnClose()
    {
        Service.Config.ShowRotationPlannerWindow.Value = false;
        base.OnClose();
    }

    public override void PreDraw()
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, RSRStyle.BgDeep);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
        base.PreDraw();
    }

    public override void PostDraw()
    {
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        base.PostDraw();
    }

    public override void Draw()
    {
        using var theme = RSRStyle.PushTheme();

        var windowSize = ImGui.GetContentRegionAvail();

        // Toolbar
        DrawToolbar(windowSize.X);

        // Main content: Palette + Timeline
        var contentHeight = windowSize.Y - ToolbarTotalHeight;
        DrawPalette(contentHeight);
        ImGui.SameLine();
        DrawTimelineCanvas(windowSize.X - PaletteWidth - 8, contentHeight);

        // Draw floating drag cursor on foreground
        if (_isDraggingFromPalette)
        {
            var fg = ImGui.GetForegroundDrawList();
            var mouse = ImGui.GetMousePos();
            float iconSize = _draggingIsGCD ? GCDIconSize : OGCDIconSize;

            if (IconSet.GetTexture(_draggingIconId, out IDalamudTextureWrap? tex) && tex != null)
            {
                fg.AddImage(tex.Handle,
                    mouse - new Vector2(iconSize / 2, iconSize / 2),
                    mouse + new Vector2(iconSize / 2, iconSize / 2));
            }
            else
            {
                fg.AddRectFilled(
                    mouse - new Vector2(iconSize / 2, iconSize / 2),
                    mouse + new Vector2(iconSize / 2, iconSize / 2),
                    RSRStyle.AccentU32, 4f);
            }

            fg.AddText(mouse + new Vector2(iconSize / 2 + 4, -8),
                ImGui.ColorConvertFloat4ToU32(RSRStyle.TextPrimary), _draggingActionName);

            // End drag on mouse release
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _isDraggingFromPalette = false;
            }
        }
    }

    #region Toolbar

    private void DrawToolbar(float width)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, RSRStyle.BgMid);
        ImGui.BeginChild("##PlannerToolbar", new Vector2(width, ToolbarTotalHeight), false, ImGuiWindowFlags.NoScrollbar);

        bool bossModAvailable = BossModTimelineProvider.IsAvailable;

        // Lazy-load encounters
        if (_encounters.Count == 0 && bossModAvailable)
            _encounters = BossModTimelineProvider.GetEncounters();

        // ===== ROW 1 =====
        ImGui.BeginChild("##ToolbarRow1", new Vector2(width - 4, ToolbarHeight), false, ImGuiWindowFlags.NoScrollbar);
        ImGui.SetCursorPos(new Vector2(4, 4));

        // Encounter dropdown
        if (!bossModAvailable) ImGui.BeginDisabled();
        ImGui.Text("Encounter:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(280);
        var encounterLabel = _selectedEncounterIndex >= 0 && _selectedEncounterIndex < _encounters.Count
            ? _encounters[_selectedEncounterIndex].DisplayName
            : "-- Encounter --";
        if (ImGui.BeginCombo("##EncounterSelect", encounterLabel))
        {
            ImGui.SetNextItemWidth(260);
            ImGui.InputTextWithHint("##EncFilter", "Filter...", ref _encounterFilter, 64);
            var filterLower = _encounterFilter.ToLowerInvariant();
            string lastCategory = "";
            for (int i = 0; i < _encounters.Count; i++)
            {
                var enc = _encounters[i];
                if (!string.IsNullOrEmpty(filterLower)
                    && !enc.BossName.ToLowerInvariant().Contains(filterLower)
                    && !enc.GroupName.ToLowerInvariant().Contains(filterLower)
                    && !enc.Category.ToLowerInvariant().Contains(filterLower))
                    continue;
                if (enc.Category != lastCategory)
                {
                    lastCategory = enc.Category;
                    ImGui.TextColored(RSRStyle.Accent, enc.Category);
                    RSRStyle.ThemedSeparator();
                }
                bool selected = i == _selectedEncounterIndex;
                if (ImGui.Selectable($"  {enc.DisplayName}##{i}", selected))
                {
                    _selectedEncounterIndex = i;
                    AutoGeneratePlanName();
                }
            }
            ImGui.EndCombo();
        }
        if (!bossModAvailable) ImGui.EndDisabled();
        if (!bossModAvailable && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("BossModReborn nicht geladen");

        ImGui.SameLine(0, 16);

        // Plan selector
        ImGui.Text("Plan:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200);
        var currentPlanName = _selectedPlanIndex >= 0 && _selectedPlanIndex < _allPlans.Count
            ? _allPlans[_selectedPlanIndex].Name : "-- Plan --";
        if (ImGui.BeginCombo("##PlanSelect", currentPlanName))
        {
            for (int i = 0; i < _allPlans.Count; i++)
            {
                bool selected = i == _selectedPlanIndex;
                var plan = _allPlans[i];
                string label = plan.IsActive ? $"[ON] {plan.Name}" : plan.Name;
                if (ImGui.Selectable(label, selected))
                {
                    _selectedPlanIndex = i;
                    _currentPlan = plan;
                    _selectedActionId = null;
                    if (plan.EncounterOID != 0)
                        _selectedEncounterIndex = _encounters.FindIndex(e => e.OID == plan.EncounterOID);
                }
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine(0, 16);
        if (_selectedPlanIndex >= 0)
        {
            bool isActive = _currentPlan.IsActive;
            if (ImGui.Checkbox("Aktiv", ref isActive))
            {
                _currentPlan.IsActive = isActive;
                RotationPlanStorage.Save(_currentPlan);
            }
        }

        ImGui.EndChild();

        // ===== ROW 2 =====
        ImGui.BeginChild("##ToolbarRow2", new Vector2(width - 4, ToolbarHeight), false, ImGuiWindowFlags.NoScrollbar);
        ImGui.SetCursorPos(new Vector2(4, 4));

        ImGui.SetNextItemWidth(150);
        ImGui.InputTextWithHint("##NewPlanName", "Plan-Name...", ref _newPlanName, 64);

        ImGui.SameLine();
        if (ImGui.Button("Neu"))
        {
            if (string.IsNullOrWhiteSpace(_newPlanName))
                AutoGeneratePlanName();
            if (!string.IsNullOrWhiteSpace(_newPlanName))
                CreateNewPlan();
        }

        ImGui.SameLine();
        if (ImGui.Button("Speichern") && _currentPlan.Name.Length > 0)
            RotationPlanStorage.Save(_currentPlan);

        ImGui.SameLine();
        if (ImGui.Button("Loeschen") && _selectedPlanIndex >= 0)
        {
            RotationPlanStorage.Delete(_currentPlan.TerritoryId, _currentPlan.Job, _currentPlan.Name);
            RefreshPlanList();
            _selectedPlanIndex = -1;
            _currentPlan = new();
        }

        ImGui.SameLine(0, 16);
        if (!bossModAvailable) ImGui.BeginDisabled();
        if (ImGui.Button("Import"))
        {
            if (_selectedEncounterIndex >= 0 && _selectedEncounterIndex < _encounters.Count)
            {
                var enc = _encounters[_selectedEncounterIndex];
                BossModTimelineProvider.TryPopulateForOID(_currentPlan, enc.OID);
                _currentPlan.EncounterOID = enc.OID;
                _currentPlan.EncounterName = enc.DisplayName;
            }
            else
            {
                BossModTimelineProvider.TryPopulate(_currentPlan);
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Reload"))
        {
            BossModTimelineProvider.InvalidateEncounterCache();
            _encounters = BossModTimelineProvider.GetEncounters();
        }
        if (!bossModAvailable) ImGui.EndDisabled();

        ImGui.SameLine(0, 16);
        ImGui.Text("Precast:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(60);
        float precast = _currentPlan.PrecastTime;
        if (ImGui.DragFloat("##Precast", ref precast, 0.5f, 0f, 30f, "%.0fs"))
        {
            _currentPlan.PrecastTime = Math.Max(0, precast);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Prepull-Zeit: Sekunden vor dem Pull\nfuer Precast-Skills (z.B. Countdown)");

        ImGui.SameLine(0, 16);
        ImGui.Text("Zoom:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.SliderFloat("##Zoom", ref _pixelsPerSecond, MinPPS, MaxPPS, "%.0f px/s", ImGuiSliderFlags.Logarithmic);

        ImGui.EndChild();

        ImGui.EndChild(); // PlannerToolbar
        ImGui.PopStyleColor();
    }

    private void AutoGeneratePlanName()
    {
        string rotationName = DataCenter.CurrentRotation?.GetType().Name ?? "Default";
        string encounterShort = "";

        if (_selectedEncounterIndex >= 0 && _selectedEncounterIndex < _encounters.Count)
        {
            var enc = _encounters[_selectedEncounterIndex];
            // Generate short name like "M10S", "E12S", etc. from the boss/group name
            encounterShort = enc.ShortName;
            // Try to extract a short identifier (e.g. "M10S" from group name)
            if (enc.GroupName.Length > 0)
            {
                // Use category shorthand for common fight types
                var shortId = enc.Category switch
                {
                    "Savage" => ExtractFightId(enc.GroupName, enc.SortOrder, "S"),
                    "Extreme" => ExtractFightId(enc.GroupName, enc.SortOrder, "EX"),
                    "Ultimate" => enc.BossName,
                    _ => enc.BossName
                };
                if (!string.IsNullOrEmpty(shortId))
                    encounterShort = shortId;
            }
        }

        _newPlanName = string.IsNullOrEmpty(encounterShort)
            ? rotationName
            : $"{rotationName}-{encounterShort}";
    }

    private static string ExtractFightId(string groupName, int sortOrder, string suffix)
    {
        // Try to create identifiers like "M10S", "P12S", etc.
        // Use first letter of group + sort order + suffix
        if (groupName.Length == 0) return "";
        char prefix = char.ToUpper(groupName[0]);
        return sortOrder > 0 ? $"{prefix}{sortOrder}{suffix}" : $"{prefix}{suffix}";
    }

    #endregion

    #region Action Palette

    private void DrawPalette(float height)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, RSRStyle.SidebarBg);
        ImGui.BeginChild("##ActionPalette", new Vector2(PaletteWidth, height), true);

        ImGui.SetCursorPos(new Vector2(8, 8));
        ImGui.SetNextItemWidth(PaletteWidth - 16);
        ImGui.InputTextWithHint("##PaletteFilter", "Search...", ref _paletteFilter, 64);

        var rotation = DataCenter.CurrentRotation;
        if (rotation == null)
        {
            ImGui.SetCursorPosX(8);
            ImGui.TextColored(RSRStyle.TextSecondary, "No rotation loaded");
            ImGui.EndChild();
            ImGui.PopStyleColor();
            return;
        }

        var actions = rotation.AllBaseActions;
        var filterLower = _paletteFilter.ToLowerInvariant();

        // GCD section
        ImGui.SetCursorPosX(8);
        ImGui.TextColored(RSRStyle.Accent, "GCD");
        RSRStyle.ThemedSeparator();
        DrawPaletteActions(actions, true, filterLower);

        ImGui.Spacing();

        // oGCD section
        ImGui.SetCursorPosX(8);
        ImGui.TextColored(RSRStyle.Accent, "oGCD");
        RSRStyle.ThemedSeparator();
        DrawPaletteActions(actions, false, filterLower);

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawPaletteActions(IEnumerable<RotationSolver.Basic.Actions.IBaseAction> actions, bool isGCD, string filter)
    {
        int idx = 0;
        foreach (var action in actions)
        {
            bool actionIsGCD = action.Info.IsRealGCD;
            if (actionIsGCD != isGCD) continue;

            string name = action.Name;
            if (!string.IsNullOrEmpty(filter) && !name.ToLowerInvariant().Contains(filter)) continue;

            ImGui.SetCursorPosX(8);

            float iconSize = isGCD ? 28 : 24;

            // InvisibleButton captures mouse input (prevents window drag)
            var btnPos = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton($"##palAction_{(isGCD ? "g" : "o")}_{idx}", new Vector2(iconSize, iconSize));
            idx++;

            // Draw icon on top via draw list
            var dl = ImGui.GetWindowDrawList();
            if (IconSet.GetTexture(action.IconID, out IDalamudTextureWrap? texture) && texture != null)
            {
                dl.AddImage(texture.Handle, btnPos, btnPos + new Vector2(iconSize, iconSize));
            }
            else
            {
                var fallbackColor = isGCD ? RSRStyle.Accent : RSRStyle.AccentDim;
                dl.AddRectFilled(btnPos, btnPos + new Vector2(iconSize), ImGui.ColorConvertFloat4ToU32(fallbackColor), 4f);
            }

            // Start drag on mouse down + drag
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                _isDraggingFromPalette = true;
                _draggingActionId = action.ID;
                _draggingIconId = action.IconID;
                _draggingActionName = name;
                _draggingIsGCD = actionIsGCD;
            }

            ImGui.SameLine();
            ImGui.Text(name);
        }
    }

    #endregion

    #region Timeline Canvas

    private void DrawTimelineCanvas(float width, float height)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, RSRStyle.BgDeep);
        ImGui.BeginChild("##TimelineCanvas", new Vector2(width, height), false);

        var canvasPos = ImGui.GetCursorScreenPos();
        var canvasSize = new Vector2(width, height);
        var drawList = ImGui.GetWindowDrawList();

        float precast = _currentPlan.PrecastTime;
        float totalSeconds = Math.Max(_currentPlan.TotalDuration, 600f);
        float totalWidth = (totalSeconds + precast) * _pixelsPerSecond;

        // InvisibleButton FIRST as base interactable (drop target + input)
        ImGui.SetCursorScreenPos(canvasPos);
        ImGui.InvisibleButton("##CanvasDrop", canvasSize);
        bool canvasHovered = ImGui.IsItemHovered();

        // Manual drop detection (cross-window drag)
        if (_isDraggingFromPalette && canvasHovered && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            float dropTime = XToTime(canvasPos.X, ImGui.GetMousePos().X);
            dropTime = Math.Max(-precast, dropTime);

            if (_draggingIsGCD)
            {
                // Prepull GCDs snap to GCD grid relative to t=0
                dropTime = SnapToGCD(dropTime);
            }
            else
            {
                dropTime = MathF.Round(dropTime * 10f) / 10f;
            }

            AddActionToPlan(_draggingActionId, dropTime);
            _isDraggingFromPalette = false;
        }

        // Handle mouse interactions (zoom, pan, select, context menu)
        HandleCanvasInput(canvasPos, canvasSize, totalWidth, canvasHovered);

        // Draw layers on top via draw list
        DrawPrecastZone(drawList, canvasPos, canvasSize);
        DrawGrid(drawList, canvasPos, canvasSize, totalSeconds);
        DrawPullMarker(drawList, canvasPos, canvasSize);
        DrawPhaseSeparators(drawList, canvasPos, canvasSize);
        DrawMechanicLane(drawList, canvasPos, canvasSize);
        DrawGCDLane(drawList, canvasPos, canvasSize);
        DrawOGCDLane(drawList, canvasPos, canvasSize);
        DrawMechanicLinks(drawList, canvasPos, canvasSize);
        DrawCurrentTimeLine(drawList, canvasPos, canvasSize);
        DrawBossDeathMarker(drawList, canvasPos, canvasSize);

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void HandleCanvasInput(Vector2 pos, Vector2 size, float totalWidth, bool isHovered)
    {
        var io = ImGui.GetIO();
        var mousePos = io.MousePos;

        if (!isHovered) return;

        // Mouse wheel zoom (proportional to current zoom level)
        if (io.MouseWheel != 0)
        {
            float oldPPS = _pixelsPerSecond;
            float zoomStep = _pixelsPerSecond * 0.15f;
            _pixelsPerSecond = Math.Clamp(_pixelsPerSecond + io.MouseWheel * zoomStep, MinPPS, MaxPPS);

            // Adjust scroll to keep mouse position stable
            float precast = _currentPlan.PrecastTime;
            float mouseTime = (mousePos.X - pos.X + _scrollX) / oldPPS - precast;
            _scrollX = (mouseTime + precast) * _pixelsPerSecond - (mousePos.X - pos.X);
        }

        // Middle mouse pan
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
        {
            _isPanning = true;
            _panStartX = mousePos.X;
            _panStartScrollX = _scrollX;
        }
        if (_isPanning && ImGui.IsMouseDown(ImGuiMouseButton.Middle))
        {
            _scrollX = _panStartScrollX - (mousePos.X - _panStartX);
        }
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Middle))
        {
            _isPanning = false;
        }

        // Clamp scroll
        float maxScroll = Math.Max(0, totalWidth - size.X);
        _scrollX = Math.Clamp(_scrollX, 0, maxScroll);

        // Click to select/deselect actions
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            float clickTime = XToTime(pos.X, mousePos.X);
            _selectedActionId = FindActionAtTime(clickTime, mousePos.Y - pos.Y);
        }

        // Right-click context menu
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            float clickTime = XToTime(pos.X, mousePos.X);
            var clickedAction = FindActionAtTime(clickTime, mousePos.Y - pos.Y);
            if (clickedAction.HasValue)
            {
                _selectedActionId = clickedAction;
                ImGui.OpenPopup("##ActionContextMenu");
            }
        }

        // Context menu
        if (ImGui.BeginPopup("##ActionContextMenu"))
        {
            if (_selectedActionId.HasValue)
            {
                var action = _currentPlan.Actions.Find(a => a.Id == _selectedActionId.Value);
                if (action != null)
                {
                    ImGui.Text(action.ActionName);
                    RSRStyle.ThemedSeparator();

                    float time = action.CombatTime;
                    float precast = _currentPlan.PrecastTime;
                    ImGui.SetNextItemWidth(100);
                    if (ImGui.DragFloat("Zeit (s)", ref time, 0.1f, -precast, _currentPlan.TotalDuration))
                    {
                        action.CombatTime = time;
                    }

                    float castTime = action.CastTime;
                    ImGui.SetNextItemWidth(100);
                    if (ImGui.DragFloat("Castzeit (s)", ref castTime, 0.1f, 0, 5f))
                    {
                        action.CastTime = castTime;
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Castzeit des Skills. Bestimmt wann der\nSkill aktiviert werden muss (vorher druecken).");

                    // Show nearest mechanic info
                    var nearestMech = FindNearestMechanic(action.EffectTime);
                    if (nearestMech != null)
                    {
                        float delta = nearestMech.CombatTime - action.EffectTime;
                        ImGui.TextColored(GetMechanicColor(nearestMech.Type),
                            $"{delta:F1}s vor {nearestMech.Name}");
                    }

                    string comment = action.Comment ?? "";
                    ImGui.SetNextItemWidth(150);
                    if (ImGui.InputText("Kommentar", ref comment, 128))
                    {
                        action.Comment = string.IsNullOrEmpty(comment) ? null : comment;
                    }

                    if (ImGui.MenuItem("Loeschen"))
                    {
                        _currentPlan.Actions.RemoveAll(a => a.Id == _selectedActionId.Value);
                        _selectedActionId = null;
                    }
                }
            }
            ImGui.EndPopup();
        }
    }

    private Guid? FindActionAtTime(float time, float y)
    {
        float gcdLaneY = MechanicLaneHeight;
        float ogcdLaneY = MechanicLaneHeight + GCDLaneHeight;

        foreach (var action in _currentPlan.Actions)
        {
            float actionX = action.CombatTime;
            float duration = action.Duration > 0 ? action.Duration : 2.5f;

            if (time >= actionX && time <= actionX + duration)
            {
                bool isGCD = action.Type == TimelineItemType.GCD;
                float laneY = isGCD ? gcdLaneY : ogcdLaneY;
                float laneH = isGCD ? GCDLaneHeight : OGCDLaneHeight;

                if (y >= laneY && y <= laneY + laneH)
                    return action.Id;
            }
        }
        return null;
    }

    private void AddActionToPlan(uint actionId, float combatTime)
    {
        var rotation = DataCenter.CurrentRotation;
        if (rotation == null) return;

        var baseAction = rotation.AllBaseActions.FirstOrDefault(a => a.ID == actionId);
        if (baseAction == null) return;

        bool isGCD = baseAction.Info.IsRealGCD;
        float gcd = ActionTimelineManager.Instance.GCD;

        // Get cast time from action info (BaseCastTime is in 100ms units in Lumina)
        float castTime = 0f;
        try
        {
            var castTime100ms = baseAction.Info.CastTime;
            if (castTime100ms > 0)
                castTime = castTime100ms;
        }
        catch { /* fallback to 0 */ }

        var planned = new PlannedAction
        {
            ActionId = actionId,
            ActionName = baseAction.Name,
            IconId = baseAction.IconID,
            Type = isGCD ? TimelineItemType.GCD : TimelineItemType.OGCD,
            CombatTime = combatTime,
            Duration = isGCD ? gcd : 0.6f,
            CastTime = castTime
        };

        _currentPlan.Actions.Add(planned);
        _currentPlan.Actions.Sort((a, b) => a.CombatTime.CompareTo(b.CombatTime));
    }

    #endregion

    #region Drawing

    private void DrawPrecastZone(ImDrawListPtr drawList, Vector2 pos, Vector2 size)
    {
        float precast = _currentPlan.PrecastTime;
        if (precast <= 0) return;

        float x0 = TimeToX(pos.X, -precast);
        float x1 = TimeToX(pos.X, 0);

        // Clamp to canvas
        float drawX0 = Math.Max(x0, pos.X);
        float drawX1 = Math.Min(x1, pos.X + size.X);
        if (drawX0 >= drawX1) return;

        // Dark overlay for precast zone
        drawList.AddRectFilled(
            new Vector2(drawX0, pos.Y),
            new Vector2(drawX1, pos.Y + size.Y),
            ImGui.ColorConvertFloat4ToU32(PrecastZoneColor));

        // "PREPULL" label
        if (x0 >= pos.X)
        {
            drawList.AddText(new Vector2(x0 + 4, pos.Y + size.Y - 16),
                ImGui.ColorConvertFloat4ToU32(RSRStyle.TextDisabled), "PREPULL");
        }
    }

    private void DrawPullMarker(ImDrawListPtr drawList, Vector2 pos, Vector2 size)
    {
        float x = TimeToX(pos.X, 0);
        if (x < pos.X || x > pos.X + size.X) return;

        uint pullColor = ImGui.ColorConvertFloat4ToU32(PullMarkerColor);

        // Thick vertical line at t=0
        drawList.AddLine(new Vector2(x, pos.Y), new Vector2(x, pos.Y + size.Y), pullColor, 3f);

        // "PULL" label
        drawList.AddText(new Vector2(x + 4, pos.Y + 4), pullColor, "PULL");
    }

    private void DrawGrid(ImDrawListPtr drawList, Vector2 pos, Vector2 size, float totalSeconds)
    {
        float precast = _currentPlan.PrecastTime;
        uint gridColor = RSRStyle.SeparatorU32;
        uint gridColorMinor = ImGui.ColorConvertFloat4ToU32(RSRStyle.SeparatorColor with { W = 0.15f });
        uint textColor = ImGui.ColorConvertFloat4ToU32(RSRStyle.TextDisabled);

        // Major grid interval based on zoom
        int majorInterval = _pixelsPerSecond >= 60 ? 5
            : _pixelsPerSecond >= 30 ? 10
            : _pixelsPerSecond >= 15 ? 15
            : _pixelsPerSecond >= 8 ? 30
            : 60;

        float startTime = -precast;

        // Minor grid (1s lines) when zoomed in enough
        if (_pixelsPerSecond >= 30)
        {
            for (float t = MathF.Ceiling(startTime); t <= totalSeconds; t += 1f)
            {
                if (t % majorInterval == 0) continue;
                float x = TimeToX(pos.X, t);
                if (x < pos.X || x > pos.X + size.X) continue;
                drawList.AddLine(new Vector2(x, pos.Y), new Vector2(x, pos.Y + size.Y), gridColorMinor);
            }
        }

        // Major grid lines with labels (including negative time)
        float firstMajor = MathF.Ceiling(startTime / majorInterval) * majorInterval;
        for (float t = firstMajor; t <= totalSeconds; t += majorInterval)
        {
            if (MathF.Abs(t) < 0.01f) continue; // Skip t=0, drawn by PullMarker
            float x = TimeToX(pos.X, t);
            if (x < pos.X || x > pos.X + size.X) continue;

            drawList.AddLine(new Vector2(x, pos.Y), new Vector2(x, pos.Y + size.Y), gridColor);

            string label;
            if (t < 0)
            {
                label = $"{t:F0}s";
            }
            else
            {
                int minutes = (int)t / 60;
                int seconds = (int)t % 60;
                label = minutes > 0 ? $"{minutes}:{seconds:D2}" : $"{seconds}s";
            }
            drawList.AddText(new Vector2(x + 2, pos.Y + 2), textColor, label);
        }
    }

    private void DrawPhaseSeparators(ImDrawListPtr drawList, Vector2 pos, Vector2 size)
    {
        uint phaseColor = ImGui.ColorConvertFloat4ToU32(RSRStyle.Accent with { W = 0.50f });
        uint textColor = ImGui.ColorConvertFloat4ToU32(RSRStyle.Accent);

        foreach (var phase in _currentPlan.Phases)
        {
            float x = TimeToX(pos.X, phase.StartTime);
            if (x < pos.X || x > pos.X + size.X) continue;

            // Dashed line
            for (float y = pos.Y; y < pos.Y + size.Y; y += 8)
            {
                drawList.AddLine(new Vector2(x, y), new Vector2(x, Math.Min(y + 4, pos.Y + size.Y)), phaseColor, 2f);
            }

            // Phase name
            drawList.AddText(new Vector2(x + 4, pos.Y + MechanicLaneHeight - 14), textColor, phase.Name);
        }
    }

    private void DrawMechanicLane(ImDrawListPtr drawList, Vector2 pos, Vector2 size)
    {
        // Lane background
        drawList.AddRectFilled(pos, new Vector2(pos.X + size.X, pos.Y + MechanicLaneHeight),
            ImGui.ColorConvertFloat4ToU32(RSRStyle.BgMid with { W = 0.40f }));

        // Lane label with mechanic count
        var mechanics = _currentPlan.Mechanics;
        string laneLabel = mechanics.Count > 0
            ? $"Mechaniken ({mechanics.Count})"
            : "Mechaniken (Import fuer Daten)";
        drawList.AddText(new Vector2(pos.X + 4, pos.Y + 2),
            ImGui.ColorConvertFloat4ToU32(RSRStyle.TextDisabled), laneLabel);

        foreach (var mech in mechanics)
        {
            float x = TimeToX(pos.X, mech.CombatTime);
            float w = Math.Max(mech.Duration * _pixelsPerSecond, 6f);
            if (x + w < pos.X || x > pos.X + size.X) continue;

            var color = GetMechanicColor(mech.Type);
            float y = pos.Y + 16;
            float h = MechanicLaneHeight - 18;

            // Draw marker
            drawList.AddRectFilled(new Vector2(x, y), new Vector2(x + w, y + h),
                ImGui.ColorConvertFloat4ToU32(color), 3f);

            // Outline for important mechanics
            if (mech.Type is MechanicType.Raidwide or MechanicType.Tankbuster)
            {
                drawList.AddRect(new Vector2(x, y), new Vector2(x + w, y + h),
                    ImGui.ColorConvertFloat4ToU32(color with { W = 1.0f }), 3f, ImDrawFlags.None, 1.5f);
            }

            // Label
            if (w > 30)
            {
                var labelColor = ImGui.ColorConvertFloat4ToU32(RSRStyle.TextPrimary);
                drawList.AddText(new Vector2(x + 3, y + 1), labelColor, TruncateText(mech.Name, w - 6));
            }

            // Tooltip
            var mousePos = ImGui.GetMousePos();
            if (mousePos.X >= x && mousePos.X <= x + w && mousePos.Y >= y && mousePos.Y <= y + h)
            {
                ImGui.BeginTooltip();
                ImGui.TextColored(GetMechanicColor(mech.Type), mech.Type.ToString());
                ImGui.SameLine();
                ImGui.Text(mech.Name);
                ImGui.Text($"Zeit: {FormatTime(mech.CombatTime)}");
                if (mech.Duration > 0) ImGui.Text($"Dauer: {mech.Duration:F1}s");
                ImGui.EndTooltip();
            }
        }
    }

    private void DrawGCDLane(ImDrawListPtr drawList, Vector2 pos, Vector2 size)
    {
        float laneY = pos.Y + MechanicLaneHeight;

        // Lane background
        drawList.AddRectFilled(new Vector2(pos.X, laneY),
            new Vector2(pos.X + size.X, laneY + GCDLaneHeight),
            ImGui.ColorConvertFloat4ToU32(RSRStyle.BgCard with { W = 0.20f }));

        // Draw GCD slot boxes
        float gcd = GetCurrentGCD();
        if (gcd > 0)
        {
            uint slotColorA = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.25f, 0.30f, 0.40f));
            uint slotColorB = ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.20f, 0.25f, 0.40f));
            uint slotBorder = ImGui.ColorConvertFloat4ToU32(RSRStyle.SeparatorColor with { W = 0.50f });
            uint slotNumColor = ImGui.ColorConvertFloat4ToU32(RSRStyle.TextDisabled with { W = 0.60f });

            float totalDuration = Math.Max(_currentPlan.TotalDuration, 600f);
            int slotIndex = 0;
            for (float t = 0; t < totalDuration; t += gcd)
            {
                float x1 = TimeToX(pos.X, t);
                float x2 = TimeToX(pos.X, t + gcd);

                // Cull off-screen slots
                if (x2 < pos.X) { slotIndex++; continue; }
                if (x1 > pos.X + size.X) break;

                // Clamp to canvas
                float drawX1 = Math.Max(x1, pos.X);
                float drawX2 = Math.Min(x2, pos.X + size.X);

                // Alternating slot fill
                uint fillColor = (slotIndex % 2 == 0) ? slotColorA : slotColorB;
                drawList.AddRectFilled(
                    new Vector2(drawX1, laneY),
                    new Vector2(drawX2, laneY + GCDLaneHeight),
                    fillColor);

                // Slot border (left edge)
                if (x1 >= pos.X && x1 <= pos.X + size.X)
                {
                    drawList.AddLine(
                        new Vector2(x1, laneY),
                        new Vector2(x1, laneY + GCDLaneHeight),
                        slotBorder);
                }

                // Slot number at top
                float slotWidth = x2 - x1;
                if (slotWidth > 16 && x1 >= pos.X - slotWidth)
                {
                    string slotLabel = $"{slotIndex + 1}";
                    drawList.AddText(new Vector2(Math.Max(x1, pos.X) + 2, laneY + 1), slotNumColor, slotLabel);
                }

                slotIndex++;
            }

            // GCD info label
            string gcdLabel = $"GCD {gcd:F2}s";
            drawList.AddText(new Vector2(pos.X + 4, laneY + GCDLaneHeight - 14),
                ImGui.ColorConvertFloat4ToU32(RSRStyle.TextDisabled), gcdLabel);
        }
        else
        {
            drawList.AddText(new Vector2(pos.X + 4, laneY + 2),
                ImGui.ColorConvertFloat4ToU32(RSRStyle.TextDisabled), "GCD (kein GCD-Wert)");
        }

        // Draw GCD actions
        DrawActionsInLane(drawList, pos, laneY, GCDLaneHeight, GCDIconSize, TimelineItemType.GCD);
    }

    private void DrawOGCDLane(ImDrawListPtr drawList, Vector2 pos, Vector2 size)
    {
        float laneY = pos.Y + MechanicLaneHeight + GCDLaneHeight;

        // Lane background
        drawList.AddRectFilled(new Vector2(pos.X, laneY),
            new Vector2(pos.X + size.X, laneY + OGCDLaneHeight),
            ImGui.ColorConvertFloat4ToU32(RSRStyle.BgMid with { W = 0.15f }));

        // Lane label
        drawList.AddText(new Vector2(pos.X + 4, laneY + 2),
            ImGui.ColorConvertFloat4ToU32(RSRStyle.TextDisabled), "oGCD");

        // Draw oGCD actions
        DrawActionsInLane(drawList, pos, laneY, OGCDLaneHeight, OGCDIconSize, TimelineItemType.OGCD);
    }

    private void DrawActionsInLane(ImDrawListPtr drawList, Vector2 canvasPos, float laneY, float laneHeight, float iconSize, TimelineItemType type)
    {
        uint castBarU32 = ImGui.ColorConvertFloat4ToU32(CastBarColor);
        uint castBarBorderU32 = ImGui.ColorConvertFloat4ToU32(CastBarBorderColor);

        foreach (var action in _currentPlan.Actions)
        {
            if (action.Type != type) continue;

            float x = TimeToX(canvasPos.X, action.CombatTime);
            if (x + iconSize < canvasPos.X || x > canvasPos.X + ImGui.GetWindowSize().X) continue;

            float iconY = laneY + (laneHeight - iconSize) / 2;
            bool isSelected = _selectedActionId == action.Id;

            // Cast bar (extends left from action showing when to start pressing)
            if (action.CastTime > 0)
            {
                float castBarWidth = action.CastTime * _pixelsPerSecond;
                float castBarX = x - castBarWidth;
                float castBarY = iconY + iconSize * 0.3f;
                float castBarH = iconSize * 0.4f;

                // Cast bar fill
                drawList.AddRectFilled(
                    new Vector2(castBarX, castBarY),
                    new Vector2(x, castBarY + castBarH),
                    castBarU32, 2f);

                // Cast bar border
                drawList.AddRect(
                    new Vector2(castBarX, castBarY),
                    new Vector2(x, castBarY + castBarH),
                    castBarBorderU32, 2f);

                // "Aktivierung" arrow at cast start
                drawList.AddTriangleFilled(
                    new Vector2(castBarX, castBarY - 4),
                    new Vector2(castBarX + 6, castBarY - 4),
                    new Vector2(castBarX + 3, castBarY),
                    castBarBorderU32);

                // Cast time label
                if (castBarWidth > 30)
                {
                    string castLabel = $"{action.CastTime:F1}s";
                    drawList.AddText(new Vector2(castBarX + 3, castBarY + 1),
                        ImGui.ColorConvertFloat4ToU32(RSRStyle.TextPrimary), castLabel);
                }
            }

            // Selection highlight
            if (isSelected)
            {
                drawList.AddRect(
                    new Vector2(x - 2, iconY - 2),
                    new Vector2(x + iconSize + 2, iconY + iconSize + 2),
                    RSRStyle.AccentU32, 4f, ImDrawFlags.None, 2f);
            }

            // Prepull indicator (negative time)
            if (action.CombatTime < 0)
            {
                drawList.AddText(
                    new Vector2(x, iconY - 12),
                    ImGui.ColorConvertFloat4ToU32(PullMarkerColor),
                    $"{action.CombatTime:F1}s");
            }

            // Draw icon
            if (IconSet.GetTexture(action.IconId, out IDalamudTextureWrap? texture) && texture != null)
            {
                drawList.AddImage(texture.Handle,
                    new Vector2(x, iconY),
                    new Vector2(x + iconSize, iconY + iconSize));
            }
            else
            {
                var fallbackColor = type == TimelineItemType.GCD ? RSRStyle.AccentU32 : RSRStyle.AccentDimU32;
                drawList.AddRectFilled(
                    new Vector2(x, iconY),
                    new Vector2(x + iconSize, iconY + iconSize),
                    fallbackColor, 4f);
            }

            // Duration bar under icon
            float duration = action.Duration > 0 ? action.Duration : (type == TimelineItemType.GCD ? 2.5f : 0.6f);
            float barWidth = duration * _pixelsPerSecond;
            drawList.AddRectFilled(
                new Vector2(x, iconY + iconSize),
                new Vector2(x + barWidth, iconY + iconSize + 3),
                ImGui.ColorConvertFloat4ToU32(RSRStyle.AccentActive with { W = 0.60f }), 1f);

            // Tooltip
            var mousePos = ImGui.GetMousePos();
            if (mousePos.X >= x && mousePos.X <= x + iconSize && mousePos.Y >= iconY && mousePos.Y <= iconY + iconSize)
            {
                ImGui.BeginTooltip();
                ImGui.Text(action.ActionName);

                if (action.CombatTime < 0)
                    ImGui.TextColored(PullMarkerColor, $"Prepull: {action.CombatTime:F1}s");
                else
                    ImGui.Text($"Aktivierung: {FormatTime(action.CombatTime)}");

                if (action.CastTime > 0)
                {
                    ImGui.TextColored(CastBarColor,
                        $"Castzeit: {action.CastTime:F1}s (Effekt bei {FormatTime(action.EffectTime)})");
                }

                if (action.Type == TimelineItemType.GCD)
                {
                    float gcd = GetCurrentGCD();
                    int slot = (int)MathF.Round(action.CombatTime / gcd) + 1;
                    if (slot > 0) ImGui.TextColored(RSRStyle.Accent, $"GCD Slot #{slot}");
                }

                // Show relation to nearest mechanic
                var nearestMech = FindNearestMechanic(action.EffectTime);
                if (nearestMech != null)
                {
                    float delta = nearestMech.CombatTime - action.EffectTime;
                    ImGui.TextColored(GetMechanicColor(nearestMech.Type),
                        $"{delta:F1}s vor {nearestMech.Type}: {nearestMech.Name}");
                }

                if (!string.IsNullOrEmpty(action.Comment))
                    ImGui.TextColored(RSRStyle.TextSecondary, action.Comment);
                ImGui.EndTooltip();
            }
        }
    }

    /// <summary>
    /// Draw connecting lines between oGCD actions and their nearest relevant mechanic
    /// </summary>
    private void DrawMechanicLinks(ImDrawListPtr drawList, Vector2 pos, Vector2 size)
    {
        uint linkColor = ImGui.ColorConvertFloat4ToU32(MechanicLinkColor);
        float ogcdLaneY = pos.Y + MechanicLaneHeight + GCDLaneHeight;

        foreach (var action in _currentPlan.Actions)
        {
            if (action.Type != TimelineItemType.OGCD) continue;

            var mech = FindNearestMechanic(action.EffectTime, 15f);
            if (mech == null) continue;

            float actionX = TimeToX(pos.X, action.EffectTime);
            float mechX = TimeToX(pos.X, mech.CombatTime);

            // Only draw if both are on screen
            if (actionX > pos.X + size.X || mechX < pos.X) continue;

            float mechY = pos.Y + 16 + (MechanicLaneHeight - 18) / 2;
            float actionY = ogcdLaneY + OGCDLaneHeight / 2;

            // Dashed connecting line
            float dx = mechX - actionX;
            float dy = mechY - actionY;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 5) continue;

            float nx = dx / len;
            float ny = dy / len;
            for (float d = 0; d < len; d += 6)
            {
                float segEnd = Math.Min(d + 3, len);
                drawList.AddLine(
                    new Vector2(actionX + nx * d, actionY + ny * d),
                    new Vector2(actionX + nx * segEnd, actionY + ny * segEnd),
                    linkColor, 1.5f);
            }

            // Time delta label at midpoint
            float delta = mech.CombatTime - action.EffectTime;
            if (delta > 0.5f)
            {
                float midX = (actionX + mechX) / 2;
                float midY = (actionY + mechY) / 2;
                string deltaLabel = $"-{delta:F1}s";
                drawList.AddText(new Vector2(midX - 10, midY - 8), linkColor, deltaLabel);
            }
        }
    }

    private void DrawCurrentTimeLine(ImDrawListPtr drawList, Vector2 pos, Vector2 size)
    {
        if (!DataCenter.InCombat) return;

        float combatTime = DataCenter.CombatTimeRaw;
        float x = TimeToX(pos.X, combatTime);
        if (x < pos.X || x > pos.X + size.X) return;

        drawList.AddLine(
            new Vector2(x, pos.Y),
            new Vector2(x, pos.Y + size.Y),
            RSRStyle.AccentU32, 2f);
    }

    private void DrawBossDeathMarker(ImDrawListPtr drawList, Vector2 pos, Vector2 size)
    {
        float totalDuration = _currentPlan.TotalDuration;
        if (totalDuration <= 0) return;

        float x = TimeToX(pos.X, totalDuration);
        if (x < pos.X || x > pos.X + size.X) return;

        uint deathColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 0.2f, 0.2f, 0.9f));
        uint deathBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.1f, 0.1f, 0.25f));

        // Vertical line
        drawList.AddLine(new Vector2(x, pos.Y), new Vector2(x, pos.Y + size.Y), deathColor, 2.5f);

        // Background shade after death
        float endX = Math.Min(x + 60, pos.X + size.X);
        drawList.AddRectFilled(new Vector2(x, pos.Y), new Vector2(endX, pos.Y + size.Y), deathBg);

        // Label
        string label = $"BOSS DEATH {FormatTime(totalDuration)}";
        drawList.AddText(new Vector2(x + 4, pos.Y + 4), deathColor, label);
    }

    #endregion

    #region Helpers

    private void CreateNewPlan()
    {
        var territory = DataCenter.Territory;
        string job = Player.Available ? DataCenter.Job.ToString() : "UNK";

        _currentPlan = new RotationPlan
        {
            Name = _newPlanName,
            TerritoryId = territory?.Id ?? 0,
            EncounterName = territory?.ContentFinderName ?? territory?.Name ?? "Unknown",
            Job = job,
            TotalDuration = 600f
        };

        // If an encounter is selected, use its data
        if (_selectedEncounterIndex >= 0 && _selectedEncounterIndex < _encounters.Count)
        {
            var enc = _encounters[_selectedEncounterIndex];
            _currentPlan.EncounterOID = enc.OID;
            _currentPlan.EncounterName = enc.DisplayName;
            BossModTimelineProvider.TryPopulateForOID(_currentPlan, enc.OID);
        }
        else
        {
            // Fallback: try active BossMod encounter
            BossModTimelineProvider.TryPopulate(_currentPlan);
        }

        RotationPlanStorage.Save(_currentPlan);
        RefreshPlanList();

        _selectedPlanIndex = _allPlans.FindIndex(p => p.Name == _newPlanName);
        _newPlanName = "";
    }

    private void RefreshPlanList()
    {
        var territory = DataCenter.Territory;
        uint territoryId = territory?.Id ?? 0;
        string job = Player.Available ? DataCenter.Job.ToString() : "";
        _allPlans = RotationPlanStorage.LoadAll(territoryId > 0 ? territoryId : null, !string.IsNullOrEmpty(job) ? job : null);
    }

    /// <summary>
    /// Get current GCD value with fallback to 2.5s
    /// </summary>
    private static float GetCurrentGCD()
    {
        float gcd = ActionTimelineManager.Instance.GCD;
        return gcd > 0 ? gcd : 2.5f;
    }

    /// <summary>
    /// Snap a time value to the nearest GCD slot boundary
    /// </summary>
    private static float SnapToGCD(float time)
    {
        float gcd = GetCurrentGCD();
        return MathF.Round(time / gcd) * gcd;
    }

    /// <summary>
    /// Convert a combat time (can be negative for prepull) to a screen X position
    /// </summary>
    private float TimeToX(float canvasX, float time)
    {
        float precast = _currentPlan.PrecastTime;
        return canvasX + (time + precast) * _pixelsPerSecond - _scrollX;
    }

    /// <summary>
    /// Convert a screen X position back to combat time
    /// </summary>
    private float XToTime(float canvasX, float screenX)
    {
        float precast = _currentPlan.PrecastTime;
        return (screenX - canvasX + _scrollX) / _pixelsPerSecond - precast;
    }

    /// <summary>
    /// Find the nearest mechanic happening after the given time
    /// </summary>
    private TimelineMechanic? FindNearestMechanic(float time, float maxLookahead = 30f)
    {
        TimelineMechanic? best = null;
        float bestDelta = maxLookahead;
        foreach (var m in _currentPlan.Mechanics)
        {
            if (m.Type is not (MechanicType.Raidwide or MechanicType.Tankbuster or MechanicType.SharedStack or MechanicType.Knockback))
                continue;
            float delta = m.CombatTime - time;
            if (delta > 0 && delta < bestDelta)
            {
                bestDelta = delta;
                best = m;
            }
        }
        return best;
    }

    private static Vector4 GetMechanicColor(MechanicType type) => type switch
    {
        MechanicType.Raidwide => RaidwideColor,
        MechanicType.Tankbuster => TankbusterColor,
        MechanicType.SharedStack => StackColor,
        MechanicType.Knockback => KnockbackColor,
        MechanicType.Downtime => DowntimeColor,
        MechanicType.Positioning => PositioningColor,
        MechanicType.Vulnerable => VulnerableColor,
        MechanicType.BossCast => BossCastColor,
        _ => CustomColor
    };

    private static string FormatTime(float seconds)
    {
        int min = (int)seconds / 60;
        float sec = seconds % 60;
        return min > 0 ? $"{min}:{sec:00.0}" : $"{sec:F1}s";
    }

    private static string TruncateText(string text, float maxWidth)
    {
        if (ImGui.CalcTextSize(text).X <= maxWidth) return text;
        for (int i = text.Length - 1; i > 0; i--)
        {
            string truncated = text[..i] + "..";
            if (ImGui.CalcTextSize(truncated).X <= maxWidth) return truncated;
        }
        return "";
    }

    /// <summary>
    /// Called when territory changes to refresh plan list
    /// </summary>
    public void OnTerritoryChanged()
    {
        RefreshPlanList();
        BossModTimelineProvider.InvalidateCache();
    }

    #endregion
}
