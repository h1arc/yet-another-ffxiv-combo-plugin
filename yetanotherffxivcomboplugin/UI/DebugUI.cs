using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using yetanotherffxivcomboplugin.Jobs.WHM;

namespace yetanotherffxivcomboplugin.UI;

internal static class DebugUI
{
    public static void DrawInline(Plugin plugin)
    {
        if (plugin.Pipeline is null || plugin.GameCache is null)
        {
            ImGui.Text("Plugin not initialized yet.");
            ImGui.BulletText($"Pipeline: {(plugin.Pipeline == null ? "<null>" : "ok")} ");
            ImGui.BulletText($"GameCache: {(plugin.GameCache == null ? "<null>" : "ok")} ");
            return;
        }
        DrawBody(plugin);
    }

    public static void Draw(Plugin plugin, ref bool open)
    {
        if (!ImGui.Begin("MAC Debug", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        { ImGui.End(); return; }
        if (plugin.Pipeline is null || plugin.GameCache is null)
        {
            ImGui.Text("Plugin not initialized yet.");
            ImGui.BulletText($"Pipeline: {(plugin.Pipeline == null ? "<null>" : "ok")} ");
            ImGui.BulletText($"GameCache: {(plugin.GameCache == null ? "<null>" : "ok")} ");
            var gate = new Core.UpdateGate(Plugin.ClientState, Plugin.Condition);
            var skip = gate.ShouldSkip(out var reason);
            ImGui.Separator();
            ImGui.Text("Gating snapshot:");
            ImGui.BulletText($"ShouldSkip: {skip} reason={reason}");
            ImGui.BulletText($"IsLoggedIn: {Plugin.ClientState?.IsLoggedIn == true}");
            ImGui.BulletText($"InCombat: {Plugin.Condition?[ConditionFlag.InCombat] == true}");
            ImGui.BulletText($"BetweenAreas: {Plugin.Condition?[ConditionFlag.BetweenAreas] == true} / 51: {Plugin.Condition?[ConditionFlag.BetweenAreas51] == true}");
            ImGui.BulletText($"Cutscene: {Plugin.Condition?[ConditionFlag.OccupiedInCutSceneEvent] == true} / Watching: {Plugin.Condition?[ConditionFlag.WatchingCutscene] == true}");
            ImGui.BulletText($"OccupiedInEvent: {Plugin.Condition?[ConditionFlag.OccupiedInEvent] == true}");
            ImGui.BulletText($"Mounted: {Plugin.Condition?[ConditionFlag.Mounted] == true} Casting: {Plugin.Condition?[ConditionFlag.Casting] == true}");
            ImGui.End();
            return;
        }
        DrawBody(plugin);
        ImGui.End();
    }

    private static void DrawBody(Plugin plugin)
    {
        var pipe = plugin.Pipeline;
        ref readonly var m = ref pipe.Metrics;
        ImGui.Text($"Pipeline: {m}");
        var snap = plugin.GameCache;
        ImGui.Separator();
        ImGui.Text($"Player: {(snap.Player.IsValid ? snap.Player.Id.ToString() : "<none>")}");
        ImGui.Text($"HP%={snap.PlayerHpPct} MP={snap.PlayerMp} Lvl={snap.PlayerLevel}");
        ImGui.Text($"InCombat={(snap.Flags.InCombat ? 1 : 0)} Casting={(snap.Flags.Casting ? 1 : 0)}");
        ImGui.Text($"Target={(snap.Target.HasValue ? snap.Target.Value.Id.ToString() : "<none>")}");
        ImGui.Text($"JobProfile={(ushort)snap.PlayerJobId}");

        ImGui.Separator();
        ImGui.Text("Gauges:");
        ushort jobId = (ushort)snap.PlayerJobId;
        switch (jobId)
        {
            case 24:
                var g = WhmGaugeReader.Read();
                ImGui.BulletText($"gauge1: {g.Lilies}");
                ImGui.BulletText($"gauge2: {g.BloodLilies}");
                ImGui.BulletText($"gauge3: {g.LilyTimerMs}ms");
                ImGui.BulletText($"overcapSoon: {(g.OvercapSoon ? 1 : 0)}");
                var nextMs = g.NextLilyRemainingMs;
                ImGui.BulletText($"nextLilyIn: {(nextMs >= 0 ? nextMs.ToString() : "-")}ms");
                break;
            case 40:
                var sg = yetanotherffxivcomboplugin.Jobs.SGE.SgeGaugeReader.Read();
                ImGui.BulletText($"addersgall: {sg.Addersgall}");
                ImGui.BulletText($"addersting: {sg.Addersting}");
                ImGui.BulletText($"gallTimer: {sg.AddersgallTimerMs}ms");
                ImGui.BulletText($"overcapSoon: {(sg.OvercapSoon ? 1 : 0)}");
                var nextGall = sg.NextAddersgallRemainingMs;
                ImGui.BulletText($"nextGallIn: {(nextGall >= 0 ? nextGall.ToString() : "-")}ms");
                break;
            default:
                ImGui.BulletText("<none>");
                break;
        }

        ImGui.Separator();

        var anchors = snap.Anchors;
        ImGui.Text($"Anchors ({anchors.Length}): [{string.Join(",", anchors.ToArray())}]");
        if (Plugin.Planner != null)
        {
            var pa = Plugin.Planner.ActiveAnchor;
            var last = Plugin.Planner.LastAnchors;
            ImGui.BulletText($"ActiveAnchor: {pa}");
            ImGui.BulletText($"LastAnchors: [{string.Join(",", last.ToArray())}]");
        }

        // Transient suggestions per anchor (fresh plan built for each anchor without mutating persistent plan)
        // var plannerTmp = Plugin.Planner;
        // if (plannerTmp != null && anchors.Length > 0)
        // {
        //     ImGui.Text("Transient (per-anchor) suggestions:");
        //     Span<Core.Suggestion> gTmp = stackalloc Core.Suggestion[4];
        //     Span<Core.Suggestion> oTmp = stackalloc Core.Suggestion[6];
        //     for (int k = 0; k < anchors.Length; k++)
        //     {
        //         int anchor = anchors[k];
        //         ImGui.BulletText($"anchor[{k}] id={anchor}");
        //         gTmp.Clear();
        //         oTmp.Clear();
        //         plannerTmp.BuildForPressed(snap, anchor, gTmp, oTmp);

        //         ImGui.Indent();
        //         ImGui.Text("GCD:");
        //         for (int i = 0; i < gTmp.Length; i++)
        //         {
        //             var s = gTmp[i];
        //             if (s.ActionId == 0) { ImGui.BulletText($"g[{i}] -"); continue; }
        //             var fb = s.Order >= 250 ? " (fallback)" : string.Empty;
        //             ImGui.BulletText($"g[{i}] id={s.ActionId} order={s.Order}{fb}");
        //         }
        //         ImGui.Text("OGCD:");
        //         for (int i = 0; i < oTmp.Length; i++)
        //         {
        //             var s = oTmp[i];
        //             if (s.ActionId == 0) { ImGui.BulletText($"o[{i}] -"); continue; }
        //             bool ready = snap.IsActionReady(s.ActionId);
        //             float remSec = 0f;
        //             if (snap.TryGetCooldownRemainingMs(s.ActionId, out var ms)) remSec = ms / 1000f;
        //             ImGui.BulletText($"o[{i}] id={s.ActionId} order={s.Order} ready={(ready ? 1 : 0)} rem={remSec:0.00}s");
        //         }
        //         ImGui.Unindent();
        //     }
        // }

        ImGui.Text("Plan (GCD):");
        var planner = Plugin.Planner;
        if (planner != null)
        {
            var gcd = planner.Gcd;
            for (int i = 0; i < gcd.Length; i++)
            {
                var s = gcd[i];
                if (s.ActionId != 0)
                    ImGui.BulletText($"[{i}] id={s.ActionId} order={s.Order}");
                else ImGui.BulletText($"[{i}] -");
            }

            ImGui.Text("Plan (OGCD):");
            var og = planner.Ogcd;
            for (int i = 0; i < og.Length; i++)
            {
                var s = og[i];
                if (s.ActionId != 0)
                {
                    bool ready = snap.IsActionReady(s.ActionId);
                    float remSec = 0f;
                    if (snap.TryGetCooldownRemainingMs(s.ActionId, out var ms)) remSec = ms / 1000f;
                    ImGui.BulletText($"[{i}] id={s.ActionId} order={s.Order} ready={(ready ? 1 : 0)} rem={remSec:0.00}s");
                }
                else ImGui.BulletText($"[{i}] -");
            }
        }
        else
        {
            ImGui.Text("Planner not initialized.");
        }

        ImGui.Separator();
        ImGui.Text("Tracked Cooldowns:");
        var cds = snap.TrackedCooldowns;
        if (cds.Length == 0)
        {
            ImGui.BulletText("<none>");
        }
        else
        {
            for (int i = 0; i < cds.Length; i++)
            {
                var id = cds[i];
                if (id == 0) continue;
                var has = snap.TryGetCooldownRemainingMs(id, out int ms);
                var sec = ms / 1000f;
                ImGui.BulletText($"cooldown{i + 1}: id={id} remaining={(has ? sec.ToString("0.00") : "0.00")}s");
            }
        }

        ImGui.Separator();
        ImGui.Text("Debuff Tracker (current target):");
        var tgtId = snap.Target.HasValue ? snap.Target.Value.Id : 0UL;
        var debuffs = snap.TrackedDebuffActions;
        if (tgtId == 0 || debuffs.Length == 0)
        {
            ImGui.BulletText("<none>");
        }
        else
        {
            for (int i = 0; i < debuffs.Length; i++)
            {
                var act = debuffs[i];
                if (act == 0) continue;
                var has = snap.TryGetDebuffRemainingMsForActionOnTarget(act, tgtId, out int ms);
                var sec = ms / 1000f;
                ImGui.BulletText($"action={act} remaining={(has ? sec.ToString("0.00") : "-")}s");
            }
        }

        ImGui.Separator();
        ImGui.Text("Status Micro Cache:");
        var lutInit = Snapshot.GameSnapshot.DispellableLutInitialized;
        var lutCount = Snapshot.GameSnapshot.DispellableLutCount;
        ImGui.BulletText($"Initialized: {lutInit}");
        ImGui.BulletText($"Entries: {lutCount}");
        Span<ushort> sample = stackalloc ushort[8];
        if (Snapshot.GameSnapshot.TryGetDispellableLutSample(sample, out var filled) && filled > 0)
        {
            ushort[] arr = new ushort[filled];
            for (int i = 0; i < filled; i++) arr[i] = sample[i];
            ImGui.BulletText($"Sample: [{string.Join(",", arr)}]");
        }
        else
        {
            ImGui.BulletText("Sample: <none>");
        }

        ImGui.Separator();
        ImGui.Text("Gating / Conditions:");
        var g1 = new yetanotherffxivcomboplugin.Core.UpdateGate(Plugin.ClientState, Plugin.Condition);
        var shouldSkip = g1.ShouldSkip(out var skipReason);
        ImGui.BulletText($"UpdateGate.ShouldSkip: {shouldSkip} reason={skipReason}");
        ImGui.BulletText($"NonCombatThrottleInterval: {Core.UpdateGate.NonCombatThrottleInterval}");
        ImGui.BulletText($"IsLoggedIn: {Plugin.ClientState?.IsLoggedIn == true}");
        ImGui.BulletText($"Condition.InCombat: {Plugin.Condition?[ConditionFlag.InCombat] == true}");
        ImGui.BulletText($"Condition.Mounted: {Plugin.Condition?[ConditionFlag.Mounted] == true}");
        ImGui.BulletText($"Condition.Casting: {Plugin.Condition?[ConditionFlag.Casting] == true}");
        ImGui.BulletText($"Condition.BetweenAreas: {Plugin.Condition?[ConditionFlag.BetweenAreas] == true} | BetweenAreas51: {Plugin.Condition?[ConditionFlag.BetweenAreas51] == true}");
        ImGui.BulletText($"Condition.OccupiedInEvent: {Plugin.Condition?[ConditionFlag.OccupiedInEvent] == true}");
        ImGui.BulletText($"Condition.Cutscene: {Plugin.Condition?[ConditionFlag.OccupiedInCutSceneEvent] == true} | WatchingCutscene: {Plugin.Condition?[ConditionFlag.WatchingCutscene] == true}");
    }
}
