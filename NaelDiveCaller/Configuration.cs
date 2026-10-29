using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace NaelDiveCaller;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    /// <summary>Print a chat message each time the number of visible dragons changes.</summary>
    public bool Debug { get; set; }

    /// <summary>Full width of a dive band in yalms.</summary>
    public float DiveWidth { get; set; } = DiveSolver.DefaultHalfWidth * 2f * Plugin.ArenaRadius;

    // Waymark angles (degrees clockwise from north) used when A/B/C aren't placed in game.
    public float FallbackA { get; set; } = DiveSolver.DefaultWaymarks["A"];
    public float FallbackB { get; set; } = DiveSolver.DefaultWaymarks["B"];
    public float FallbackC { get; set; } = DiveSolver.DefaultWaymarks["C"];

    public Dictionary<string, float> FallbackWaymarks() => new()
    {
        ["A"] = FallbackA,
        ["B"] = FallbackB,
        ["C"] = FallbackC,
    };
}
