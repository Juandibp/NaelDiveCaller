using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace NaelDiveCaller;

public sealed class Plugin : IDalamudPlugin
{
    private const ushort UcobTerritoryId = 733;
    private const string CommandName = "/naeldive";
    private const string ChatTag = "NaelDive";

    // The arena is centered on (0,0); dragons spawn on its edge, so ignore anything near the middle.
    private const float MinDragonDistance = 10f;
    internal const float ArenaRadius = 22f;

    // A waymark in the middle of the arena has no useful direction.
    private const float MinMarkerDistance = 5f;

    // Wait for the dragons to stop moving before calling, in case they spawn and then get placed.
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(500);

    private static readonly HashSet<string> DragonNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Iceclaw", "Thunderwing", "Fang of Light", "Tail of Darkness", "Firehorn",
    };

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private string lastLayout = "";
    private DateTime stableSince;
    private bool announced;
    private int lastCount;

    private readonly WindowSystem windowSystem = new("NaelDiveCaller");
    private readonly ConfigWindow configWindow;

    internal Configuration Configuration { get; }

    internal bool InUcob => ClientState.TerritoryType == UcobTerritoryId;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        configWindow = new ConfigWindow(this);
        windowSystem.AddWindow(configWindow);
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += configWindow.Toggle;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Print the divebomb spots for the current dragons. "
                + "'/naeldive test N S SW W NW' tries a pattern, '/naeldive scan' lists nearby NPCs, "
                + "'/naeldive debug' toggles detection messages, '/naeldive config' opens settings.",
        });
        Framework.Update += OnFrameworkUpdate;
        ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void Dispose()
    {
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        Framework.Update -= OnFrameworkUpdate;
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.OpenMainUi -= configWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= configWindow.Toggle;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        windowSystem.RemoveAllWindows();
    }

    internal void SaveConfiguration() => PluginInterface.SavePluginConfig(Configuration);

    /// <summary>Configured dive half-width as a fraction of the arena radius.</summary>
    internal float HalfWidth => Configuration.DiveWidth / 2f / ArenaRadius;

    internal IGameObject[] CurrentDragons() => FindDragons().ToArray<IGameObject>();

    private void OnTerritoryChanged(uint territory)
    {
        if (territory == UcobTerritoryId)
        {
            ChatGui.Print("Watching for Nael's dragons.", ChatTag);
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!InUcob)
        {
            Reset();
            return;
        }

        var dragons = FindDragons().ToArray();
        if (Configuration.Debug && dragons.Length != lastCount)
        {
            ChatGui.Print($"Dragons seen: {dragons.Length} ({string.Join(", ", dragons.Select(Describe))})", ChatTag);
        }
        lastCount = dragons.Length;

        if (dragons.Length != 5)
        {
            // Re-arm once the dragons are gone so a later pull gets its own callout.
            if (dragons.Length == 0)
            {
                Reset();
            }
            return;
        }

        if (announced)
        {
            return;
        }

        // Compare the snapped layout instead of raw positions so small drift doesn't hold the callout back.
        // Distance is part of it so a dragon still flying in to the edge holds the callout.
        var layout = string.Join(",", dragons
            .Select(d => $"{DiveSolver.CompassName(DiveSolver.AngleOf(d.Position.X, d.Position.Z))}"
                + $"@{MathF.Round(new Vector2(d.Position.X, d.Position.Z).Length())}")
            .Order());
        if (layout != lastLayout)
        {
            lastLayout = layout;
            stableSince = DateTime.UtcNow;
            return;
        }

        if (DateTime.UtcNow - stableSince >= SettleTime)
        {
            announced = true;
            Announce(dragons.Select(d => d.Position).ToArray());
        }
    }

    private void Reset()
    {
        lastLayout = "";
        lastCount = 0;
        announced = false;
    }

    internal static string Describe(IGameObject o)
    {
        var distance = new Vector2(o.Position.X, o.Position.Z).Length();
        return $"{o.Name} {DiveSolver.CompassName(DiveSolver.AngleOf(o.Position.X, o.Position.Z))} {distance:F1}y";
    }

    private static IEnumerable<IBattleNpc> FindDragons() =>
        ObjectTable
            .OfType<IBattleNpc>()
            .Where(o => DragonNames.Contains(o.Name.TextValue))
            .Where(o => new Vector2(o.Position.X, o.Position.Z).Length() >= MinDragonDistance)
            .GroupBy(o => o.Name.TextValue)
            .Select(g => g.First());

    private void OnCommand(string command, string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && parts[0].Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            Configuration.Debug = !Configuration.Debug;
            SaveConfiguration();
            lastCount = -1;
            ChatGui.Print($"Detection messages {(Configuration.Debug ? "on" : "off")}.", ChatTag);
            return;
        }

        if (parts.Length > 0 && parts[0].Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            configWindow.Toggle();
            return;
        }

        if (parts.Length > 0 && parts[0].Equals("scan", StringComparison.OrdinalIgnoreCase))
        {
            var npcs = ObjectTable.OfType<IBattleNpc>().ToArray();
            ChatGui.Print($"Zone {ClientState.TerritoryType}, {npcs.Length} NPCs:", ChatTag);
            foreach (var npc in npcs)
            {
                ChatGui.Print($"{Describe(npc)} base {npc.BaseId} name {npc.NameId}", ChatTag);
            }
            return;
        }

        if (parts.Length > 0 && parts[0].Equals("test", StringComparison.OrdinalIgnoreCase))
        {
            var angles = new List<float>();
            foreach (var part in parts.Skip(1))
            {
                if (!DiveSolver.TryParseDirection(part, out var angle))
                {
                    ChatGui.PrintError($"Unknown direction '{part}'. Use N, NNE, NE... or degrees.", ChatTag);
                    return;
                }
                angles.Add(angle);
            }
            if (angles.Count != 5)
            {
                ChatGui.PrintError("Give exactly 5 dragon directions, e.g. /naeldive test N S SW W NW", ChatTag);
                return;
            }
            Print(DiveSolver.Solve(angles, Waymarks()));
            return;
        }

        var found = FindDragons().ToArray();
        if (found.Length != 5)
        {
            ChatGui.PrintError($"Found {found.Length}/5 dragons: {string.Join(", ", found.Select(Describe))}", ChatTag);
            return;
        }
        Announce(found.Select(d => d.Position).ToArray());
    }

    internal void Announce(Vector3[] positions)
    {
        foreach (var dragon in FindDragons())
        {
            Log.Debug($"{dragon.Name} base {dragon.BaseId} name {dragon.NameId} at {dragon.Position}");
        }
        // Median, so one dragon that isn't in place yet doesn't skew it.
        var distance = positions.Select(p => new Vector2(p.X, p.Z).Length()).Order().ElementAt(positions.Length / 2);
        if (Configuration.Debug)
        {
            ChatGui.Print($"Dragons are {distance:F1}y from the center.", ChatTag);
        }

        var angles = positions.Select(p => DiveSolver.AngleOf(p.X, p.Z)).ToArray();
        Print(DiveSolver.Solve(angles, Waymarks(), distance / ArenaRadius, HalfWidth));
    }

    private static void Print(DiveSolution solution)
    {
        // Echo-style local messages: nobody else sees these.
        foreach (var line in DiveSolver.Describe(solution, ReadPlacedWaymarks()))
        {
            ChatGui.Print(line, ChatTag);
        }
    }

    /// <summary>Angles of the placed A/B/C waymarks, with the configured fallbacks for missing ones.</summary>
    internal Dictionary<string, float> Waymarks()
    {
        var result = Configuration.FallbackWaymarks();
        foreach (var (name, angle) in ReadPlacedWaymarks())
        {
            result[name] = angle;
        }
        return result;
    }

    /// <summary>Angles of the waymarks currently placed in game, skipping any placed in the middle.</summary>
    internal static unsafe Dictionary<string, float> ReadPlacedWaymarks()
    {
        var result = new Dictionary<string, float>();
        var marking = MarkingController.Instance();
        if (marking == null)
        {
            return result;
        }

        // Field marker slots are A, B, C, D, then 1-4.
        string[] names = ["A", "B", "C", "D", "1", "2", "3", "4"];
        for (var i = 0; i < names.Length; i++)
        {
            var marker = marking->FieldMarkers[i];
            if (marker.Active && new Vector2(marker.Position.X, marker.Position.Z).Length() >= MinMarkerDistance)
            {
                result[names[i]] = DiveSolver.AngleOf(marker.Position.X, marker.Position.Z);
            }
        }
        return result;
    }
}
