using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace yetanotherffxivcomboplugin.ui.Components;

internal static class IconRail
{
    public sealed record Item(uint IconId, string Label, string Id);

    public static int Draw(
        IReadOnlyList<Item> items,
        ref int selectedIndex,
        float width = 30f,
        float iconSize = 30f,
        IReadOnlyList<int>? separatorIndices = null)
    {
        using var _ = ImRaii.Child("##icon-rail", new Vector2(width, 0), false);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 2));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 4));

        int nextSepIdx = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (separatorIndices != null && nextSepIdx < separatorIndices.Count && i == separatorIndices[nextSepIdx])
            {
                var dl = ImGui.GetWindowDrawList();
                var cursor = ImGui.GetCursorScreenPos();
                var x1 = cursor.X;
                var x2 = cursor.X + width;
                var y = cursor.Y + 2f;
                dl.AddLine(new Vector2(x1, y), new Vector2(x2, y), 0x40FFFFFF);
                ImGui.Dummy(new Vector2(width, 6f));
                nextSepIdx++;
            }

            var it = items[i];
            float pad = (width - iconSize) * 0.5f;
            if (pad > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + pad);

            bool drewImage = false;
            if (IconCache.TryGetIcon(it.IconId, out var tex) && tex != null)
            {
                var wrap = tex.GetWrapOrEmpty();
                if (wrap.Width > 1 && wrap.Height > 1)
                {
                    ImGui.Image(wrap.Handle, new Vector2(iconSize, iconSize));
                    drewImage = true;
                }
            }
            if (!drewImage)
            {
                var dl = ImGui.GetWindowDrawList();
                var p = ImGui.GetCursorScreenPos();
                dl.AddRectFilled(p, p + new Vector2(iconSize, iconSize), 0xFF808080, 6f);
                ImGui.Dummy(new Vector2(iconSize, iconSize));
            }

            if (ImGui.IsItemClicked()) selectedIndex = i;
            Tooltip.Show(it.Label);
        }

        ImGui.PopStyleVar(2);
        return selectedIndex;
    }
}
