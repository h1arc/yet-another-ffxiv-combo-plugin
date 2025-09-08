using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using yetanotherffxivcomboplugin.UI.Components;

namespace yetanotherffxivcomboplugin.UI.Components;

/// <summary>
/// Collapsible combo entry component for individual combat features.
/// Provides a checkbox header with expandable/collapsible content area.
/// </summary>
public static class ComboEntry
{
    /// <summary>
    /// Creates a collapsible combo entry with auto-sizing based on content.
    /// </summary>
    /// <param name="label">Label for the entry (e.g., "Single Target DPS")</param>
    /// <param name="enabled">Reference to the enabled state</param>
    /// <param name="onEnabledChanged">Callback when enabled state changes</param>
    /// <param name="content">Content to render when expanded</param>
    /// <param name="indent">Indentation amount in pixels (default: 20f)</param>
    public static void Draw(string label, ref bool enabled, Action<bool>? onEnabledChanged, Action content, float indent = 20f)
    {
        ImGui.Indent(indent);

        // Calculate a lighter background color (10% lighter than default)
        var frame = ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg];
        var lightBg = new Vector4(
            Math.Min(1.0f, frame.X + 0.1f),
            Math.Min(1.0f, frame.Y + 0.1f),
            Math.Min(1.0f, frame.Z + 0.1f),
            frame.W * 0.3f // Slightly more opacity for visibility
        );

        // Use traditional push/pop for ImGui to avoid ambiguity with ImPlot
        ImGui.PushStyleColor(ImGuiCol.ChildBg, lightBg);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f));

        // Calculate height based on content state
        var headerHeight = ImGui.GetFrameHeight();
        var bottomPadding = 8f; // Add some bottom padding only when expanded
        var contentHeight = enabled ? headerHeight + 6f + ImGui.GetTextLineHeight() + 16f + bottomPadding : headerHeight + 16f;

        var success = ImGui.BeginChild($"##{label}-container", new Vector2(0, contentHeight), true);
        if (success)
        {
            // Header row (always interactive)
            Check.Box(label, Fonts.Size.Default, ref enabled, onEnabledChanged);

            // Content only renders when enabled
            if (enabled)
            {
                ImGui.Dummy(new Vector2(0, 3));
                ImGui.Indent(20f);
                content();
                ImGui.Unindent(20f);
            }
        }
        ImGui.EndChild();

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(); // Pop the background color
        ImGui.Unindent(indent);
    }

    /// <summary>
    /// Creates a collapsible combo entry with calculated content height.
    /// Use this when you know the exact content height for better precision.
    /// </summary>
    /// <param name="label">Label for the entry</param>
    /// <param name="enabled">Reference to the enabled state</param>
    /// <param name="onEnabledChanged">Callback when enabled state changes</param>
    /// <param name="content">Content to render when expanded</param>
    /// <param name="contentHeight">Height of the content when expanded</param>
    /// <param name="indent">Indentation amount in pixels (default: 20f)</param>
    public static void DrawWithHeight(string label, ref bool enabled, Action<bool>? onEnabledChanged, Action content, float contentHeight, float indent = 20f)
    {
        ImGui.Indent(indent);

        // Calculate a lighter background color (10% lighter than default)
        var frame = ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg];
        var lightBg = new Vector4(
            Math.Min(1.0f, frame.X + 0.1f),
            Math.Min(1.0f, frame.Y + 0.1f),
            Math.Min(1.0f, frame.Z + 0.1f),
            frame.W * 0.3f // Slightly more opacity for visibility
        );

        // Use traditional push/pop for ImGui to avoid ambiguity with ImPlot
        ImGui.PushStyleColor(ImGuiCol.ChildBg, lightBg);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f));

        // Calculate total height based on content state
        var headerHeight = ImGui.GetFrameHeight();
        var bottomPadding = 8f;
        var totalHeight = enabled ? headerHeight + 6f + contentHeight + bottomPadding : headerHeight + 16f;

        var success = ImGui.BeginChild($"##{label}-container", new Vector2(0, totalHeight), true);
        if (success)
        {
            // Header row (always interactive)
            Check.Box(label, Fonts.Size.Default, ref enabled, onEnabledChanged);

            // Content only renders when enabled
            if (enabled)
            {
                ImGui.Dummy(new Vector2(0, 3));
                ImGui.Indent(20f);
                content();
                ImGui.Unindent(20f);
            }
        }
        ImGui.EndChild();

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(); // Pop the background color
        ImGui.Unindent(indent);
    }
}
