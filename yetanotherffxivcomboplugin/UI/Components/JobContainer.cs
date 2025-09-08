using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using yetanotherffxivcomboplugin.UI.Components;

namespace yetanotherffxivcomboplugin.UI.Components;

/// <summary>
/// Main job container that wraps all job configuration content.
/// Handles the overall enable flag and provides a styled container background.
/// </summary>
public static class JobContainer
{
    /// <summary>
    /// Creates a job container with enable checkbox and styled background.
    /// </summary>
    /// <param name="jobName">Name of the job (e.g., "White Mage")</param>
    /// <param name="enabled">Reference to the enabled state</param>
    /// <param name="onEnabledChanged">Callback when enabled state changes</param>
    /// <param name="content">Content to render inside the container</param>
    public static void Draw(string jobName, ref bool enabled, Action<bool>? onEnabledChanged, Action content)
    {
        ImGui.Indent(20f);

        // Main enable checkbox with dynamic label using Large font
        var checkboxLabel = enabled ? $"{jobName} Enabled" : $"{jobName} Disabled";
        Check.Box(checkboxLabel, Fonts.Size.Large, ref enabled, onEnabledChanged);

        ImGui.Unindent(20f);

        ImGui.Dummy(new Vector2(0, 4));

        // Disable all content when not enabled
        if (!enabled) ImGui.BeginDisabled();

        // Render content directly without extra container
        content();

        if (!enabled) ImGui.EndDisabled();
    }
}
