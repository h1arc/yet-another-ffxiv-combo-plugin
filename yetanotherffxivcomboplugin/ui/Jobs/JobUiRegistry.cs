using System.Collections.Generic;
using yetanotherffxivcomboplugin.config.Jobs;
using yetanotherffxivcomboplugin.ui.Jobs.WHM;

namespace yetanotherffxivcomboplugin.ui.Jobs;

public static class JobUiRegistry
{
    // Lazily created UIs are kept here for the session.
    private static readonly Dictionary<ushort, IJobConfigUI> Cache = [];

    public static IJobConfigUI? Resolve(ushort jobId, Plugin plugin)
    {
        if (Cache.TryGetValue(jobId, out var ui))
            return ui;

        switch (jobId)
        {
            case 24: // WHM
                // In the future, job configs may live within Configuration; for now, keep a transient instance.
                var whmCfg = new WHMConfig();
                ui = new WHMConfigUI(whmCfg);
                Cache[jobId] = ui;
                return ui;
            default:
                return null;
        }
    }
}
