using System;
using Dalamud.Bindings.ImGui;
using yetanotherffxivcomboplugin.src.Jobs.WHM;
using yetanotherffxivcomboplugin.src.Jobs.Interfaces;

namespace yetanotherffxivcomboplugin.ui;

internal static class DebugUI
{
    public static void Draw(Plugin plugin, ref bool open)
    {
        if (plugin.Snapshot is null)
        {
            ImGui.Text("Plugin not initialized yet.");
            ImGui.BulletText($"Snapshot: {(plugin.Snapshot == null ? "<null>" : "ok")} ");
            return;
        }
        DrawBody(plugin);
    }

    public static void DrawInline(Plugin plugin)
    {
        if (plugin.Snapshot is null)
        {
            ImGui.Text("Plugin not initialized yet.");
            ImGui.BulletText($"Snapshot: {(plugin.Snapshot == null ? "<null>" : "ok")} ");
            return;
        }
        DrawBody(plugin);
    }

    private static void DrawBody(Plugin plugin)
    {
        var snap = plugin.Snapshot;
        DrawPlayerInfo(snap);
        ImGui.Separator();
        DrawGauges(snap);
        ImGui.Separator();
        DrawCompiledRulesInfo(snap);
        ImGui.Separator();
        DrawTrackedCooldowns(snap);
        ImGui.Separator();
        DrawTrackedDebuffs(snap);
        ImGui.Separator();
        DrawRetraceInfo(snap);
    }

    // 1) Player info
    private static void DrawPlayerInfo(src.Snapshot.GameSnapshot snap)
    {
        ImGui.Text($"Player: {(snap.Player.IsValid ? snap.Player.Id.ToString() : "<none>")}");
        ImGui.Text($"HP%={snap.PlayerHpPct} MP={snap.PlayerMp} Lvl={snap.PlayerLevel}");
        ImGui.Text($"InCombat={(snap.Flags.InCombat ? 1 : 0)} Casting={(snap.Flags.Casting ? 1 : 0)}");
        ImGui.Text($"Moving={(snap.PlayerMoving ? 1 : 0)}");
        ImGui.Text($"Target={(snap.Target.HasValue ? snap.Target.Value.Id.ToString() : "<none>")}");
        ImGui.Text($"JobProfile={(ushort)snap.PlayerJobId}");
    }

    // 2) Gauges (with per-job helpers)
    private static void DrawGauges(src.Snapshot.GameSnapshot snap)
    {
        ImGui.Text("Gauges:");
        ushort jobId = (ushort)snap.PlayerJobId;
        switch (jobId)
        {
            case 24: DrawWhmGauges(); break;
            case 40: DrawSgeGauges(); break;
            default: ImGui.BulletText("<none>"); break;
        }
    }

    // 4) CompiledRules info (anchors, last resolved + OGCD telemetry)
    private static void DrawCompiledRulesInfo(src.Snapshot.GameSnapshot snap)
    {
        if (Plugin.Runtime.Resolver == null)
        {
            ImGui.Text("Resolver: <null>");
            return;
        }
        var resolver = Plugin.Runtime.Resolver;
        Span<int> tmp = stackalloc int[8];
        int aCount = resolver.CopyAnchors(tmp);
        // Fallback: include tracked cooldown ids that are anchors
        var cdsSpan = snap.TrackedCooldowns;
        for (int i = 0; i < cdsSpan.Length && aCount < tmp.Length; i++)
        {
            var id = cdsSpan[i]; if (id == 0) continue; if (resolver.IsAnchor(id)) tmp[aCount++] = id;
        }
        if (aCount > 0)
        {
            ImGui.Text($"Rule Anchors ({aCount}/{resolver.RuleCount}):");
            for (int i = 0; i < aCount; i++)
            {
                var anchor = tmp[i]; var (resolvedId, isGcd) = resolver.Resolve(snap, anchor);
                ImGui.BulletText($"[{i}] {anchor} -> {resolvedId} (gcd={(isGcd ? 1 : 0)})");
            }
        }
        else ImGui.Text($"Rule Anchors: <none> (ruleCount={resolver.RuleCount})");

        ImGui.BulletText($"last: anchor={resolver.LastAnchor} resolved={resolver.LastResolved} isGcd={(resolver.LastResolvedIsGcd ? 1 : 0)} frame={resolver.LastResolvedFrame}");
        ImGui.BulletText($"ogcd: lastSuggested={resolver.LastOgcdSuggested} prio={resolver.LastOgcdPriority} frame={resolver.LastOgcdFrame} throttleRemMs={resolver.OgcdThrottleRemainingMs}");
        ImGui.BulletText($"lastActionUsed: id={resolver.LastActionUsed}");
    }

    // 5) Tracked cooldowns
    private static void DrawTrackedCooldowns(src.Snapshot.GameSnapshot snap)
    {
        ImGui.Text("Tracked Cooldowns:");
        var cds = snap.TrackedCooldowns;
        if (cds.Length == 0)
        {
            ImGui.BulletText("<none>");
            return;
        }
        for (int i = 0; i < cds.Length; i++)
        {
            var id = cds[i];
            if (id == 0) continue;
            var has = snap.TryGetCooldownRemainingMs(id, out int ms);
            var sec = ms / 1000f;
            ImGui.BulletText($"cooldown{i + 1}: id={id} remaining={(has ? sec.ToString("0.00") : "0.00")}s");
        }
    }

