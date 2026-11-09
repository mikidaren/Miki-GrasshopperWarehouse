using Miki.IceWorld;

int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + " — " + error.Message); }
}
void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
void Reject(Action action)
{
    try { action(); } catch (InvalidOperationException) { return; }
    throw new Exception("Expected input rejection");
}
ExampleModel Build(string kind, params (string Key, double Value)[] values) =>
    ExampleEngine.Build(new("IW-TEST-001", kind, values.ToDictionary(v => v.Key, v => v.Value)));

Test("straight stair uses 12 risers plus two platforms", () =>
{
    var model = Build("straight-stair", ("rise", 1.8), ("run", 3.6), ("risers", 12));
    Check(model.Prisms.Length == 14);
    var lastStep = model.Prisms.Where(p => p.Role == "step").Last();
    Check(Math.Abs(lastStep.Height - 1.8) < 1e-9);
    Check(Math.Abs(lastStep.Boundary[0].X - 3.3) < 1e-9);
    var handrails = model.Rails.Where(r => r.Role == "handrail").ToArray();
    Check(handrails.Length == 2);
    Check(Math.Abs(handrails[0].Path[^1].Z - 2.9) < 1e-9);
});
Test("switchback finishes above its lower origin with joined landing", () =>
{
    var model = Build("switchback-stair", ("rise", 3.6), ("run", 3.6), ("risers", 12), ("width", 1.5), ("gap", .2));
    Check(model.Prisms.Count(p => p.Role == "step") == 24);
    var upper = model.Prisms.Single(p => p.Role == "upper-platform");
    Check(Math.Abs(upper.Boundary[0].X + 1.5) < 1e-9);
    Check(Math.Abs(upper.Boundary[0].Z - 3.4) < 1e-9);
    Check(model.Rails.Count(r => r.Role == "handrail") == 2);
});
Test("platform perimeter keeps the full entry edge open", () =>
{
    var model = Build("platform-railing", ("length", 6), ("width", 4));
    Check(model.Prisms.Length == 1);
    Check(model.Rails.Count(r => r.Role == "handrail") == 1);
    Check(model.Rails.Single(r => r.Role == "handrail").Path.Length == 4);
});
Test("queue divides lanes with separate barriers", () =>
{
    var model = Build("queue-barriers", ("lanes", 4), ("length", 8));
    Check(model.Rails.Count(r => r.Role == "handrail") == 5);
    Check(model.Rails.Where(r => r.Role == "handrail").All(r => r.Path.Length == 2));
});
Test("curved wall produces courses times segments", () =>
{
    var model = Build("curved-ice-wall", ("segments", 8), ("courses", 3));
    Check(model.Prisms.Length == 24 && model.Rails.Length == 0);
    Check(model.Prisms.All(p => p.Height > 0 && p.Boundary.Length >= 4));
});
Test("spiral ornament preserves turn count and end elevation", () =>
{
    var model = Build("spiral-ornament", ("turns", 3), ("height", 3), ("radius", 2));
    var path = model.Rails.Single().Path;
    Check(path.Length == 145 && Math.Abs(path[^1].Z - 3) < 1e-9);
    Check(Math.Abs(path[^1].X - 2) < 1e-9 && Math.Abs(path[^1].Y) < 1e-9);
});
Test("reject unknown kind", () => Reject(() => Build("unknown")));
Test("reject unknown parameter", () => Reject(() => Build("straight-stair", ("widht", 2))));
foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1, 0 })
    Test("reject invalid width " + invalid, () => Reject(() => Build("straight-stair", ("width", invalid))));
