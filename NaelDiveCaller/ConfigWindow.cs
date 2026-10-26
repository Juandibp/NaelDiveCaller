using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace NaelDiveCaller;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;
    private string testPattern = "N S SW W NW";
    private string[] testResult = [];

    public ConfigWindow(Plugin plugin) : base("Nael Dive Caller")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 300),
            MaximumSize = new Vector2(800, 1000),
        };
    }

    public override void Draw()
    {
        var config = plugin.Configuration;

        var debug = config.Debug;
        if (ImGui.Checkbox("Show detection messages in chat", ref debug))
        {
            config.Debug = debug;
            plugin.SaveConfiguration();
        }

        ImGui.Separator();
        DrawWaymarks(config);

        ImGui.Separator();
        DrawDragons();

        ImGui.Separator();
        DrawTest();
    }

    private void DrawWaymarks(Configuration config)
    {
        ImGui.TextUnformatted("Waymarks");
        ImGui.TextDisabled("Placed waymarks are used; these angles (clockwise from north) fill in for missing ones.");

        var placed = Plugin.ReadPlacedWaymarks();
        var changed = false;
        changed |= DrawWaymark("A", config.FallbackA, placed, v => config.FallbackA = v);
        changed |= DrawWaymark("B", config.FallbackB, placed, v => config.FallbackB = v);
        changed |= DrawWaymark("C", config.FallbackC, placed, v => config.FallbackC = v);
        if (changed)
        {
            plugin.SaveConfiguration();
        }

        if (ImGui.Button("Reset to raidplan angles"))
        {
            config.FallbackA = DiveSolver.DefaultWaymarks["A"];
            config.FallbackB = DiveSolver.DefaultWaymarks["B"];
            config.FallbackC = DiveSolver.DefaultWaymarks["C"];
            plugin.SaveConfiguration();
        }
    }

    private static bool DrawWaymark(string name, float fallback, IReadOnlyDictionary<string, float> placed, Action<float> set)
    {
        var value = fallback;
        var changed = ImGui.SliderFloat($"{name}##fallback{name}", ref value, 0f, 359f, "%.0f°");
        if (changed)
        {
            set(value);
        }

        ImGui.SameLine();
        if (placed.TryGetValue(name, out var angle))
        {
            ImGui.TextUnformatted($"placed {DiveSolver.CompassName(angle)} ({angle:F0}°)");
        }
        else
        {
            ImGui.TextDisabled($"not placed, using {DiveSolver.CompassName(value)}");
        }
        return changed;
    }

    private void DrawDragons()
    {
        ImGui.TextUnformatted("Dragons");
        if (!plugin.InUcob)
        {
            ImGui.TextDisabled("Not in UCoB.");
            return;
        }

        var dragons = plugin.CurrentDragons();
        if (dragons.Length == 0)
        {
            ImGui.TextDisabled("None visible yet.");
            return;
        }

        foreach (var dragon in dragons)
        {
            ImGui.BulletText(Plugin.Describe(dragon));
        }
        if (dragons.Length == 5 && ImGui.Button("Print spots to chat"))
        {
            plugin.Announce(dragons.Select(d => d.Position).ToArray());
        }
    }

    private void DrawTest()
    {
        ImGui.TextUnformatted("Try a pattern");
        ImGui.TextDisabled("Five dragon directions, e.g. N S SW W NW (or degrees).");

        ImGui.SetNextItemWidth(-90);
        ImGui.InputText("##pattern", ref testPattern, 64);
        ImGui.SameLine();
        if (ImGui.Button("Solve"))
        {
            testResult = Solve(testPattern);
        }

        foreach (var line in testResult)
        {
            ImGui.TextUnformatted(line);
        }
    }

    private string[] Solve(string pattern)
    {
        var angles = new List<float>();
        foreach (var part in pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!DiveSolver.TryParseDirection(part, out var angle))
            {
                return [$"Unknown direction '{part}'."];
            }
            angles.Add(angle);
        }
        if (angles.Count != 5)
        {
            return [$"Need 5 directions, got {angles.Count}."];
        }
        return DiveSolver.Describe(DiveSolver.Solve(angles, plugin.Waymarks()), Plugin.ReadPlacedWaymarks()).ToArray();
    }
}