    // 6) Tracked debuffs (current target)
    private static void DrawTrackedDebuffs(src.Snapshot.GameSnapshot snap)
    {
        ImGui.Text("Debuff Tracker (current target):");
        var tgtId = snap.Target.HasValue ? snap.Target.Value.Id : 0UL;
        var debuffs = snap.TrackedDebuffActions;
        if (tgtId == 0 || debuffs.Length == 0)
        {
            ImGui.BulletText("<none>");
            return;
        }
        for (int i = 0; i < debuffs.Length; i++)
        {
            var act = debuffs[i];
            if (act == 0) continue;
            var has = snap.TryGetDebuffRemainingMsForActionOnTarget(act, tgtId, out int ms);
            var sec = ms / 1000f;
            ImGui.BulletText($"action={act} remaining={(has ? sec.ToString("0.00") : "-")}s");
        }
    }

    // 3) Retrace info (micro-cache)
    private static void DrawRetraceInfo(src.Snapshot.GameSnapshot snap)
    {
        ImGui.Text("Retrace candidates (micro-cache):");
        var job = JobRegistry.Current;
        if (job == null || job.RetraceActions.Length == 0)
        {
            ImGui.BulletText("<no retrace list>");
            return;
        }
        int healAid = 0; int cleanseAid = 0;
        var rs = job.RetraceActions;
        for (int i = 0; i < rs.Length; i++)
        {
            var id = rs[i]; if (id == 0) continue;
            if (id == 7568) { cleanseAid = id; continue; } // Esuna
            if (id == 125 || id == 24287) continue; // Raise / Egeiro
            if (healAid == 0) healAid = id;
        }
        if (healAid != 0)
        {
            var pair = snap.GetTopHealablePair(healAid, includeSelf: true);
            if (pair.TryGetFirst(out var a))
                ImGui.BulletText($"healable[0]: id={a} hp={(snap.Player.Id == a ? snap.PlayerHpPct : FindHpPct(snap, a))}%");
            if (pair.TryGetSecond(out var b))
                ImGui.BulletText($"healable[1]: id={b} hp={(snap.Player.Id == b ? snap.PlayerHpPct : FindHpPct(snap, b))}%");
            if (!pair.TryGetFirst(out _)) ImGui.BulletText("healable: <none>");
        }
        else ImGui.BulletText("healable: <none>");

        if (cleanseAid != 0)
        {
            var cpair = snap.GetTopCleansablePair(cleanseAid, includeSelf: true);
            if (cpair.TryGetFirst(out var ca))
                ImGui.BulletText($"cleansable[0]: id={ca} hp={(snap.Player.Id == ca ? snap.PlayerHpPct : FindHpPct(snap, ca))}%");
            if (cpair.TryGetSecond(out var cb))
                ImGui.BulletText($"cleansable[1]: id={cb} hp={(snap.Player.Id == cb ? snap.PlayerHpPct : FindHpPct(snap, cb))}%");
            if (!cpair.TryGetFirst(out _)) ImGui.BulletText("cleansable: <none>");
        }
        else ImGui.BulletText("cleansable: <none>");
    }

    private static byte FindHpPct(src.Snapshot.GameSnapshot snap, ulong id)
    {
        var span = snap.Party;
        var hps = snap.PartyHpPct;
        for (int i = 0; i < span.Length; i++) if (span[i].IsValid && span[i].Id == id) return hps[i];
        return 0;
    }




    /// GAUGES
    private static void DrawWhmGauges()
    {
        var g = WhmGaugeReader.Read();
        ImGui.BulletText($"lilies: {g.Lilies}");
        ImGui.BulletText($"bloodLilies: {g.BloodLilies}");
        ImGui.BulletText($"lilyTimer: {g.LilyTimerMs}ms");
        ImGui.BulletText($"overcapSoon: {(g.OvercapSoon ? 1 : 0)}");
        var nextMs = g.NextLilyRemainingMs;
        ImGui.BulletText($"nextLilyIn: {(nextMs >= 0 ? nextMs.ToString() : "-")}ms");
    }

    private static void DrawSgeGauges()
    {
        var sg = src.Jobs.SGE.SgeGaugeReader.Read();
        ImGui.BulletText($"addersgall: {sg.Addersgall}");
        ImGui.BulletText($"addersting: {sg.Addersting}");
        ImGui.BulletText($"gallTimer: {sg.AddersgallTimerMs}ms");
        ImGui.BulletText($"overcapSoon: {(sg.OvercapSoon ? 1 : 0)}");
        var nextGall = sg.NextAddersgallRemainingMs;
        ImGui.BulletText($"nextGallIn: {(nextGall >= 0 ? nextGall.ToString() : "-")}ms");
    }
}
