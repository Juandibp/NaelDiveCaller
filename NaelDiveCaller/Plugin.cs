using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
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

    // Wait for the dragons to stop moving before calling, in case they spawn and then get placed.
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(500);

    private static readonly HashSet<string> DragonNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Iceclaw", "Thunderwing", "Fang of Light", "Tail of Darkness", "Firehorn",
    };

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
    private bool debug;

    public Plugin()
    {
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Print the divebomb spots for the current dragons. "
                + "'/naeldive test N S SW W NW' tries a pattern, '/naeldive scan' lists nearby NPCs, "
                + "'/naeldive debug' toggles detection messages.",
        });
        Framework.Update += OnFrameworkUpdate;
        ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void Dispose()
    {
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        Framework.Update -= OnFrameworkUpdate;
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnTerritoryChanged(uint territory)
    {
        if (territory == UcobTerritoryId)
        {
            ChatGui.Print("Watching for Nael's dragons.", ChatTag);
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (ClientState.TerritoryType != UcobTerritoryId)
        {
            Reset();
            return;
        }

        var dragons = FindDragons().ToArray();
        if (debug && dragons.Length != lastCount)
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
        var layout = string.Join(",", dragons
            .Select(d => DiveSolver.CompassName(DiveSolver.AngleOf(d.Position.X, d.Position.Z)))
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

    private static string Describe(IGameObject o)
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
            debug = !debug;
            lastCount = -1;
            ChatGui.Print($"Detection messages {(debug ? "on" : "off")}.", ChatTag);
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
            Print(DiveSolver.Solve(angles, ReadWaymarks()));
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

    private void Announce(Vector3[] positions)
    {
        foreach (var dragon in FindDragons())
        {
            Log.Debug($"{dragon.Name} base {dragon.BaseId} name {dragon.NameId} at {dragon.Position}");
        }
        Print(DiveSolver.Solve(positions.Select(p => DiveSolver.AngleOf(p.X, p.Z)).ToArray(), ReadWaymarks()));
    }

    private static void Print(DiveSolution solution)
    {
        // Echo-style local messages: nobody else sees these.
        foreach (var line in DiveSolver.Describe(solution))
        {
            ChatGui.Print(line, ChatTag);
        }
    }

    /// <summary>Angles of the placed A/B/C waymarks; missing ones fall back to the defaults.</summary>
    private static unsafe Dictionary<string, float> ReadWaymarks()
    {
        var result = new Dictionary<string, float>();
        var marking = MarkingController.Instance();
        if (marking == null)
        {
            return result;
        }

        string[] names = ["A", "B", "C"];
        for (var i = 0; i < names.Length; i++)
        {
            var marker = marking->FieldMarkers[i];
            if (marker.Active)
            {
                result[names[i]] = DiveSolver.AngleOf(marker.Position.X, marker.Position.Z);
            }
        }
        return result;
    }
}
