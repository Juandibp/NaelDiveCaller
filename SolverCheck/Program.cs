using NaelDiveCaller;

// Runs the solver on dragon patterns given as arguments ("N,NNE,S,W,NW") or on the raidplan examples,
// once with no waymarks placed and once with the raidplan's waymark layout.
var raidplanMarkers = new Dictionary<string, float>
{
    ["A"] = 55f, ["B"] = 154f, ["C"] = 235f, ["1"] = 0f, ["2"] = 246f, ["3"] = 116f,
};
string[] patterns = args.Length > 0 ? args : ["N,NNE,S,W,NW", "N,S,SW,W,NW"];
foreach (var pattern in patterns)
{
    var angles = pattern.Split(',').Select(p => DiveSolver.TryParseDirection(p, out var a) ? a : throw new ArgumentException(p)).ToArray();
    Console.WriteLine($"== {pattern}");
    var solution = DiveSolver.Solve(angles, DiveSolver.DefaultWaymarks);
    foreach (var markers in new[] { new Dictionary<string, float>(), raidplanMarkers })
    {
        Console.WriteLine(markers.Count == 0 ? "-- no waymarks" : "-- raidplan waymarks");
        foreach (var line in DiveSolver.Describe(solution, markers))
        {
            Console.WriteLine(line);
        }
    }
    Console.WriteLine(string.Join("  ", solution.Calls.Select(c => $"clearance{c.Dive}={c.Clearance:F2}")));
}
