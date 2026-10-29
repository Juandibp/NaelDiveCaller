using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NaelDiveCaller;

/// <summary>One divebomb: the dragons that dive on it and where its bait player should stand.</summary>
public sealed record DiveCall(int Dive, float[] Dragons, float BaitAngle, string? Waymark, float Clearance);

/// <summary>The three calls, plus why the usual A/B/C spots don't work (empty for a standard pattern).</summary>
public sealed record DiveSolution(DiveCall[] Calls, string[] Problems)
{
    public bool Cursed => Problems.Length > 0;
}

/// <summary>
/// Angles are in degrees clockwise from north. Dragons are taken clockwise from north in a 2-1-2 split:
/// the first divebomb gets dragons 1-2, the second dragon 3, the third dragons 4-5.
/// A dive is a wide band from the dragon to a little past its bait spot. The usual spots
/// (A/B/C) are used unless a dive would cut through the middle or run over the spot where a later
/// bait player is waiting; then the three spots are chosen together, moving as few as possible
/// (the "cursed" pattern).
/// </summary>
public static class DiveSolver
{
    // A dive line closer than this to the center (as a fraction of the arena radius) clips the group.
    public const float MinClearance = 0.6f;

    // Half the width of a dive band, as a fraction of the arena radius. In game a dive from the SE
    // dragon onto A killed the dive 2 bait player standing on B about 8y to the side of it.
    public const float DefaultHalfWidth = 9f / 22f;

    // How far a dive band carries on past the bait spot, as a fraction of the arena radius. Treating
    // dives as crossing the whole arena flagged most layouts as cursed, which doesn't match play.
    private const float Overshoot = 0.2f;

    // Extra room (fraction of the arena radius) a moved spot keeps from other dives' bands.
    private const float SearchMargin = 4f / 22f;

    // Bait spots tried when the usual waymarks don't work.
    private const float SearchStep = 10f;

    // Dragons sit on the arena edge; bait players stand just inside it.
    // Dragon distance from the center as a fraction of the arena radius, used when it isn't measured.
    // Measured in game: dragons sit about 23.6y out on a 22y arena.
    // The further out the dragons are, the more a dive onto a nearby bait spot points at the middle.
    public const float DefaultDragonRadius = 1.07f;
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

    private static readonly string[] Plain =
    [
        "North", "North, slightly east", "North-east", "East, slightly north",
        "East", "East, slightly south", "South-east", "South, slightly east",
        "South", "South, slightly west", "South-west", "West, slightly south",
        "West", "West, slightly north", "North-west", "North, slightly west",
    ];

    // A spot this close to a waymark is called as standing on it.
    private const float OnMarkerDegrees = 12f;

    // Only describe a spot as between two waymarks when they're reasonably close together.
    private const float MaxBetweenGap = 120f;

    /// <summary>Angle of a world position around the arena center (0,0); north is -Z.</summary>
    public static float AngleOf(float x, float z) => Normalize(MathF.Atan2(x, -z) * 180f / MathF.PI);

    public static float Normalize(float deg)
    {
        deg %= 360f;
        return deg < 0 ? deg + 360f : deg;
    }

    public static string CompassName(float deg) => Compass[(int)MathF.Round(Normalize(deg) / 22.5f) % 16];

    public static string PlainDirection(float deg) => Plain[(int)MathF.Round(Normalize(deg) / 22.5f) % 16];

    /// <summary>
    /// Describes a spot on the edge in words, using the placed waymarks when there are any:
    /// "at 3", "between 3 and A (closer to 3)", or just "South, slightly east".
    /// </summary>
    public static string SpotName(float deg, IReadOnlyDictionary<string, float> markers)
    {
        var plain = PlainDirection(deg);
        if (markers.Count == 0)
        {
            return plain;
        }

        var nearest = markers.MinBy(m => AngularDistance(m.Value, deg));
        if (AngularDistance(nearest.Value, deg) <= OnMarkerDegrees)
        {
            return $"{plain} - at {nearest.Key}";
        }

        // Closest waymark on each side, going counter-clockwise and clockwise from the spot.
        var before = markers.MinBy(m => Normalize(deg - m.Value));
        var after = markers.MinBy(m => Normalize(m.Value - deg));
        var gap = Normalize(after.Value - before.Value);
        if (before.Key == after.Key || gap > MaxBetweenGap)
        {
            return $"{plain} - near {nearest.Key}";
        }

        var toBefore = Normalize(deg - before.Value);
        var toAfter = Normalize(after.Value - deg);
        var closer = MathF.Abs(toBefore - toAfter) < gap * 0.2f
            ? ""
            : $" (closer to {(toBefore < toAfter ? before.Key : after.Key)})";
        return $"{plain} - between {before.Key} and {after.Key}{closer}";
    }

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

    public static DiveSolution Solve(
        IReadOnlyList<float> dragonAngles,
        IReadOnlyDictionary<string, float> waymarks,
        float dragonRadius = DefaultDragonRadius,
        float halfWidth = DefaultHalfWidth)
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
        var defaults = DiveWaymarks
            .Select(m => waymarks.TryGetValue(m, out var placed) ? placed : DefaultWaymarks[m])
            .ToArray();

        // Candidate spots per dive: the usual waymark first, then the edge in fixed steps.
        var steps = Enumerable.Range(0, (int)(360f / SearchStep)).Select(i => i * SearchStep);
        var candidates = defaults.Select(d => steps.Prepend(d).ToArray()).ToArray();
        var clearance = Enumerable.Range(0, 3)
            .Select(i => candidates[i].Select(a => Clearance(groups[i], a, dragonRadius)).ToArray())
            .ToArray();

