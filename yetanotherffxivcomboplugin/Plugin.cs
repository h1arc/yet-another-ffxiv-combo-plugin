using System;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects;

using yetanotherffxivcomboplugin.src.Core;
using yetanotherffxivcomboplugin.ui;
using System.Collections.Generic;
using System.Collections.Frozen;
using Lumina.Excel.Sheets;
using yetanotherffxivcomboplugin.src.Snapshot;
using yetanotherffxivcomboplugin.Hooks;
using yetanotherffxivcomboplugin.src.Adapters;
using yetanotherffxivcomboplugin.src.Helpers;

namespace yetanotherffxivcomboplugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider Interop { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IJobGauges Gauges { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;

    private const string CommandName = "/yafcp";

    public Configuration Configuration { get; init; }

    // GameSnapshot instance exposed for consumers
    public GameSnapshot Snapshot { get; private set; } = null!;
    public ActionResolver Resolver { get; private set; } = null!;

    internal static class Runtime
    {
        internal static GameSnapshot? Snap;
        internal static ActionResolver? Resolver;
    }

    private readonly UseActionHook? _useActionHook;

    private bool _debugOpen;
    private bool _mainOpen;
    private bool _configOpen;
    private bool _needsProfileApply;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Initialize immutable rules data (dispellable statuses) once — null/defensive checks only
        var statusSheet = DataManager.GetExcelSheet<Status>();
        if (statusSheet != null)
        {
            var list = new List<uint>(512);
            foreach (var row in statusSheet)
            {
                // RowId==0 is typically invalid; guard and avoid exceptions entirely
                if (row.RowId == 0) continue;
                if (row.CanDispel) list.Add(row.RowId);
            }
            GameSnapshot.InitializeRules(list.ToFrozenSet());
        }

        // Initialize core backend
        var view = new DalamudGameView(ClientState, TargetManager, PartyList, ObjectTable, Condition);
        Snapshot = new GameSnapshot(view);
        Resolver = new ActionResolver();

        Runtime.Snap = Snapshot;
        Runtime.Resolver = Resolver;

        _useActionHook = new UseActionHook(Resolver, Interop);
        _useActionHook.Enable();

        // Initialize sanctuary micro-cache once at startup via GameSnapshot
        GameSnapshot.UpdateSanctuary(DataManager, ClientState?.TerritoryType ?? 0);

        // Defer job profile application to the Framework thread to avoid main-thread violations
        _needsProfileApply = true;

        // No cached player state; events drive resets/loads
        Framework.Update += OnFrameworkUpdate;

