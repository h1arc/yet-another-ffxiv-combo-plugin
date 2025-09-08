using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace yetanotherffxivcomboplugin.UI.Components;

internal static class JobRail
{
    public sealed record JobItem(ushort JobId, string Label, string Abbrev, string Id);
    public sealed record Section(string Label, string Id, IReadOnlyList<JobItem> Jobs);

    public static (int sectionIndex, int jobIndex) Draw(
        IReadOnlyList<Section> sections,
        ref int selectedSection,
        ref int selectedJob,
        float width = 30f,
        float iconSize = 30f)
    {
        using var _ = ImRaii.Child("##job-sidebar", new Vector2(width, 0), false);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 2));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 4));

        for (int s = 0; s < sections.Count; s++)
        {
            var section = sections[s];
            if (s != 0)
            {
                var dl = ImGui.GetWindowDrawList();
                var cursor = ImGui.GetCursorScreenPos();
                var x1 = cursor.X;
                var x2 = cursor.X + width;
                var y = cursor.Y + 2f;
                dl.AddLine(new Vector2(x1, y), new Vector2(x2, y), 0x40FFFFFF);
                ImGui.Dummy(new Vector2(width, 6f));
            }

            for (int j = 0; j < section.Jobs.Count; j++)
            {
                var job = section.Jobs[j];
                using var __ = ImRaii.PushId(job.Id);

                float pad = (width - iconSize) * 0.5f;
                if (pad > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + pad);

                DrawIcon(iconSize, job.JobId);
                if (ImGui.IsItemClicked()) { selectedSection = s; selectedJob = j; }
                Tooltip.Show($"{job.Label} ({job.Abbrev})");
            }
        }

        ImGui.PopStyleVar(2);
        return (selectedSection, selectedJob);
    }

    private static void DrawIcon(float size, ushort jobId)
    {
        if (IconCache.TryGetJobIcon(jobId, out Dalamud.Interface.Textures.ISharedImmediateTexture? shared) && shared != null)
        {
            var wrap = shared.GetWrapOrEmpty();
            if (wrap != null)
            {
                ImGui.Image(wrap.Handle, new Vector2(size, size));
                return;
            }
        }

        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var col = PlaceholderColor(jobId);
        dl.AddRectFilled(p, p + new Vector2(size, size), col, 6f);
        ImGui.Dummy(new Vector2(size, size));
    }

    private static uint PlaceholderColor(ushort jobId)
    {
        unchecked
        {
            uint x = jobId * 2654435761u;
            byte r = (byte)(0x60 + (x & 0x7F));
            byte g = (byte)(0x60 + ((x >> 8) & 0x7F));
            byte b = (byte)(0x60 + ((x >> 16) & 0x7F));
            byte a = 0xFF;
            return (uint)(a << 24 | b << 16 | g << 8 | r);
        }
    }
}
