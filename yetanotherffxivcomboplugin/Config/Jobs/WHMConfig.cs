using System;

namespace yetanotherffxivcomboplugin.Config.Jobs;

[Serializable]
public class WHMConfig
{
    // Top-level enable switch for the WHM module.
    public bool Enabled { get; set; } = false;

    // Placeholder options; flesh out later.
    public bool ShowAdvancedSettings { get; set; } = false;
    public bool ExperimentalTweaks { get; set; } = false;
}