        // New spots keep a margin from the bands so the call doesn't hinge on a few yalms.
        bool Clips(int dive, int spot, int later, int laterSpot) =>
            Hits(groups[dive], candidates[dive][spot], candidates[later][laterSpot], dragonRadius, halfWidth + SearchMargin);

        var problems = Problems(groups, defaults, dragonRadius, halfWidth);
        int[] best = [0, 0, 0];
        if (problems.Length > 0)
        {
            // Fewest moved spots, then the most room around the middle, then closest to the usual spots.
            var bestScore = (Moved: int.MaxValue, Clearance: 0f, Drift: 0f);
            var n = candidates[0].Length;
            for (var a = 0; a < n; a++)
            {
                for (var b = 0; b < n; b++)
                {
                    if (clearance[0][a] < MinClearance || clearance[1][b] < MinClearance || Clips(0, a, 1, b))
                    {
                        continue;
                    }
                    for (var c = 0; c < n; c++)
                    {
                        if (clearance[2][c] < MinClearance || Clips(0, a, 2, c) || Clips(1, b, 2, c))
                        {
                            continue;
                        }
                        var score = (
                            Moved: (a > 0 ? 1 : 0) + (b > 0 ? 1 : 0) + (c > 0 ? 1 : 0),
                            Clearance: MathF.Min(clearance[0][a], MathF.Min(clearance[1][b], clearance[2][c])),
                            Drift: AngularDistance(candidates[0][a], defaults[0])
                                + AngularDistance(candidates[1][b], defaults[1])
                                + AngularDistance(candidates[2][c], defaults[2]));
                        var better = score.Moved != bestScore.Moved
                            ? score.Moved < bestScore.Moved
                            : MathF.Abs(score.Clearance - bestScore.Clearance) > 0.02f
                                ? score.Clearance > bestScore.Clearance
                                : score.Drift < bestScore.Drift;
                        if (better)
                        {
                            bestScore = score;
                            best = [a, b, c];
                        }
                    }
                }
            }

            if (bestScore.Moved == int.MaxValue)
            {
                problems = [.. problems, "no layout avoids everything, spots only keep dives off the middle"];
                best = Enumerable.Range(0, 3)
                    .Select(i => Enumerable.Range(0, n).MaxBy(k => clearance[i][k]))
                    .ToArray();
            }
        }

        var calls = Enumerable.Range(0, 3)
            .Select(i => new DiveCall(
                i + 1,
                groups[i],
                candidates[i][best[i]],
                best[i] == 0 ? DiveWaymarks[i] : null,
                clearance[i][best[i]]))
            .ToArray();
        return new DiveSolution(calls, problems);
    }

    /// <summary>What goes wrong when every dive is baited on its usual waymark.</summary>
    private static string[] Problems(float[][] groups, float[] spots, float dragonRadius, float halfWidth)
    {
        var problems = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            if (Clearance(groups[i], spots[i], dragonRadius) < MinClearance)
            {
                problems.Add($"dive {i + 1} on {DiveWaymarks[i]} cuts through the middle");
            }
            for (var j = i + 1; j < 3; j++)
            {
                if (Hits(groups[i], spots[i], spots[j], dragonRadius, halfWidth))
                {
                    problems.Add($"dive {i + 1} on {DiveWaymarks[i]} hits the dive {j + 1} spot ({DiveWaymarks[j]})");
                }
            }
        }
        return [.. problems];
    }

    public static IEnumerable<string> Describe(DiveSolution solution, IReadOnlyDictionary<string, float> markers)
    {
        var dragons = string.Join(" | ", solution.Calls.Select(c => string.Join(", ", c.Dragons.Select(CompassName))));
        yield return $"Dragons (clockwise from N): {dragons}";
        yield return solution.Cursed ? $"CURSED pattern: {string.Join("; ", solution.Problems)}" : "Standard pattern";
        foreach (var call in solution.Calls)
        {
            var spot = call.Waymark is { } mark
                ? $"{mark} ({PlainDirection(call.BaitAngle)})"
                : SpotName(call.BaitAngle, markers);
            yield return $"Dive {call.Dive}: {spot}";
        }
    }

    /// <summary>Smallest distance from the arena center to any of the group's dive lines.</summary>
    public static float Clearance(float[] dragons, float bait, float dragonRadius) =>
        dragons.Min(d => LineDistanceToCenter(Point(d, dragonRadius), Point(bait, BaitRadius)));

    /// <summary>Whether any of the group's dive bands toward <paramref name="bait"/> covers the spot at <paramref name="spot"/>.</summary>
    private static bool Hits(float[] dragons, float bait, float spot, float dragonRadius, float halfWidth)
    {
        var target = Point(bait, BaitRadius);
        var player = Point(spot, BaitRadius);
        foreach (var dragon in dragons)
        {
            var start = Point(dragon, dragonRadius);
            var dx = target.X - start.X;
            var dy = target.Y - start.Y;
            var length = MathF.Sqrt(dx * dx + dy * dy);
            if (length < 1e-4f)
            {
                continue;
            }
            dx /= length;
            dy /= length;

            // The band runs from the dragon to a little past the bait spot.
            var px = player.X - start.X;
            var py = player.Y - start.Y;
            var along = Math.Clamp(px * dx + py * dy, 0f, length + Overshoot);
            var cx = px - along * dx;
            var cy = py - along * dy;
            var distance = MathF.Sqrt(cx * cx + cy * cy);
            if (distance < halfWidth)
            {
                return true;
            }
        }
        return false;
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
