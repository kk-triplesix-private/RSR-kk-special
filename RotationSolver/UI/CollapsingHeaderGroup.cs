using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.Logging;

namespace RotationSolver.UI;

internal class CollapsingHeaderGroup(Dictionary<Func<string>, Action> headers)
{
	private readonly Dictionary<Func<string>, Action> _headers = headers ?? throw new ArgumentNullException(nameof(headers));
	private int _openedIndex = -1;

	public float HeaderSize { get; set; } = 24;

	public void AddCollapsingHeader(Func<string> name, Action action)
	{
		ArgumentNullException.ThrowIfNull(name);

		ArgumentNullException.ThrowIfNull(action);

		_headers[name] = action;
	}

	public void RemoveCollapsingHeader(Func<string> name)
	{
		ArgumentNullException.ThrowIfNull(name);

		_ = _headers.Remove(name);
	}

	public void ClearCollapsingHeader()
	{
		_headers.Clear();
	}

	/// <summary>
	/// Programmatically open a header by its display title.
	/// </summary>
	public void OpenHeaderByTitle(string? title, bool ignoreCase = true)
	{
		if (string.IsNullOrEmpty(title))
		{
			return;
		}
		int idx = -1;
		foreach (KeyValuePair<Func<string>, Action> header in _headers)
		{
			idx++;
			string name = header.Key?.Invoke() ?? string.Empty;
			if (string.IsNullOrEmpty(name))
			{
				continue;
			}
			if (string.Equals(name, title, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
			{
				_openedIndex = idx;
				return;
			}
		}
	}

	/// <summary>
	/// Programmatically open a header by its 0-based index.
	/// </summary>
	public void OpenHeaderByIndex(int index)
	{
		_openedIndex = index;
	}

	public void Draw()
	{
		int index = -1;
		foreach (KeyValuePair<Func<string>, Action> header in _headers)
		{
			index++;

			if (header.Key is null || header.Value is null)
			{
				continue;
			}

			string name = header.Key();
			if (string.IsNullOrEmpty(name))
			{
				continue;
			}

            try
            {
                ImGui.Dummy(new Vector2(0, 4));
                bool selected = index == _openedIndex;

                // Section header background
                float scale = ImGuiHelpers.GlobalScale;
                Vector2 headerPos = ImGui.GetCursorScreenPos();
                float headerWidth = ImGui.GetContentRegionAvail().X;
                float headerHeight = (HeaderSize + 8) * scale;

                var drawList = ImGui.GetWindowDrawList();
                drawList.AddRectFilled(
                    headerPos,
                    new Vector2(headerPos.X + headerWidth, headerPos.Y + headerHeight),
                    selected ? RSRStyle.SectionHeaderBgU32
                             : ImGui.ColorConvertFloat4ToU32(RSRStyle.SectionHeaderBg with { W = 0.5f }),
                    4f * scale);

                // Accent bar on left edge for open section
                if (selected)
                {
                    RSRStyle.DrawAccentBar(headerPos, headerHeight);
                }

                // Chevron indicator
                string chevron = selected ? "\u25BC " : "\u25B6 ";

                bool changed;
                using (ImRaii.PushColor(ImGuiCol.Header, Vector4.Zero))
                using (ImRaii.PushColor(ImGuiCol.HeaderHovered, RSRStyle.SectionHeaderHover))
                using (ImRaii.PushColor(ImGuiCol.HeaderActive, RSRStyle.SectionHeaderBg))
                using (ImRaii.PushColor(ImGuiCol.Text, selected ? RSRStyle.Accent : RSRStyle.TextPrimary))
                using (ImRaii.PushStyle(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.0f, 0.5f)))
                using (var font = ImRaii.PushFont(FontManager.GetFont(18)))
                {
                    changed = ImGui.Selectable($"   {chevron}{name}", selected, ImGuiSelectableFlags.DontClosePopups,
                        new Vector2(0, headerHeight));
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                }
                if (changed)
                {
                    _openedIndex = selected ? -1 : index;
                }
                if (selected)
                {
                    ImGui.Dummy(new Vector2(0, 4));
                    header.Value();
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"An error occurred while drawing the header: {ex.Message}");
            }
        }
    }
}
