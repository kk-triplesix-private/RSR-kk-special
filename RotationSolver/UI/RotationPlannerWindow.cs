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
    private float _pixelsPerSecond = 8f;
    private float _scrollX;
    private const float MinPPS = 2f;
    private const float MaxPPS = 30f;
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
    private uint _dragPayloadActionId;
    private bool _isPanning;
    private float _panStartX;
    private float _panStartScrollX;

    // Encounter selection
    private List<EncounterInfo> _encounters = [];
    private int _selectedEncounterIndex = -1;
    private string _encounterFilter = "";

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
        ImGui.Text("Zoom:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        ImGui.SliderFloat("##Zoom", ref _pixelsPerSecond, MinPPS, MaxPPS, "%.0f");

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
        foreach (var action in actions)
        {
            bool actionIsGCD = action.Info.IsRealGCD;
            if (actionIsGCD != isGCD) continue;

            string name = action.Name;
            if (!string.IsNullOrEmpty(filter) && !name.ToLowerInvariant().Contains(filter)) continue;

            ImGui.SetCursorPosX(8);

            float iconSize = isGCD ? 28 : 24;

            // Draw icon
            if (IconSet.GetTexture(action.IconID, out IDalamudTextureWrap? texture) && texture != null)
            {
                var cursor = ImGui.GetCursorScreenPos();
                ImGui.Image(texture.Handle, new Vector2(iconSize, iconSize));

                // Drag source
                if (ImGui.BeginDragDropSource())
                {
                    _dragPayloadActionId = action.ID;
                    ImGui.SetDragDropPayload("PLANNED_ACTION", ReadOnlySpan<byte>.Empty);
                    ImGui.Image(texture.Handle, new Vector2(32, 32));
                    ImGui.SameLine();
                    ImGui.Text(name);
                    ImGui.EndDragDropSource();
                }
            }
            else
            {
                var fallbackColor = isGCD ? RSRStyle.Accent : RSRStyle.AccentDim;
                var cursor = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRectFilled(cursor, cursor + new Vector2(iconSize), ImGui.ColorConvertFloat4ToU32(fallbackColor), 4f);
                ImGui.Dummy(new Vector2(iconSize, iconSize));
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

        float totalSeconds = Math.Max(_currentPlan.TotalDuration, 600f);
        float totalWidth = totalSeconds * _pixelsPerSecond;

        // InvisibleButton FIRST as base interactable (drop target + input)
        ImGui.SetCursorScreenPos(canvasPos);
        ImGui.InvisibleButton("##CanvasDrop", canvasSize);
        bool canvasHovered = ImGui.IsItemHovered();

        // Handle drop on the invisible button
        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("PLANNED_ACTION");
            if (_dragPayloadActionId != 0)
            {
                float dropTime = (ImGui.GetMousePos().X - canvasPos.X + _scrollX) / _pixelsPerSecond;
                dropTime = Math.Max(0, dropTime);

                // Determine lane from Y position
                float mouseY = ImGui.GetMousePos().Y - canvasPos.Y;
                bool isGCDLane = mouseY < (MechanicLaneHeight + GCDLaneHeight);

                // Snap to GCD slots if dropping in GCD lane
                float gcdTime = ActionTimelineManager.Instance.GCD;
                if (isGCDLane && gcdTime > 0)
                {
                    dropTime = MathF.Round(dropTime / gcdTime) * gcdTime;
                }

                AddActionToPlan(_dragPayloadActionId, dropTime);
                _dragPayloadActionId = 0;
            }
            ImGui.EndDragDropTarget();
        }

        // Handle mouse interactions (zoom, pan, select, context menu)
        HandleCanvasInput(canvasPos, canvasSize, totalWidth, canvasHovered);

        // Draw layers on top via draw list
        DrawGrid(drawList, canvasPos, canvasSize, totalSeconds);
        DrawPhaseSeparators(drawList, canvasPos, canvasSize);
        DrawMechanicLane(drawList, canvasPos, canvasSize);
        DrawGCDLane(drawList, canvasPos, canvasSize);
        DrawOGCDLane(drawList, canvasPos, canvasSize);
        DrawCurrentTimeLine(drawList, canvasPos, canvasSize);

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void HandleCanvasInput(Vector2 pos, Vector2 size, float totalWidth, bool isHovered)
    {
        var io = ImGui.GetIO();
        var mousePos = io.MousePos;

        if (!isHovered) return;

        // Mouse wheel zoom
        if (io.MouseWheel != 0)
        {
            float oldPPS = _pixelsPerSecond;
            _pixelsPerSecond = Math.Clamp(_pixelsPerSecond + io.MouseWheel * 1f, MinPPS, MaxPPS);

            // Adjust scroll to keep mouse position stable
            float mouseTime = (mousePos.X - pos.X + _scrollX) / oldPPS;
            _scrollX = mouseTime * _pixelsPerSecond - (mousePos.X - pos.X);
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
            float clickTime = (mousePos.X - pos.X + _scrollX) / _pixelsPerSecond;
            _selectedActionId = FindActionAtTime(clickTime, mousePos.Y - pos.Y);
        }

        // Right-click context menu
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            float clickTime = (mousePos.X - pos.X + _scrollX) / _pixelsPerSecond;
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
                    ImGui.SetNextItemWidth(100);
                    if (ImGui.DragFloat("Time (s)", ref time, 0.1f, 0, _currentPlan.TotalDuration))
                    {
                        action.CombatTime = time;
                    }

                    string comment = action.Comment ?? "";
                    ImGui.SetNextItemWidth(150);
                    if (ImGui.InputText("Comment", ref comment, 128))
                    {
                        action.Comment = string.IsNullOrEmpty(comment) ? null : comment;
                    }

                    if (ImGui.MenuItem("Delete"))
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

        var planned = new PlannedAction
        {
            ActionId = actionId,
            ActionName = baseAction.Name,
            IconId = baseAction.IconID,
            Type = isGCD ? TimelineItemType.GCD : TimelineItemType.OGCD,
            CombatTime = combatTime,
            Duration = isGCD ? gcd : 0.6f // GCD or animation lock
        };

        _currentPlan.Actions.Add(planned);
        _currentPlan.Actions.Sort((a, b) => a.CombatTime.CompareTo(b.CombatTime));
    }

    #endregion

    #region Drawing

    private void DrawGrid(ImDrawListPtr drawList, Vector2 pos, Vector2 size, float totalSeconds)
    {
        uint gridColor = RSRStyle.SeparatorU32;
        uint textColor = ImGui.ColorConvertFloat4ToU32(RSRStyle.TextDisabled);

        // Determine grid interval based on zoom
        int interval = _pixelsPerSecond > 15 ? 5 : _pixelsPerSecond > 8 ? 10 : 30;

        for (float t = 0; t <= totalSeconds; t += interval)
        {
            float x = pos.X + t * _pixelsPerSecond - _scrollX;
            if (x < pos.X || x > pos.X + size.X) continue;

            drawList.AddLine(new Vector2(x, pos.Y), new Vector2(x, pos.Y + size.Y), gridColor);

            int minutes = (int)t / 60;
            int seconds = (int)t % 60;
            string label = minutes > 0 ? $"{minutes}:{seconds:D2}" : $"{seconds}s";
            drawList.AddText(new Vector2(x + 2, pos.Y + 2), textColor, label);
        }
    }

    private void DrawPhaseSeparators(ImDrawListPtr drawList, Vector2 pos, Vector2 size)
    {
        uint phaseColor = ImGui.ColorConvertFloat4ToU32(RSRStyle.Accent with { W = 0.50f });
        uint textColor = ImGui.ColorConvertFloat4ToU32(RSRStyle.Accent);

        foreach (var phase in _currentPlan.Phases)
        {
            float x = pos.X + phase.StartTime * _pixelsPerSecond - _scrollX;
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
            float x = pos.X + mech.CombatTime * _pixelsPerSecond - _scrollX;
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

        // Lane label
        drawList.AddText(new Vector2(pos.X + 4, laneY + 2),
            ImGui.ColorConvertFloat4ToU32(RSRStyle.TextDisabled), "GCD");

        // Draw GCD slot markers
        float gcd = ActionTimelineManager.Instance.GCD;
        if (gcd > 0)
        {
            uint slotColor = ImGui.ColorConvertFloat4ToU32(RSRStyle.SeparatorColor with { W = 0.20f });
            for (float t = 0; t < _currentPlan.TotalDuration; t += gcd)
            {
                float x = pos.X + t * _pixelsPerSecond - _scrollX;
                if (x < pos.X || x > pos.X + size.X) continue;
                drawList.AddLine(new Vector2(x, laneY + 14), new Vector2(x, laneY + GCDLaneHeight - 2), slotColor);
            }
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
        foreach (var action in _currentPlan.Actions)
        {
            if (action.Type != type) continue;

            float x = canvasPos.X + action.CombatTime * _pixelsPerSecond - _scrollX;
            if (x + iconSize < canvasPos.X || x > canvasPos.X + ImGui.GetWindowSize().X) continue;

            float iconY = laneY + (laneHeight - iconSize) / 2;
            bool isSelected = _selectedActionId == action.Id;

            // Selection highlight
            if (isSelected)
            {
                drawList.AddRect(
                    new Vector2(x - 2, iconY - 2),
                    new Vector2(x + iconSize + 2, iconY + iconSize + 2),
                    RSRStyle.AccentU32, 4f, ImDrawFlags.None, 2f);
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
                ImGui.Text($"Time: {FormatTime(action.CombatTime)}");
                if (!string.IsNullOrEmpty(action.Comment))
                    ImGui.TextColored(RSRStyle.TextSecondary, action.Comment);
                ImGui.EndTooltip();
            }
        }
    }

    private void DrawCurrentTimeLine(ImDrawListPtr drawList, Vector2 pos, Vector2 size)
    {
        if (!DataCenter.InCombat) return;

        float combatTime = DataCenter.CombatTimeRaw;
        float x = pos.X + combatTime * _pixelsPerSecond - _scrollX;
        if (x < pos.X || x > pos.X + size.X) return;

        drawList.AddLine(
            new Vector2(x, pos.Y),
            new Vector2(x, pos.Y + size.Y),
            RSRStyle.AccentU32, 2f);
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
