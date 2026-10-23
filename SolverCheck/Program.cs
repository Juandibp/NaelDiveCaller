using NaelDiveCaller;

// Runs the solver on dragon patterns given as arguments ("N,NNE,S,W,NW") or on the raidplan examples.
string[] patterns = args.Length > 0 ? args : ["N,NNE,S,W,NW", "N,S,SW,W,NW"];
foreach (var pattern in patterns)
{
    var angles = pattern.Split(',').Select(p => DiveSolver.TryParseDirection(p, out var a) ? a : throw new ArgumentException(p)).ToArray();
    Console.WriteLine($"== {pattern}");
    var solution = DiveSolver.Solve(angles, DiveSolver.DefaultWaymarks);
    foreach (var line in DiveSolver.Describe(solution))
    {
        Console.WriteLine(line);
    }
    Console.WriteLine(string.Join("  ", solution.Calls.Select(c => $"clearance{c.Dive}={c.Clearance:F2}")));
}