        PluginInterface.UiBuilder.Draw += OnDrawUi;
        PluginInterface.UiBuilder.OpenMainUi += OnOpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;
        if (ClientState != null)
        {
            ClientState.Login += OnLogin;
            ClientState.Logout += OnLogout;

            ClientState.ClassJobChanged += OnClassJobChanged;
            ClientState.LevelChanged += OnLevelChanged;
            // ClientState.TerritoryChanged += OnTerritoryChanged;
        }

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "No help message yet! :3"
        });

        Log.Information($"[{PluginInterface.Manifest.Name}] Initialized");
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        // Apply job profile early on the Framework thread when needed (safe to access ClientState here)
        if (_needsProfileApply)
        {
            if (ClientState.IsLoggedIn)
            {
                // Refresh snapshot first so ConfigureJob reads the correct level and state
                Snapshot.Update();
                ApplyCurrentJobProfile();
                _needsProfileApply = false;
            }
            // else: stay true and try again next tick
        }

        if (ShouldSkipUpdate()) return;

        UpdateSnapshotAndPlan();
    }

    private static bool ShouldSkipUpdate()
    {
        // Intrinsic no-update gate: avoid doing any work when game is not in an actionable state.
        var gate = new UpdateGate(ClientState, Condition, () => GameSnapshot.IsInSanctuary);
        if (gate.ShouldSkip(out var _))
        {
            // Optional sparse logging can be added here if desired.
            return true;
        }
        return false;
    }

    private void UpdateSnapshotAndPlan()
    {
        // Refresh snapshot
        Snapshot.Update();
    }


    public void Dispose()
    {
        // No explicit sanctuary cleanup needed; stays false on next start
        Runtime.Snap = null;
        CommandManager.RemoveHandler(CommandName);
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= OnDrawUi;
        PluginInterface.UiBuilder.OpenMainUi -= OnOpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;

        if (ClientState != null)
        {
            ClientState.Login -= OnLogin;
            ClientState.Logout -= OnLogout;
            ClientState.ClassJobChanged -= OnClassJobChanged;
            ClientState.LevelChanged -= OnLevelChanged;
            // ClientState.TerritoryChanged -= OnTerritoryChanged;
        }
        _useActionHook?.Dispose();
    }

    private void OnLogin() { ClearAll(); _needsProfileApply = true; }
    private void OnLogout(int type, int code) { ClearAll(); }

    private void OnClassJobChanged(uint newJobId)
    {
        // Refresh snapshot first so level/job-dependent progression resolves correctly
        Snapshot.Update();
        Resolver.ClearRules();
        ApplyCurrentJobProfile((ushort)newJobId);
        Log.Information($"[yafcp] Job changed -> applied profile (job={newJobId})");
    }

    private void OnLevelChanged(uint _, uint __)
    {
        // Snapshot must reflect new level before re-registering rules
        Snapshot.Update();
        Resolver.ClearRules();
        ApplyCurrentJobProfile();
        Log.Information($"[yafcp] Level changed -> re-applied profile (lvl={Snapshot.PlayerLevel})");
    }

    // private void OnTerritoryChanged(ushort _)
    // {
    //     ClearAll();
    //     ApplyCurrentJobProfile();
    //     Log.Information("[yafcp] Territory changed: scheduling profile apply");
    //     GameSnapshot.UpdateSanctuary(DataManager, ClientState?.TerritoryType ?? 0);
    // }

    private void ClearAll()
    {
        // No explicit shutdown; clearing rules by setting to current job again handled in SetJob.
        Resolver?.ClearRules();
    }

    // Sanctuary updates are handled at startup and on TerritoryChanged.

    private void ApplyCurrentJobProfile(ushort? explicitJob = null)
    {
        var jid = explicitJob ?? (ushort)(ClientState.LocalPlayer?.ClassJob.Value.RowId ?? 0u);
        if (jid == 0) { Resolver.ClearRules(); return; }
        src.Jobs.Interfaces.JobRegistry.SetJob(jid, Snapshot, Resolver);
    }


    private void OnCommand(string command, string args)
    {
        var input = args ?? string.Empty;
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.Equals(parts[0], "debug", StringComparison.OrdinalIgnoreCase))
        {
            ToggleDebug();
            return;
        }
        if (string.Equals(parts[0], "config", StringComparison.OrdinalIgnoreCase))
        {
            _configOpen = true;
            return;
        }
        Log.Information("Usage: /yafcp [config|debug]");
    }

    private void ToggleDebug() => _debugOpen = !_debugOpen;
    private void OnOpenMainUi()
    {
        _mainOpen = true;
        try
        {
            if (Configuration.OpenMainUiToCurrentJob && Snapshot != null)
            {
                var job = (ushort)Snapshot.PlayerJobId;
                if (job != 0)
                {
                    _ = MainUI.SelectJob(job);
                }
            }
        }
        catch { /* be robust: never throw from UI open */ }
    }
    private void OnOpenConfigUi() { _configOpen = true; }

    private void OnDrawUi()
    {
        if (_mainOpen) MainUI.Draw(this, ref _mainOpen);
        if (_configOpen) ConfigUI.Draw(this, ref _configOpen);
        if (_debugOpen) DebugUI.Draw(this, ref _debugOpen);
    }
}
