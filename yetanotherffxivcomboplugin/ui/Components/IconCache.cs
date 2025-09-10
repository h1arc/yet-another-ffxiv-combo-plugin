using System.Collections.Generic;
using Dalamud.Interface.Textures;

namespace yetanotherffxivcomboplugin.ui.Components;

internal static class IconCache
{
    private static readonly Dictionary<ushort, ISharedImmediateTexture> JobIcons = [];
    private static readonly Dictionary<uint, ISharedImmediateTexture> GenericIcons = [];

    public static bool TryGetJobIcon(ushort jobId, out ISharedImmediateTexture? texture)
    {
        if (JobIcons.TryGetValue(jobId, out var cached))
        { texture = cached; return true; }

        uint iconId = ComputeJobIconId(jobId);
        var tex = Plugin.Textures.GetFromGameIcon(new GameIconLookup(iconId));
        if (tex == null) { texture = null; return false; }
        JobIcons[jobId] = tex;
        texture = tex;

        return true;
    }

    private static uint ComputeJobIconId(ushort jobId)
        => jobId == 0 ? 62146u : (uint)(62100 + jobId);

    public static bool TryGetIcon(uint iconId, out ISharedImmediateTexture? texture)
    {
        if (GenericIcons.TryGetValue(iconId, out var cached))
        { texture = cached; return true; }

        var tex = Plugin.Textures.GetFromGameIcon(new GameIconLookup(iconId));
        if (tex == null) { texture = null; return false; }
        GenericIcons[iconId] = tex;
        texture = tex;
        return true;
    }
}
