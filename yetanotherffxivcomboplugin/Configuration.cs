using System;
using Dalamud.Configuration;

namespace yetanotherffxivcomboplugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // UI behavior: when enabled, opening MainUI selects the current job's tab.
    public bool OpenMainUiToCurrentJob { get; set; } = false;

    // When enabled (and combined with the above), switch to current job whenever MainUI gains focus.
    public bool SwitchToCurrentJobOnMainUiFocus { get; set; } = false;

    // the below exist just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
