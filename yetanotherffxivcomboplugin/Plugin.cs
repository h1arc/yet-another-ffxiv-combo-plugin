using System;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects;

using yetanotherffxivcomboplugin.Core;
using yetanotherffxivcomboplugin.UI;
using System.Collections.Generic;
using System.Collections.Frozen;
using Lumina.Excel.Sheets;
using yetanotherffxivcomboplugin.Snapshot;
using yetanotherffxivcomboplugin.Hooks;
using yetanotherffxivcomboplugin.Adapters;

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
    public GameSnapshot GameCache { get; private set; } = null!;
    public RetraceRegistry Retrace { get; private set; } = null!;
    public ActionExecutionPipeline Pipeline { get; private set; } = null!;

    internal static GameSnapshot? GameSnapshot { get; private set; }
    internal static Planner? Planner { get; private set; }

    private readonly UseActionHooker? _useActionHooker;
    private readonly Planner? _planner;

    private ushort _job;
    private bool _debugOpen;
    private bool _mainOpen;
    private bool _configOpen;
    private bool _needsProfileApply;

    // (moved) Sanctuary micro-cache now lives in GameSnapshot

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
        GameCache = new GameSnapshot(view);
        GameSnapshot = GameCache;
        Retrace = new RetraceRegistry();
        _planner = new Planner();
        Planner = _planner;
        Pipeline = new ActionExecutionPipeline(GameCache, _planner, Retrace);
        _useActionHooker = new UseActionHooker(Pipeline, Interop);
        _useActionHooker.Enable();

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
            HelpMessage = "yafcp: debug"
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
                GameCache.Update();
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
        // Refresh snapshot, prune Retrace entries, and tick planner (planner rebuilds in combat only)
        GameCache.Update();
        Retrace.ClearOld(TimeSpan.FromSeconds(20));
        Pipeline.Tick();
    }


    public void Dispose()
    {
        // No explicit sanctuary cleanup needed; stays false on next start
        GameSnapshot = null;
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
        _useActionHooker?.Dispose();
    }

    private void OnLogin() { ClearAll(); _needsProfileApply = true; }
    private void OnLogout(int type, int code) { ClearAll(); }

    private void OnClassJobChanged(uint _)
    {
        ClearAll();
        ApplyCurrentJobProfile();
        Log.Information("[yafcp] Job changed: scheduling profile apply");
    }

    private void OnLevelChanged(uint _, uint __)
    {
        ClearAll();
        ApplyCurrentJobProfile();
        Log.Information("[yafcp] Level changed: scheduling profile apply");
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
        _planner?.Reset();
        Retrace.ClearAll();
    }

    // Sanctuary updates are handled at startup and on TerritoryChanged.

    private void ApplyCurrentJobProfile()
    {
        _job = (ushort)(ClientState.LocalPlayer?.ClassJob.Value.RowId ?? 0u);
        Jobs.Interfaces.JobRegistry.ApplyForJob(_job, GameCache, _planner);
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
            if (Configuration.OpenMainUiToCurrentJob && GameCache != null)
            {
                var job = (ushort)GameCache.PlayerJobId;
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

    // Removed legacy dump/test commands; Debug UI surfaces runtime info now.
}
