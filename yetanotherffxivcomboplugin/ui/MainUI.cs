using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using yetanotherffxivcomboplugin.ui.Components;
using yetanotherffxivcomboplugin.ui.Jobs;

namespace yetanotherffxivcomboplugin.ui;

internal static class MainUI
{
    // Sidebar settings (can be exposed to config later)
    private static float _sidebarWidth = 30f; // icon rail per request
    private static float _iconSize = 30f;    // icon size kept at 30px

    // Selection state
    private static int _selectedSection = 0;
    private static int _selectedJob = 0;

    // Category -> Jobs (ordered by category, then job id)
    private static readonly List<JobRail.Section> _sections =
    [
        new JobRail.Section(
            "Tanks", "tanks",
            [
                new(19, "Paladin", "PLD", "pld"),
                new(21, "Warrior", "WAR", "war"),
                new(32, "Dark Knight", "DRK", "drk"),
                new(37, "Gunbreaker", "GNB", "gnb"),
            ]
        ),
        new JobRail.Section(
            "Healers", "healers",
            [
                new(24, "White Mage", "WHM", "whm"),
                new(28, "Scholar", "SCH", "sch"),
                new(33, "Astrologian", "AST", "ast"),
                new(40, "Sage", "SGE", "sge"),
            ]
        ),
        new JobRail.Section(
            "Melee DPS", "melee",
            [
                new(20, "Monk", "MNK", "mnk"),
                new(22, "Dragoon", "DRG", "drg"),
                new(30, "Ninja", "NIN", "nin"),
                new(34, "Samurai", "SAM", "sam"),
                new(39, "Reaper", "RPR", "rpr"),
                new(41, "Viper", "VPR", "vpr"),
            ]
        ),
        new JobRail.Section(
            "Ranged DPS", "ranged",
            [
                new(23, "Bard", "BRD", "brd"),
                new(31, "Machinist", "MCH", "mch"),
                new(38, "Dancer", "DNC", "dnc"),
            ]
        ),
        new JobRail.Section(
            "Casters", "casters",
            [
                new(25, "Black Mage", "BLM", "blm"),
                new(27, "Summoner", "SMN", "smn"),
                new(35, "Red Mage", "RDM", "rdm"),
                new(42, "Pictomancer", "PCT", "pct"),
            ]
        ),
        new JobRail.Section(
            "Gatherers", "dol",
            [
                new(16, "Miner", "MIN", "min"),
                new(17, "Botanist", "BTN", "btn"),
                new(18, "Fisher", "FSH", "fsh"),
            ]
        ),
    ];

    public static void Draw(Plugin plugin, ref bool open)
    {
        ImGui.SetNextWindowSize(new Vector2(520, 400), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("yafcp — Main", ref open))
        { ImGui.End(); return; }

        // Optional stricter behavior: switch on focus if enabled.
        if (plugin.Configuration.SwitchToCurrentJobOnMainUiFocus && ImGui.IsWindowFocused())
        {
            var job = (ushort)plugin.Snapshot.PlayerJobId;
            if (job != 0)
                SelectJob(job);
        }

        using (ImRaii.Child("##main-root", new Vector2(0, 0), false))
        {
            // Left: job sidebar with icons
            ImGui.BeginGroup();
            JobRail.Draw(_sections, ref _selectedSection, ref _selectedJob, _sidebarWidth, _iconSize);
            ImGui.EndGroup();

            ImGui.SameLine();

            // Right: content panel for selected job; use registry if available
            using (ImRaii.Child("##content", new Vector2(0, 0), true))
            {
                var section = _sections[_selectedSection];
                var job = section.Jobs[_selectedJob];
                var jobUi = JobUiRegistry.Resolve(job.JobId, plugin);
                if (jobUi != null)
                {
                    jobUi.Draw(plugin);
                }
                else
                {
                    ImGui.Text($"Category: {section.Label}");
                    ImGui.Text($"Job: {job.Label} (ID: {job.JobId})");
                    ImGui.Separator();
                    ImGui.Text("Job configuration will go here.");
                }
            }
        }

        ImGui.End();
    }

    // Helper: map a jobId to indices and select them. Returns true if changed.
    public static bool SelectJob(ushort jobId)
    {
        for (int s = 0; s < _sections.Count; s++)
        {
            var jobs = _sections[s].Jobs;
            for (int j = 0; j < jobs.Count; j++)
            {
                if (jobs[j].JobId == jobId)
                {
                    bool changed = _selectedSection != s || _selectedJob != j;
                    _selectedSection = s;
                    _selectedJob = j;
                    return changed;
                }
            }
        }
        return false;
    }
}
