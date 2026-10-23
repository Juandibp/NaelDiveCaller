using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NaelDiveCaller;

/// <summary>One divebomb: the dragons that dive on it and where its bait player should stand.</summary>
public sealed record DiveCall(int Dive, float[] Dragons, float BaitAngle, string? Waymark, float Clearance);

public sealed record DiveSolution(DiveCall[] Calls, bool Cursed);

/// <summary>
/// Angles are in degrees clockwise from north. Dragons are taken clockwise from north in a 2-1-2 split:
/// the first divebomb gets dragons 1-2, the second dragon 3, the third dragons 4-5.
/// Each dive is baited on its usual waymark (A/B/C) unless that would send a dive line through the
/// middle of the arena; then the bait spot is moved to whichever spot on the edge keeps the lines
/// furthest from the middle (the "cursed" pattern).
/// </summary>
public static class DiveSolver
{
    // A dive line closer than this to the center (as a fraction of the arena radius) clips the group.
    public const float MinClearance = 0.6f;

    // Dragons sit on the arena edge; bait players stand just inside it.
    private const float DragonRadius = 1.0f;
    private const float BaitRadius = 0.9f;

    // A dragon this many degrees counter-clockwise of north still counts as the north dragon.
    private const float NorthTolerance = 5f;

    // Waymark angles from the reference raidplan, used when A/B/C aren't placed in game.
    public static readonly IReadOnlyDictionary<string, float> DefaultWaymarks = new Dictionary<string, float>
    {
        ["A"] = 55f,
        ["B"] = 154f,
        ["C"] = 235f,
    };

    private static readonly string[] DiveWaymarks = ["A", "B", "C"];

    private static readonly string[] Compass =
        ["N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"];

    /// <summary>Angle of a world position around the arena center (0,0); north is -Z.</summary>
    public static float AngleOf(float x, float z) => Normalize(MathF.Atan2(x, -z) * 180f / MathF.PI);

    public static float Normalize(float deg)
    {
        deg %= 360f;
        return deg < 0 ? deg + 360f : deg;
    }

    public static string CompassName(float deg) => Compass[(int)MathF.Round(Normalize(deg) / 22.5f) % 16];

    /// <summary>Accepts a compass name (N, SW, NNE...) or a number of degrees.</summary>
    public static bool TryParseDirection(string text, out float deg)
    {
        var index = Array.FindIndex(Compass, c => c.Equals(text, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            deg = index * 22.5f;
            return true;
        }
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out deg);
    }

    public static DiveSolution Solve(IReadOnlyList<float> dragonAngles, IReadOnlyDictionary<string, float> waymarks)
    {
        if (dragonAngles.Count != 5)
        {
            throw new ArgumentException($"Expected 5 dragons, got {dragonAngles.Count}.", nameof(dragonAngles));
        }

        var order = dragonAngles
            .Select(Normalize)
            .Select(a => a > 360f - NorthTolerance ? a - 360f : a)
            .OrderBy(a => a)
            .ToArray();
        float[][] groups = [[order[0], order[1]], [order[2]], [order[3], order[4]]];

        var calls = new DiveCall[3];
        var cursed = false;
        for (var i = 0; i < 3; i++)
        {
            var mark = DiveWaymarks[i];
            var markAngle = waymarks.TryGetValue(mark, out var placed) ? placed : DefaultWaymarks[mark];
            var clearance = Clearance(groups[i], markAngle);
            if (clearance >= MinClearance)
            {
                calls[i] = new DiveCall(i + 1, groups[i], markAngle, mark, clearance);
                continue;
            }

            cursed = true;
            var bait = BestBait(groups[i], markAngle);
            calls[i] = new DiveCall(i + 1, groups[i], bait, null, Clearance(groups[i], bait));
        }
        return new DiveSolution(calls, cursed);
    }

    public static IEnumerable<string> Describe(DiveSolution solution)
    {
        var dragons = string.Join(" | ", solution.Calls.Select(c => string.Join(", ", c.Dragons.Select(CompassName))));
        yield return $"Dragons (clockwise from N): {dragons}";
        yield return solution.Cursed ? "CURSED pattern - don't use the default waymarks blindly" : "Standard pattern";
        foreach (var call in solution.Calls)
        {
            var spot = call.Waymark is { } mark
                ? $"{mark} ({CompassName(call.BaitAngle)})"
                : $"{CompassName(call.BaitAngle)} (moved off the usual waymark)";
            yield return $"Dive {call.Dive}: {spot}";
        }
    }

    /// <summary>Smallest distance from the arena center to any of the group's dive lines.</summary>
    public static float Clearance(float[] dragons, float bait) =>
        dragons.Min(d => LineDistanceToCenter(Point(d, DragonRadius), Point(bait, BaitRadius)));

    private static float BestBait(float[] dragons, float preferred)
    {
        // Search the edge in 5° steps; among near-equal spots take the one closest to the usual waymark.
        var candidates = Enumerable.Range(0, 72)
            .Select(i => i * 5f)
            .Select(a => (Angle: a, Clearance: Clearance(dragons, a)))
            .ToArray();
        var top = candidates.Max(c => c.Clearance);
        return candidates
            .Where(c => c.Clearance >= top - 0.02f)
            .MinBy(c => AngularDistance(c.Angle, preferred))
            .Angle;
    }

    private static float AngularDistance(float a, float b)
    {
        var d = Normalize(a - b);
        return d > 180f ? 360f - d : d;
    }

    private static (float X, float Y) Point(float deg, float radius)
    {
        var rad = deg * MathF.PI / 180f;
        return (radius * MathF.Sin(rad), -radius * MathF.Cos(rad));
    }

    private static float LineDistanceToCenter((float X, float Y) a, (float X, float Y) b)
    {
        var length = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        if (length < 1e-4f)
        {
            return 0f;
        }
        return MathF.Abs(a.X * b.Y - a.Y * b.X) / length;
    }
}
