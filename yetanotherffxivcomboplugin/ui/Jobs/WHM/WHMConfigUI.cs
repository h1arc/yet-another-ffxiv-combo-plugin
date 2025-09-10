using System.Numerics;
using Dalamud.Bindings.ImGui;
using yetanotherffxivcomboplugin.config.Jobs;
using yetanotherffxivcomboplugin.ui.Components;

namespace yetanotherffxivcomboplugin.ui.Jobs.WHM;

public class WHMConfigUI(WHMConfig cfg) : IJobConfigUI
{
    private readonly WHMConfig _cfg = cfg;

    // Transient UI state for blocks; persistence to be decided later.
    private bool _stEnabled = false;
    private bool _aoeEnabled = false;
    private bool _raiseEnabled = false;
    private bool _retraceEnabled = false;

    public void Draw(Plugin plugin)
    {
        var enabled = _cfg.Enabled;
        JobContainer.Draw("White Mage", ref enabled, e => _cfg.Enabled = e, () =>
        {
            // ST DPS combo entry
            ComboEntry.Draw("ST DPS", ref _stEnabled, null, () =>
            {
                ImGui.TextDisabled("ST combo settings go here");
            });

            ImGui.Dummy(new Vector2(0, 4));

            // AoE combo entry
            ComboEntry.Draw("AoE DPS", ref _aoeEnabled, null, () =>
            {
                ImGui.TextDisabled("AoE combo settings go here");
            });

            ImGui.Dummy(new Vector2(0, 4));

            // Raise combo entry
            ComboEntry.Draw("Raise", ref _raiseEnabled, null, () =>
            {
                ImGui.TextDisabled("Raise settings go here");
            });

            ImGui.Dummy(new Vector2(0, 4));

            // Retrace entry
            ComboEntry.Draw("Retrace", ref _retraceEnabled, null, () =>
            {
                ImGui.TextDisabled("Retrace settings go here");
            });
        });
    }
}
