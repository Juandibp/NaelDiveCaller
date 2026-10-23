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

    private Vector3[] lastPositions = [];
    private DateTime stableSince;
    private bool announced;

    public Plugin()
    {
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Print the divebomb spots for the current dragons. '/naeldive test N S SW W NW' tries a pattern.",
        });
        Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (ClientState.TerritoryType != UcobTerritoryId)
        {
            Reset();
            return;
        }

        var positions = FindDragons().Select(d => d.Position).ToArray();
        if (positions.Length != 5)
        {
            // Re-arm once the dragons are gone so a later pull gets its own callout.
            if (positions.Length == 0)
            {
                Reset();
            }
            return;
        }

        if (announced)
        {
            return;
        }

        var moved = lastPositions.Length != positions.Length
            || positions.Zip(lastPositions).Any(p => Vector3.Distance(p.First, p.Second) > 0.1f);
        lastPositions = positions;
        if (moved)
        {
            stableSince = DateTime.UtcNow;
            return;
        }

        if (DateTime.UtcNow - stableSince >= SettleTime)
        {
            announced = true;
            Announce(positions);
        }
    }

    private void Reset()
    {
        lastPositions = [];
        announced = false;
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

        var positions = FindDragons().Select(d => d.Position).ToArray();
        if (positions.Length != 5)
        {
            ChatGui.PrintError($"Found {positions.Length}/5 dragons.", ChatTag);
            return;
        }
        Announce(positions);
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