Test("reject fractional counts", () => Reject(() => Build("straight-stair", ("risers", 12.5))));
Test("reject degenerate curved wall inner radius", () => Reject(() => Build("curved-ice-wall", ("radius", .5), ("thickness", 1))));
Test("reject invalid ID", () => Reject(() => ExampleEngine.Build(new("../bad", "straight-stair", new()))));
Test("reject geometry expansion over budget", () => Reject(() => Build("curved-ice-wall", ("segments", 256), ("courses", 32))));
Test("strict parse rejects duplicate fields", () => Reject(() => ExampleEngine.Parse("""{"id":"A","id":"B","kind":"straight-stair","parameters":{}}""")));
Test("strict parse rejects unknown metadata", () => Reject(() => ExampleEngine.Parse("""{"id":"A","kind":"straight-stair","parameters":{},"script":"run"}""")));
Test("strict parse rejects missing parameters", () => Reject(() => ExampleEngine.Parse("""{"id":"A","kind":"straight-stair"}""")));
Test("preview approval validates the source", () =>
{
    var source = new ExampleDefinition("IW-TEST", "straight-stair", new());
    Check(ExecutionGate.Evaluate("preview", source, ExecutionPolicy.Default, false).Automatic);
    source.Parameters["width"] = double.NaN;
    Check(!ExecutionGate.Evaluate("preview", source, ExecutionPolicy.Default, false).Automatic);
});
Test("new bounded bake approved while existing objects require confirmation", () =>
{
    var source = new ExampleDefinition("IW-TEST", "straight-stair", new());
    Check(ExecutionGate.Evaluate("bake", source, ExecutionPolicy.Default, false).Automatic);
    Check(!ExecutionGate.Evaluate("bake", source, ExecutionPolicy.Default, true).Automatic);
    Check(!ExecutionGate.Evaluate("delete", source, ExecutionPolicy.Default, false).Automatic);
    Check(!ExecutionGate.Evaluate("export", source, ExecutionPolicy.Default, false).Automatic);
});
Test("policy budget and disabled auto bake are enforced", () =>
{
    var source = new ExampleDefinition("IW-TEST", "straight-stair", new());
    Check(!ExecutionGate.Evaluate("bake", source, ExecutionPolicy.Default with { MaximumParts = 2 }, false).Automatic);
    Check(!ExecutionGate.Evaluate("bake", source, ExecutionPolicy.Default with { AutoBake = false }, false).Automatic);
});
Test("candidate standard gate blocks only declared candidate bake failures", () =>
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    var standards = StandardBaseline.Load(Path.Combine(root, "policies/iceworld-standard-baseline.json"));
    var source = new ExampleDefinition("IW-CANDIDATE", "straight-stair", new()
    {
        ["rise"] = 2.4
    });
    var concept = ExecutionGate.Evaluate("bake", source, ExecutionPolicy.Default, false, standards,
        new StandardDesignContext(StandardDesignContext.Concept));
    var candidate = ExecutionGate.Evaluate("bake", source, ExecutionPolicy.Default, false, standards,
        new StandardDesignContext(StandardDesignContext.StandardCandidate));
    var preview = ExecutionGate.Evaluate("preview", source, ExecutionPolicy.Default, false, standards,
        new StandardDesignContext(StandardDesignContext.StandardCandidate));
    Check(concept.Automatic);
    Check(!candidate.Automatic && candidate.Reason.Contains("Standard-candidate", StringComparison.Ordinal));
    Check(preview.Automatic);
});
Test("candidate standard gate accepts a declared compliant stair profile", () =>
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    var standards = StandardBaseline.Load(Path.Combine(root, "policies/iceworld-standard-baseline.json"));
    var source = new ExampleDefinition("IW-CANDIDATE-OK", "straight-stair", new()
    {
        ["rise"] = 2.88,
        ["run"] = 5.4,
        ["width"] = 1.1,
        ["risers"] = 18,
        ["railHeight"] = 1.1
    });
    var candidate = ExecutionGate.Evaluate("bake", source, ExecutionPolicy.Default, false, standards,
        new StandardDesignContext(StandardDesignContext.StandardCandidate));
    Check(candidate.Automatic);
});
Test("outdoor egress candidate checks stair pitch", () =>
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    var standards = StandardBaseline.Load(Path.Combine(root, "policies/iceworld-standard-baseline.json"));
    var source = new ExampleDefinition("IW-CANDIDATE-PITCH", "straight-stair", new()
    {
        ["rise"] = 2.88,
        ["run"] = 2.0,
        ["width"] = 1.1,
        ["risers"] = 18,
        ["railHeight"] = 1.1
    });
    var decision = ExecutionGate.Evaluate("bake", source, ExecutionPolicy.Default, false, standards,
        new StandardDesignContext(StandardDesignContext.StandardCandidate, OutdoorEgressStair: true));
    Check(!decision.Automatic && decision.Reason.Contains("pitch", StringComparison.OrdinalIgnoreCase));
});

if (args.Length == 2 && args[0] == "--examples")
{
    var files = Directory.GetFiles(args[1], "*.json");
    Test("warehouse contains example JSON sources", () => Check(files.Length >= 6));
    foreach (string file in files)
        Test("validate indexed numeric source " + Path.GetFileName(file), () => ExampleEngine.Parse(File.ReadAllText(file)));
}
if (args.Length == 2 && args[0] == "--warehouse")
{
    var catalog = WarehouseCatalog.Load(args[1]);
    Test("catalog loads all indexed examples", () =>
    {
        Check(catalog.Examples.Count == 6);
        foreach (var example in catalog.Examples)
        {
            var source = catalog.LoadDefinition(example);
            Check(source.Id == example.Id && source.Kind == example.Kind);
        }
    });
    Test("catalog loads execution and standards policies", () =>
    {
        Check(catalog.LoadExecutionPolicy().MaximumParts == ExampleEngine.MaximumParts);
        Check(catalog.LoadStandardBaseline().ReviewStatus == "candidate");
    });
    Test("catalog resolves a stable example by ID", () =>
        Check(catalog.Require("IW-STRAIGHT-STAIR").Category == "circulation/stairs"));
    Test("catalog rejects unknown IDs", () =>
    {
        try { catalog.Require("IW-NOT-FOUND"); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Expected unknown ID rejection");
    });
    Test("catalog rejects path escape", () =>
    {
        try { catalog.ResolveArtifact("../outside.json"); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Expected path escape rejection");
    });
}

Console.WriteLine($"RESULT {passed}/{passed + failed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
