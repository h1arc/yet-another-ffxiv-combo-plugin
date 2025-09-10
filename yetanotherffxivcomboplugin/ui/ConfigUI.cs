using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using yetanotherffxivcomboplugin.ui.Components;

namespace yetanotherffxivcomboplugin.ui;

internal static class ConfigUI
{
    private static int _selectedIndex = 0;
    private static readonly List<IconRail.Item> _items =
    [
        new IconRail.Item(62580, "General Settings", "settings"),
        new IconRail.Item(62581, "Debug", "debug"),
    ];
    private static readonly List<int> _separators = [1];

    public static void Draw(Plugin plugin, ref bool open)
    {
        ImGui.SetNextWindowSize(new Vector2(520, 400), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("yafcp — Config", ref open))
        { ImGui.End(); return; }

        using (ImRaii.Child("##config-root", new Vector2(0, 0), false))
        {
            ImGui.BeginGroup();
            IconRail.Draw(_items, ref _selectedIndex, 30f, 30f, _separators);
            ImGui.EndGroup();

            ImGui.SameLine();

            using (ImRaii.Child("##config-content", new Vector2(0, 0), true))
            {
                switch (_selectedIndex)
                {
                    case 0:
                        ImGui.Text("General Settings");
                        ImGui.Separator();
                        var cfg = plugin.Configuration;
                        var openToJob = cfg.OpenMainUiToCurrentJob;
                        Check.Box("Always open MainUI to current job?", ref openToJob, v => { cfg.OpenMainUiToCurrentJob = v; cfg.Save(); });

                        using (var sub = SubOption.Begin(levels: 1, dim: !openToJob, dimFactor: 0.5f))
                        {
                            var switchOnFocus = cfg.SwitchToCurrentJobOnMainUiFocus;
                            Check.Box("Switch to current job when MainUI gains focus", ref switchOnFocus, v => { cfg.SwitchToCurrentJobOnMainUiFocus = v; cfg.Save(); });
                        }
                        break;
                    case 1:
                        DebugUI.DrawInline(plugin);
                        break;
                }
            }
        }

        ImGui.End();
    }
}
