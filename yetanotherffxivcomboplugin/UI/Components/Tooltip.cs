using Dalamud.Bindings.ImGui;

namespace yetanotherffxivcomboplugin.ui.Components;

internal static class Tooltip
{
    public static void Show(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(text);
            ImGui.EndTooltip();
        }
    }
}
