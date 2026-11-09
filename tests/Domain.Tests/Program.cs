using System.Text.Json;
using System.Text.Json.Nodes;
using Miki.OpenBim;

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool condition, string message = "Assertion failed")
{
    if (!condition) throw new Exception(message);
}
void Reject(Action action)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new Exception("Expected InvalidOperationException");
}
JsonObject Object(string json) => JsonNode.Parse(json)!.AsObject();
BuildingProject Fixture() => new(1, "TEST", "Independent fixture", 0, "m",
    [new("L1", 0, 3.2), new("L2", 3.2, 3.2)],
    [new("AX", [0, 0], [12, 0])],
    [new("ROOM", "L1", "Living", [[0, 0], [6, 0], [6, 8], [0, 8]])],
    [new("W1", "L1", [0, 0], [12, 0], .24),
     new("W2", "L2", [0, 0], [12, 0], .24),
     new("W3", "L1", [12, 0], [12, 8], .24)],
    [new("S1", "L1", [[0, 0], [12, 0], [12, 8], [0, 8]], .2),
     new BimSlab("S2", "L2", [[0, 0], [12, 0], [12, 8], [0, 8]], .2)
     { Voids = [[[3.1, 2.4], [8, 2.4], [8, 3.7], [3.1, 3.7]]] }],
    [new("WIN-A", "window", "rectangular", 1.2, 1.4, .12),
     new("WIN-B", "window", "rectangular", .8, 1, .12),
     new("DOOR-A", "door", "rectangular", .9, 2.1, .12)],
    [new("O1", "W1", "WIN-A", 1, .9), new("O2", "W1", "WIN-A", 4, .9),
     new("O3", "W2", "WIN-A", 1, .9), new("O4", "W2", "WIN-A", 4, .9),
     new("OB", "W3", "WIN-B", 1, 1), new("OD", "W1", "DOOR-A", 8, 0)],
    [new("ST", "L1", "L2", [3.2, 2.5], [1, 0], 1.1, 4.8, 18)]);
BuildingProject Edit(string patch) => ProjectEngine.ApplyPatch(Fixture(), Object(patch));
void Affected(string patch, params string[] expected)
{
    var before = Fixture();
    var after = ProjectEngine.ApplyPatch(before, Object(patch));
    var actual = ProjectEngine.AffectedIds(before, after);
    Check(actual.SequenceEqual(expected.Order(StringComparer.Ordinal)), $"Affected IDs: {string.Join(',', actual)}");
}

Test("validate independent fixture", () => ProjectEngine.Validate(Fixture()));
Test("strict camelCase JSON round trip", () =>
{
    var json = ProjectEngine.Serialize(Fixture());
    var parsed = ProjectEngine.Parse(json);
    Check(parsed.Id == "TEST" && parsed.Levels[1].Elevation == 3.2 && parsed.Openings.Length == 6);
    Check(Object(json)["schemaVersion"]!.GetValue<int>() == 1 && !json.Contains("SchemaVersion"));
    Check(ProjectEngine.Serialize(parsed) == json);
});
Test("shared options reject unknown members", () =>
{
    try { JsonSerializer.Deserialize<BimLevel>("""{"id":"L","elevation":0,"height":3,"heigth":4}""", ProjectEngine.JsonOptions); }
    catch (JsonException) { return; }
    throw new Exception("Accepted typo");
});
Test("default omitted wall height and slab offset", () =>
{
    var json = JsonSerializer.SerializeToNode(Fixture(), ProjectEngine.JsonOptions)!.AsObject();
    json["walls"]![0]!.AsObject().Remove("height");
    json["slabs"]![0]!.AsObject().Remove("offset");
    json["slabs"]![0]!.AsObject().Remove("voids");
    var parsed = ProjectEngine.Parse(json.ToJsonString());
    Check(parsed.Walls[0].Height is null && parsed.Slabs[0].Offset == 0 && parsed.Slabs[0].Voids.Length == 0);
});
foreach (var kind in new[] { "window", "door" })
{
    Test($"omitted {kind} depth defaults to 0.12 and serializes explicitly", () =>
    {
        var project = Fixture();
        var index = Array.FindIndex(project.Types, type => type.Kind == kind);
        var json = JsonSerializer.SerializeToNode(project, ProjectEngine.JsonOptions)!.AsObject();
        json["types"]![index]!.AsObject().Remove("depth");
        var parsed = ProjectEngine.Parse(json.ToJsonString());
        Check(parsed.Types[index].Depth == .12);
        var resolved = Object(ProjectEngine.Serialize(parsed));
        Check(resolved["types"]![index]!["depth"]!.GetValue<double>() == .12);
    });
    Test($"new {kind} type patch may omit depth", () =>
    {
        var patch = new JsonObject { ["types"] = new JsonArray(new JsonObject
        {
            ["id"] = "NEW-TYPE", ["kind"] = kind, ["form"] = "rectangular", ["width"] = 1, ["height"] = 1.4
        }) };
        var after = ProjectEngine.ApplyPatch(Fixture(), patch);
        Check(after.Types.Single(type => type.Id == "NEW-TYPE").Depth == .12 && after.Revision == 1);
    });
    Test($"{kind} form remains required with optional depth", () =>
    {
        var project = Fixture();
        var index = Array.FindIndex(project.Types, type => type.Kind == kind);
        var json = JsonSerializer.SerializeToNode(project, ProjectEngine.JsonOptions)!.AsObject();
        json["types"]![index]!.AsObject().Remove("form");
        Reject(() => ProjectEngine.Parse(json.ToJsonString()));
    });
    Test($"{kind} explicit null depth is invalid", () =>
    {
        var project = Fixture();
        var index = Array.FindIndex(project.Types, type => type.Kind == kind);
        var json = JsonSerializer.SerializeToNode(project, ProjectEngine.JsonOptions)!.AsObject();
        json["types"]![index]!["depth"] = null;
        Reject(() => ProjectEngine.Parse(json.ToJsonString()));
    });
}
Test("partial type patch preserves explicit nondefault depth", () =>
{
    var project = Fixture();
    project = project with { Types = [project.Types[0] with { Depth = .18 }, ..project.Types.Skip(1)] };
    var after = ProjectEngine.ApplyPatch(project, Object("""{"types":[{"id":"WIN-A","width":1.5}]}"""));
    Check(after.Types[0].Depth == .18 && ProjectEngine.Parse(ProjectEngine.Serialize(after)).Types[0].Depth == .18);
});
foreach (var json in new[] { "null", "[]", "{}", "{", "", "{\"id\":1}", "{\"id\":\"A\",\"id\":\"B\"}" })
    Test($"reject malformed project {json}", () => Reject(() => ProjectEngine.Parse(json)));
foreach (var property in new[] { "schemaVersion", "id", "name", "revision", "units", "levels", "grids", "spaces", "walls", "slabs", "types", "openings", "stairs" })
    Test($"reject missing root {property}", () =>
    {
        var json = JsonSerializer.SerializeToNode(Fixture(), ProjectEngine.JsonOptions)!.AsObject();
        json.Remove(property);
        Reject(() => ProjectEngine.Parse(json.ToJsonString()));
    });
foreach (var collection in new[] { "levels", "grids", "spaces", "walls", "slabs", "types", "openings", "stairs" })
{
    Test($"reject unknown property on {collection}", () =>
    {
        var json = JsonSerializer.SerializeToNode(Fixture(), ProjectEngine.JsonOptions)!.AsObject();
        json[collection]![0]!["typo"] = 42;
        Reject(() => ProjectEngine.Parse(json.ToJsonString()));
    });
    Test($"reject null collection {collection}", () =>
    {
        var json = JsonSerializer.SerializeToNode(Fixture(), ProjectEngine.JsonOptions)!.AsObject();
        json[collection] = null;
        Reject(() => ProjectEngine.Parse(json.ToJsonString()));
    });
    Test($"reject null entry in {collection}", () =>
    {
        var json = JsonSerializer.SerializeToNode(Fixture(), ProjectEngine.JsonOptions)!.AsObject();
        json[collection]![0] = null;
        Reject(() => ProjectEngine.Parse(json.ToJsonString()));
    });
}
Test("reject unknown root property", () => Reject(() => Edit("""{"material":"ice"}""")));
Test("reject duplicate JSON properties on valid object", () =>
    Reject(() => ProjectEngine.Parse(JsonSerializer.Serialize(Fixture(), ProjectEngine.JsonOptions).Replace("\"height\": 3.2", "\"height\": 3.2, \"height\": 3.3"))));

var invalidProjects = new (string Name, Func<BuildingProject, BuildingProject> Change)[]
{
    ("schema", project => project with { SchemaVersion = 2 }),
    ("units", project => project with { Units = "mm" }),
    ("negative revision", project => project with { Revision = -1 }),
    ("empty name", project => project with { Name = " " }),
    ("non ASCII ID", project => project with { Id = "项目" }),
    ("duplicate cross collection IDs", project => project with { Grids = [project.Grids[0] with { Id = "L1" }] }),
    ("duplicate project ID", project => project with { Grids = [project.Grids[0] with { Id = "TEST" }] }),
    ("empty levels", project => project with { Levels = [] }),
    ("null root", _ => null!),
    ("NaN elevation", project => project with { Levels = [project.Levels[0] with { Elevation = double.NaN }, project.Levels[1]] }),
    ("infinite height", project => project with { Levels = [project.Levels[0] with { Height = double.PositiveInfinity }, project.Levels[1]] }),
    ("zero level height", project => project with { Levels = [project.Levels[0] with { Height = 0 }, project.Levels[1]] }),
    ("unbounded height", project => project with { Levels = [project.Levels[0] with { Height = 1e12 }, project.Levels[1]] }),
    ("bad XY arity", project => project with { Grids = [project.Grids[0] with { Start = [0, 0, 0] }] }),
    ("null XY", project => project with { Grids = [project.Grids[0] with { Start = null! }] }),
    ("nonfinite XY", project => project with { Grids = [project.Grids[0] with { Start = [double.NegativeInfinity, 0] }] }),
    ("unbounded XY", project => project with { Grids = [project.Grids[0] with { Start = [1e12, 0] }] }),
    ("zero grid", project => project with { Grids = [project.Grids[0] with { End = [0, 0] }] }),
    ("zero wall", project => project with { Walls = [project.Walls[0] with { End = [0, 0] }, ..project.Walls.Skip(1)] }),
    ("negative wall thickness", project => project with { Walls = [project.Walls[0] with { Thickness = -.24 }, ..project.Walls.Skip(1)] }),
    ("invalid optional height", project => project with { Walls = [project.Walls[0] with { Height = double.NaN }, ..project.Walls.Skip(1)] }),
    ("unknown wall level", project => project with { Walls = [project.Walls[0] with { Level = "MISSING" }, ..project.Walls.Skip(1)] }),
    ("unknown space level", project => project with { Spaces = [project.Spaces[0] with { Level = "MISSING" }] }),
    ("unknown slab level", project => project with { Slabs = [project.Slabs[0] with { Level = "MISSING" }, project.Slabs[1]] }),
    ("bad slab thickness", project => project with { Slabs = [project.Slabs[0] with { Thickness = 0 }, project.Slabs[1]] }),
    ("bad slab offset", project => project with { Slabs = [project.Slabs[0] with { Offset = double.NaN }, project.Slabs[1]] }),
    ("bad kind", project => project with { Types = [project.Types[0] with { Kind = "arch" }, ..project.Types.Skip(1)] }),
    ("bad form", project => project with { Types = [project.Types[0] with { Form = "circular" }, ..project.Types.Skip(1)] }),
    ("bad type depth", project => project with { Types = [project.Types[0] with { Depth = 0 }, ..project.Types.Skip(1)] }),
    ("unknown wall reference", project => project with { Openings = [project.Openings[0] with { Wall = "MISSING" }, ..project.Openings.Skip(1)] }),
    ("unknown type reference", project => project with { Openings = [project.Openings[0] with { Type = "MISSING" }, ..project.Openings.Skip(1)] }),
    ("negative opening offset", project => project with { Openings = [project.Openings[0] with { Offset = -.1 }, ..project.Openings.Skip(1)] }),
    ("negative sill", project => project with { Openings = [project.Openings[0] with { Sill = -.1 }, ..project.Openings.Skip(1)] }),
    ("opening beyond wall length", project => project with { Openings = [project.Openings[0] with { Offset = 11.5 }, ..project.Openings.Skip(1)] }),
    ("opening beyond inherited wall height", project => project with { Openings = [project.Openings[0] with { Sill = 2 }, ..project.Openings.Skip(1)] }),
    ("overlapping openings", project => project with { Openings = [project.Openings[0] with { Offset = 4.5 }, ..project.Openings.Skip(1)] }),
    ("too few polygon points", project => project with { Spaces = [project.Spaces[0] with { Boundary = [[0, 0], [1, 0]] }] }),
    ("self crossing polygon", project => project with { Spaces = [project.Spaces[0] with { Boundary = [[0, 0], [4, 4], [0, 4], [4, 0]] }] }),
    ("collinear polygon", project => project with { Spaces = [project.Spaces[0] with { Boundary = [[0, 0], [1, 0], [2, 0]] }] }),
    ("repeated polygon edge", project => project with { Spaces = [project.Spaces[0] with { Boundary = [[0, 0], [6, 0], [6, 0], [0, 8]] }] }),
    ("self touching polygon", project => project with { Spaces = [project.Spaces[0] with { Boundary = [[0, 0], [4, 0], [4, 4], [2, 0], [0, 4]] }] }),
    ("backtracking polygon edge", project => project with { Spaces = [project.Spaces[0] with { Boundary = [[0, 0], [4, 0], [2, 0], [4, 4], [0, 4]] }] }),
    ("outside slab void", project => project with { Slabs = [project.Slabs[0] with { Voids = [[[11, 1], [13, 1], [13, 2], [11, 2]]] }, project.Slabs[1]] }),
    ("touching outer slab void", project => project with { Slabs = [project.Slabs[0] with { Voids = [[[0, 1], [1, 1], [1, 2], [0, 2]]] }, project.Slabs[1]] }),
    ("overlapping slab voids", project => project with { Slabs = [project.Slabs[0] with { Voids = [[[1, 1], [3, 1], [3, 3], [1, 3]], [[2, 2], [4, 2], [4, 4], [2, 4]]] }, project.Slabs[1]] }),
    ("nested slab voids", project => project with { Slabs = [project.Slabs[0] with { Voids = [[[1, 1], [4, 1], [4, 4], [1, 4]], [[2, 2], [3, 2], [3, 3], [2, 3]]] }, project.Slabs[1]] }),
    ("null slab voids", project => project with { Slabs = [project.Slabs[0] with { Voids = null! }, project.Slabs[1]] }),
    ("zero stair direction", project => project with { Stairs = [project.Stairs[0] with { Direction = [0, 0] }] }),
    ("nonfinite stair run", project => project with { Stairs = [project.Stairs[0] with { Run = double.PositiveInfinity }] }),
    ("negative stair width", project => project with { Stairs = [project.Stairs[0] with { Width = -1 }] }),
    ("too few risers", project => project with { Stairs = [project.Stairs[0] with { Risers = 1 }] }),
    ("unbounded risers", project => project with { Stairs = [project.Stairs[0] with { Risers = int.MaxValue }] }),
    ("same stair levels", project => project with { Stairs = [project.Stairs[0] with { To = "L1" }] }),
    ("descending stair", project => project with { Stairs = [project.Stairs[0] with { From = "L2", To = "L1" }] }),
    ("unknown stair level", project => project with { Stairs = [project.Stairs[0] with { To = "MISSING" }] }),
    ("missing upper slab void", project => project with { Slabs = [project.Slabs[0], project.Slabs[1] with { Voids = [] }] }),
    ("flight outside void", project => project with { Stairs = [project.Stairs[0] with { Origin = [3.2, 4] }] }),
    ("unreachable top landing", project => project with { Stairs = [project.Stairs[0] with { Run = 8.8 }], Slabs = [project.Slabs[0], project.Slabs[1] with { Voids = [[[3.1, 2.4], [11.99, 2.4], [11.99, 3.7], [3.1, 3.7]]] }] }),
    ("unsupported bottom landing", project => project with { Slabs = [project.Slabs[0] with { Voids = [[[2, 2], [3.3, 2], [3.3, 4], [2, 4]]] }, project.Slabs[1]] }),
    ("too many parts", project => project with { Grids = Enumerable.Range(0, 10001).Select(index => new BimGrid($"G{index}", [0, 0], [1, 0])).ToArray() })
};
foreach (var entry in invalidProjects)
    Test($"reject {entry.Name}", () => Reject(() => ProjectEngine.Validate(entry.Change(Fixture()))));

Test("generated budget rejects 6000 otherwise valid stair copies", () =>
{
    var project = Fixture();
    project = project with { Stairs = Enumerable.Range(0, 6000)
        .Select(index => project.Stairs[0] with { Id = $"ST-{index}", Risers = 4 }).ToArray() };
    Reject(() => ProjectEngine.Validate(project));
});
Test("generated budget rejects five thousand-riser flights", () =>
{
    var project = Fixture();
    project = project with { Stairs = Enumerable.Range(0, 5)
        .Select(index => project.Stairs[0] with { Id = $"ST-{index}", Risers = 1000 }).ToArray() };
    Reject(() => ProjectEngine.Validate(project));
});
Test("generated budget counts the shared definitions and project state", () =>
{
    Check(ProjectEngine.EstimateGeneratedParts(Fixture()) == 59);
    Check(ProjectEngine.EstimateGeneratedParts(ProjectEngine.CreateDemo()) == 87);
    var empty = new BuildingProject(1, "EMPTY", "Empty concept", 0, "m", [new("LEVEL", 0, 3)], [], [], [], [], [], [], []);
    Check(ProjectEngine.EstimateGeneratedParts(empty) == 1);
});
var generatedWeights = new (string Name, Func<BuildingProject, BuildingProject> Change, long Expected)[]
{
    ("grid", project => project with { Grids = [..project.Grids, new("NEW-GRID", [0, 0], [0, 8])] }, 60),
    ("space", project => project with { Spaces = [..project.Spaces, project.Spaces[0] with { Id = "NEW-SPACE" }] }, 60),
    ("wall and plan", project => project with { Walls = [..project.Walls, project.Walls[0] with { Id = "NEW-WALL" }] }, 61),
    ("slab and plan", project => project with { Slabs = [..project.Slabs, project.Slabs[0] with { Id = "NEW-SLAB" }] }, 61),
    ("opening and plan", project => project with { Openings = [..project.Openings, new("NEW-OPENING", "W3", "WIN-B", 4, 1)] }, 61),
    ("five type solids", project => project with { Types = [..project.Types, project.Types[0] with { Id = "NEW-TYPE" }] }, 64),
    ("extra riser", project => project with { Stairs = [project.Stairs[0] with { Risers = 19 }] }, 60),
    ("flight with landing", project => project with { Stairs = [..project.Stairs, project.Stairs[0] with { Id = "NEW-STAIR" }] }, 78),
    ("level metadata", project => project with { Levels = [..project.Levels, new("NEW-LEVEL", 6.4, 3.2)] }, 59)
};
foreach (var entry in generatedWeights)
    Test($"generated estimate weights {entry.Name}", () =>
    {
        var project = entry.Change(Fixture());
        ProjectEngine.Validate(project);
        Check(ProjectEngine.EstimateGeneratedParts(project) == entry.Expected);
    });
Test("generated estimate uses long for individual risers and their sum", () =>
{
    var project = Fixture();
    project = project with { Stairs = [project.Stairs[0] with { Risers = int.MaxValue },
        project.Stairs[0] with { Id = "SECOND", Risers = int.MaxValue }] };
    Check(ProjectEngine.EstimateGeneratedParts(project) == 4294967336L);
    Reject(() => ProjectEngine.Validate(project));
});
Test("generated estimate covers nine million expanded stair parts", () =>
{
    var project = Fixture();
    project = project with { Stairs = Enumerable.Range(0, 9000)
        .Select(index => project.Stairs[0] with { Id = $"ST-{index}", Risers = 1000 }).ToArray() };
    Check(ProjectEngine.EstimateGeneratedParts(project) == 9009040L);
    Reject(() => ProjectEngine.Validate(project));
});
Test("generated estimate rejects missing counted collections and invalid stair counts", () =>
{
    Reject(() => ProjectEngine.EstimateGeneratedParts(null!));
    Reject(() => ProjectEngine.EstimateGeneratedParts(Fixture() with { Walls = null! }));
    Reject(() => ProjectEngine.EstimateGeneratedParts(Fixture() with { Stairs = [null!] }));
    Reject(() => ProjectEngine.EstimateGeneratedParts(Fixture() with { Stairs = [Fixture().Stairs[0] with { Risers = -1 }] }));
});
BuildingProject AtGeneratedLimit()
{
    var project = Fixture();
    return project with { Grids = Enumerable.Range(0, 4942).Select(index => new BimGrid($"G-{index}", [0, 0], [12, 0])).ToArray() };
}
Test("generated budget accepts exactly 5000 parts", () =>
{
    var project = AtGeneratedLimit();
    ProjectEngine.Validate(project);
    Check(ProjectEngine.EstimateGeneratedParts(project) == 5000 && ProjectEngine.MaxGeneratedParts == 5000);
});
Test("generated budget rejects 5001 parts", () =>
{
    var project = AtGeneratedLimit();
    Reject(() => ProjectEngine.Validate(project with { Grids = [..project.Grids, new("OVER-LIMIT", [0, 0], [12, 0])] }));
});
Test("generated budget runs before detailed polygon and stair geometry", () =>
{
    var project = AtGeneratedLimit();
    project = project with { Grids = [..project.Grids, new("OVER-LIMIT", [0, 0], [12, 0])],
        Spaces = [project.Spaces[0] with { Boundary = [[0, 0], [1, 1], [0, 1], [1, 0]] }] };
    try { ProjectEngine.Validate(project); }
    catch (InvalidOperationException error)
    {
        Check(error.Message.Contains("5001") && error.Message.Contains("5000") && error.Message.Contains("generated"),
            "Expanded budget must fail before polygon processing and report estimate and limit");
        return;
    }
    throw new Exception("Expected generated budget rejection");
});
Test("generated budget protects parse serialize and atomic patches", () =>
{
    var project = AtGeneratedLimit();
    var before = ProjectEngine.Serialize(project);
    var oversized = project with { Types = [..project.Types, project.Types[0] with { Id = "EXTRA-TYPE" }] };
    Reject(() => ProjectEngine.Serialize(oversized));
    Reject(() => ProjectEngine.Parse(JsonSerializer.Serialize(oversized, ProjectEngine.JsonOptions)));
    Reject(() => ProjectEngine.ApplyPatch(project, Object("""{"types":[{"id":"EXTRA-TYPE","kind":"window","form":"rectangular","width":1,"height":1,"depth":0.12}]}""")));
    Check(ProjectEngine.Serialize(project) == before && project.Revision == 0);
});
Test("plan-first projects support walls empty with spaces and axes", () =>
{
    var project = Fixture() with { Walls = [], Openings = [], Stairs = [] };
    ProjectEngine.Validate(project);
    var parsed = ProjectEngine.Parse(ProjectEngine.Serialize(project));
    Check(parsed.Walls.Length == 0 && parsed.Grids.Length == 1 && parsed.Spaces.Length == 1);
    Check(ProjectEngine.EstimateGeneratedParts(parsed) == 22);
    var after = ProjectEngine.ApplyPatch(parsed, Object("""{"walls":[{"id":"FIRST-WALL","level":"L1","start":[0,0],"end":[12,0],"thickness":0.24}]}"""));
    Check(after.Walls.Length == 1 && after.Revision == 1);
});
Test("case-sensitive semantic IDs remain distinct through parsing and patches", () =>
{
    var project = Fixture();
    project = project with { Types = [..project.Types, project.Types[0] with { Id = "win-a" }],
        Openings = [..project.Openings, new("o1", "W3", "win-a", 4, .9)] };
    var parsed = ProjectEngine.Parse(ProjectEngine.Serialize(project));
    Check(parsed.Types.Any(type => type.Id == "WIN-A") && parsed.Types.Any(type => type.Id == "win-a"));
    var after = ProjectEngine.ApplyPatch(parsed, Object("""{"types":[{"id":"win-a","width":1.5}]}"""));
    Check(after.Types.Single(type => type.Id == "WIN-A").Width == 1.2);
    Check(ProjectEngine.AffectedIds(parsed, after).SequenceEqual(new[] { "W3", "o1", "win-a" }));
});

Test("explicit wall height overrides storey height", () =>
{
    var project = Fixture();
    ProjectEngine.Validate(project with { Walls = [project.Walls[0] with { Height = 4 }, ..project.Walls.Skip(1)], Openings = [project.Openings[0] with { Sill = 2.5 }, ..project.Openings.Skip(1)] });
});
Test("vertically separated openings may share baseline position", () =>
{
    var project = Fixture();
    ProjectEngine.Validate(project with { Openings = [project.Openings[0] with { Type = "WIN-B", Sill = .1 }, project.Openings[1] with { Type = "WIN-B", Offset = 1, Sill = 1.2 }, ..project.Openings.Skip(2)] });
});
Test("opening may meet wall base and length limit", () => ProjectEngine.Validate(Edit("""{"openings":[{"id":"OD","offset":11.1}]}""")));
Test("nonunit stair direction normalizes", () => ProjectEngine.Validate(Edit("""{"stairs":[{"id":"ST","direction":[10,0]}]}""")));
Test("landing slab may touch the flight end without obstructing it", () =>
{
    var project = Fixture();
    ProjectEngine.Validate(project with { Slabs = [..project.Slabs,
        new("LANDING", "L2", [[8, 2.5], [9.1, 2.5], [9.1, 3.6], [8, 3.6]], .2)] });
});
Test("intermediate storey slab cannot intersect stair flight", () =>
{
    var project = Fixture();
    Reject(() => ProjectEngine.Validate(project with
    {
        Levels = [..project.Levels, new("MID", 1.6, 1.6)],
        Slabs = [..project.Slabs, new("MID-SLAB", "MID", [[0, 0], [12, 0], [12, 8], [0, 8]], .2)]
    }));
});
Test("offset slab cannot intersect stair flight", () =>
{
    var project = Fixture();
    Reject(() => ProjectEngine.Validate(project with { Slabs = [..project.Slabs,
        new("OFFSET-SLAB", "L2", [[0, 0], [12, 0], [12, 8], [0, 8]], .2, -1)] }));
});
Test("minimal project allows empty optional collections", () =>
    ProjectEngine.Validate(new BuildingProject(1, "MINIMAL", "Minimal", 0, "m", [new("L", 0, 3)], [], [], [], [], [], [], [])));
Test("JSON failures preserve inner JsonException", () =>
{
    try { ProjectEngine.Parse("{"); }
    catch (InvalidOperationException error)
    {
        Check(error.InnerException is JsonException);
        return;
    }
    throw new Exception("Expected wrapped JSON failure");
});
Test("strict JSON rejects numeric strings and property casing", () =>
{
    var json = ProjectEngine.Serialize(Fixture());
    Reject(() => ProjectEngine.Parse(json.Replace("\"elevation\": 0", "\"elevation\": \"0\"")));
    Reject(() => ProjectEngine.Parse(json.Replace("\"height\"", "\"Height\"")));
});
Test("rotated translated project remains valid", () =>
{
    var project = Fixture();
    double[] Transform(double[] point) => [999000 - point[1], -999000 + point[0]];
    double[][] Ring(double[][] boundary) => boundary.Select(Transform).ToArray();
    ProjectEngine.Validate(project with
    {
        Grids = project.Grids.Select(grid => grid with { Start = Transform(grid.Start), End = Transform(grid.End) }).ToArray(),
        Spaces = project.Spaces.Select(space => space with { Boundary = Ring(space.Boundary) }).ToArray(),
        Walls = project.Walls.Select(wall => wall with { Start = Transform(wall.Start), End = Transform(wall.End) }).ToArray(),
        Slabs = project.Slabs.Select(slab => slab with { Boundary = Ring(slab.Boundary), Voids = slab.Voids.Select(Ring).ToArray() }).ToArray(),
        Stairs = project.Stairs.Select(stair => stair with { Origin = Transform(stair.Origin), Direction = [0, 1] }).ToArray()
    });
});
Test("clockwise polygons accepted", () =>
{
    var project = Fixture();
    ProjectEngine.Validate(project with { Spaces = [project.Spaces[0] with { Boundary = project.Spaces[0].Boundary.Reverse().ToArray() }] });
});
Test("concave simple polygons accepted", () =>
{
    var project = Fixture();
    ProjectEngine.Validate(project with { Spaces = [project.Spaces[0] with { Boundary = [[0, 0], [6, 0], [6, 4], [3, 4], [3, 8], [0, 8]] }] });
});
Test("void edge escaping concave slab rejected", () =>
{
    var project = Fixture();
    Reject(() => ProjectEngine.Validate(project with { Slabs = [project.Slabs[0] with { Boundary = [[0, 0], [12, 0], [12, 8], [7, 8], [7, 3], [5, 3], [5, 8], [0, 8]], Voids = [[[4, 2], [8, 2], [8, 6], [4, 6]]] }, project.Slabs[1]] }));
});
Test("partial type patch preserves siblings and increments once", () =>
{
    var project = Fixture();
    var before = ProjectEngine.Serialize(project);
    var patch = Object("""{"types":[{"id":"WIN-A","width":1.5}],"levels":[{"id":"L2","height":3.5}]}""");
    var patchBefore = patch.ToJsonString();
    var after = ProjectEngine.ApplyPatch(project, patch);
    Check(after.Types[0].Width == 1.5 && after.Types[0].Height == 1.4 && after.Levels[1].Height == 3.5 && after.Revision == 1);
    Check(ProjectEngine.Serialize(project) == before && patch.ToJsonString() == patchBefore);
    after.Walls[0].Start[0] = 99;
    Check(project.Walls[0].Start[0] == 0, "Patch must own its arrays");
});
Test("empty patch retains revision", () => Check(Edit("{}").Revision == 0));
Test("empty merge array retains entries and advances revision", () =>
{
    var after = Edit("""{"walls":[]}""");
    Check(after.Walls.Length == 3 && after.Revision == 1);
});
Test("unchanged nonempty patch advances once", () => Check(Edit("""{"levels":[{"id":"L2","height":3.2}]}""").Revision == 1));
Test("append complete new element", () =>
{
    var after = Edit("""{"grids":[{"id":"AY","start":[0,0],"end":[0,8]}]}""");
    Check(after.Grids.Length == 2 && after.Grids[0].Id == "AX" && after.Grids[1].Id == "AY");
});
Test("append type and its opening in same patch resolves", () =>
{
    var after = Edit("""{"openings":[{"id":"NEW-O","wall":"W3","type":"NEW-T","offset":4,"sill":1}],"types":[{"id":"NEW-T","kind":"window","form":"rectangular","width":1,"height":1,"depth":0.12}]}""");
    Check(after.Types.Length == 4 && after.Openings.Length == 7);
});
Test("nullable wall height reset restores inheritance", () =>
{
    var project = Edit("""{"walls":[{"id":"W1","height":4}]}""");
    Check(ProjectEngine.ApplyPatch(project, Object("""{"walls":[{"id":"W1","height":null}]}""")).Walls[0].Height is null);
});
foreach (var patch in new[]
{
    """{"id":"RENAMED"}""", """{"schemaVersion":2}""", """{"revision":123}""", """{"units":"mm"}""",
    """{"windows":[]}""", """{"walls":null}""", """{"walls":{}}""", """{"walls":[null]}""",
    """{"walls":[{"thickness":0.3}]}""", """{"walls":[{"id":"W1","newId":"RENAMED"}]}""",
    """{"walls":[{"id":"W1","delete":true}]}""", """{"walls":[{"id":"W1","thikness":0.3}]}""",
    """{"levels":[{"id":"L2","heigth":3.5}]}""", """{"types":[{"id":"WIN-A","width":null}]}""",
    """{"grids":[{"id":"INCOMPLETE","start":[0,0]}]}""",
    """{"grids":[{"id":"DUP","start":[0,0],"end":[1,0]},{"id":"DUP","start":[0,0],"end":[2,0]}]}""",
    """{"walls":[{"id":"W1","thickness":0.3},{"id":"W1","thickness":0.4}]}""",
    """{"types":[{"id":"WIN-A","width":10}]}""", """{"levels":[{"id":"L1","height":1}]}"""
}) Test($"reject invalid patch {patch}", () => Reject(() => Edit(patch)));
Test("invalid patch leaves source intact", () =>
{
    var project = Fixture();
    var snapshot = ProjectEngine.Serialize(project);
    Reject(() => ProjectEngine.ApplyPatch(project, Object("""{"levels":[{"id":"L2","height":3.5}],"types":[{"id":"WIN-A","width":100}]}""")));
    Check(ProjectEngine.Serialize(project) == snapshot && project.Revision == 0);
});
Test("revision overflow rejected", () => Reject(() => ProjectEngine.ApplyPatch(Fixture() with { Revision = int.MaxValue }, Object("""{"name":"Change"}"""))));
Test("null patch rejected", () => Reject(() => ProjectEngine.ApplyPatch(Fixture(), null!)));
Test("invalid source cannot be patched or serialized", () =>
{
    var project = Fixture() with { Units = "cm" };
    Reject(() => ProjectEngine.ApplyPatch(project, Object("{}")));
    Reject(() => ProjectEngine.Serialize(project));
});
Test("type width affects all matching instances and host walls", () => Affected("""{"types":[{"id":"WIN-A","width":1.5}]}""", "WIN-A", "O1", "O2", "O3", "O4", "W1", "W2"));
Test("type depth affects host walls", () => Affected("""{"types":[{"id":"WIN-A","depth":0.15}]}""", "WIN-A", "O1", "O2", "O3", "O4", "W1", "W2"));
Test("second level height affects scoped dependents", () => Affected("""{"levels":[{"id":"L2","height":3.5}]}""", "L2", "W2", "S2", "O3", "O4", "ST"));
Test("first level affects spaces slabs walls instances connecting stairs", () => Affected("""{"levels":[{"id":"L1","height":3.5}]}""", "L1", "ROOM", "W1", "W3", "S1", "O1", "O2", "OB", "OD", "ST"));
Test("opening move affects host wall only", () => Affected("""{"openings":[{"id":"O1","offset":1.5}]}""", "O1", "W1"));
Test("opening rehost affects both old and new hosts", () => Affected("""{"openings":[{"id":"O1","wall":"W3","offset":4}]}""", "O1", "W1", "W3"));
Test("wall change affects its instances", () => Affected("""{"walls":[{"id":"W1","thickness":0.3}]}""", "W1", "O1", "O2", "OD"));
Test("grid change stays local", () => Affected("""{"grids":[{"id":"AX","end":[13,0]}]}""", "AX"));
Test("name patch has no geometric effects", () => Affected("""{"name":"Renamed"}"""));
Test("round trip arrays compare structurally", () =>
{
    var project = Fixture();
    Check(ProjectEngine.AffectedIds(project, ProjectEngine.Parse(ProjectEngine.Serialize(project))).Length == 0);
});
Test("collection reordering has no geometric effects", () =>
{
    var project = Fixture();
    Check(ProjectEngine.AffectedIds(project, project with { Walls = project.Walls.Reverse().ToArray() }).Length == 0);
});
Test("new element included in affected IDs", () => Affected("""{"grids":[{"id":"AY","start":[0,0],"end":[0,8]}]}""", "AY"));
Test("different project identities rejected", () => Reject(() => ProjectEngine.AffectedIds(Fixture(), Fixture() with { Id = "OTHER" })));
Test("demo contract and shared types", () =>
{
    var demo = ProjectEngine.CreateDemo();
    ProjectEngine.Validate(demo);
    Check(demo.Levels.Length == 2 && demo.Levels[0] == new BimLevel("L1", 0, 3.2) && demo.Levels[1] == new BimLevel("L2", 3.2, 3.2));
    var type = demo.Types.Single(entry => entry.Id == "WIN-A");
    Check(type.Kind == "window" && type.Width == 1.2 && type.Height == 1.4 && type.Depth == .12);
    Check(demo.Walls.All(wall => wall.Thickness == .24 && wall.Height is null) && demo.Slabs.All(slab => slab.Thickness == .2));
    foreach (var level in demo.Levels)
        Check(demo.Openings.Count(opening => opening.Type == "WIN-A" && demo.Walls.Single(wall => wall.Id == opening.Wall).Level == level.Id) >= 2);
    Check(demo.Openings.Any(opening => demo.Types.Single(entry => entry.Id == opening.Type).Kind == "door" && opening.Sill == 0));
    Check(demo.Openings.Any(opening => opening.Type != "WIN-A" && demo.Types.Single(entry => entry.Id == opening.Type).Kind == "window"));
    Check(demo.Stairs.Single() is { From: "L1", To: "L2", Risers: 18, Run: 4.8, Width: 1.1 });
    Check(demo.Spaces.Length >= 4 && demo.Grids.Length >= 4 && demo.Slabs.Single(slab => slab.Level == "L2").Voids.Length == 1);
    var points = demo.Slabs[0].Boundary;
    Check(points.Min(point => point[0]) == 0 && points.Max(point => point[0]) == 12 && points.Min(point => point[1]) == 0 && points.Max(point => point[1]) == 8);
});
foreach (var levelId in new[] { "L1", "L2" })
{
    Test($"demo {levelId} has a full-width interior partition", () =>
    {
        var demo = ProjectEngine.CreateDemo();
        var partition = demo.Walls.SingleOrDefault(wall => wall.Id == $"WALL-{levelId}-P");
        Check(partition is not null && partition.Level == levelId
            && partition.Start.SequenceEqual([0.0, 4.0]) && partition.End.SequenceEqual([12.0, 4.0])
            && partition.Thickness == .24 && partition.Height is null, "Expected a level-height partition from [0,4] to [12,4]");
    });
    Test($"demo {levelId} partition has a shared door opening", () =>
    {
        var demo = ProjectEngine.CreateDemo();
        var doors = demo.Openings.Where(opening => opening.Wall == $"WALL-{levelId}-P").ToArray();
        Check(doors.Length == 1 && doors[0].Type == "DOOR-A" && doors[0].Offset == 8.5 && doors[0].Sill == 0,
            "Expected one DOOR-A opening at offset 8.5 and sill 0");
        Check(demo.Types.Single(type => type.Id == doors[0].Type).Kind == "door");
        ProjectEngine.Validate(demo);
    });
    Test($"demo {levelId} north zone starts beyond the partition centreline", () =>
    {
        var north = ProjectEngine.CreateDemo().Spaces.Single(space => space.Id == $"ROOM-{levelId}-N");
        Check(north.Boundary[0].SequenceEqual([.24, 4.12]) && north.Boundary[1].SequenceEqual([11.76, 4.12])
            && north.Boundary[2].SequenceEqual([11.76, 7.76]) && north.Boundary[3].SequenceEqual([.24, 7.76]),
            "Expected north boundary y=4.12 at the partition face");
    });
}
Test("demo deliberately starts at revision zero", () =>
{
    var demo = ProjectEngine.CreateDemo();
    Check(demo.Revision == 0 && ProjectEngine.ApplyPatch(demo, Object("{}")).Revision == 0);
    Check(ProjectEngine.ApplyPatch(demo, Object("""{"name":"Edited concept"}""")).Revision == 1);
});
Test("demo partition preserves stair void and landing geometry", () =>
{
    var demo = ProjectEngine.CreateDemo();
    var stair = demo.Stairs.Single();
    Check(stair.Origin.SequenceEqual([3.2, 2.5]) && stair.Direction.SequenceEqual([1.0, 0.0])
        && stair.Width == 1.1 && stair.Run == 4.8 && stair.Risers == 18);
    var hole = demo.Slabs.Single(slab => slab.Level == "L2").Voids.Single();
    Check(hole.Length == 4 && hole[0].SequenceEqual([3.1, 2.4]) && hole[1].SequenceEqual([8.0, 2.4])
        && hole[2].SequenceEqual([8.0, 3.7]) && hole[3].SequenceEqual([3.1, 3.7]));
    ProjectEngine.Validate(demo);
});
Test("demo shared door edit affects both partitions and exterior door", () =>
{
    var before = ProjectEngine.CreateDemo();
    var after = ProjectEngine.ApplyPatch(before, Object("""{"types":[{"id":"DOOR-A","width":1.0}]}"""));
    string[] expected = ["DOOR-A", "DOOR-L1", "DOOR-L1-P", "DOOR-L2-P", "WALL-L1-S", "WALL-L1-P", "WALL-L2-P"];
    Check(ProjectEngine.AffectedIds(before, after).SequenceEqual(expected.Order(StringComparer.Ordinal)),
        "Door type updates must include exactly its instances and hosts");
});
Test("demo upper storey height affects its partition and door", () =>
{
    var before = ProjectEngine.CreateDemo();
    var after = ProjectEngine.ApplyPatch(before, Object("""{"levels":[{"id":"L2","height":3.5}]}"""));
    var affected = ProjectEngine.AffectedIds(before, after);
    Check(affected.Contains("WALL-L2-P") && affected.Contains("DOOR-L2-P")
        && !affected.Contains("WALL-L1-P") && !affected.Contains("DOOR-L1-P"),
        "Partition and door dependencies must stay storey-scoped");
});
Test("demo survives acceptance patches with unrelated type unchanged", () =>
{
    var before = ProjectEngine.CreateDemo();
    var after = ProjectEngine.ApplyPatch(before, Object("""{"types":[{"id":"WIN-A","width":1.5}]}"""));
    var affected = ProjectEngine.AffectedIds(before, after);
    foreach (var opening in before.Openings.Where(opening => opening.Type == "WIN-A"))
        Check(affected.Contains(opening.Id) && affected.Contains(opening.Wall));
    foreach (var opening in before.Openings.Where(opening => opening.Type != "WIN-A")) Check(!affected.Contains(opening.Id));
    var taller = ProjectEngine.ApplyPatch(after, Object("""{"levels":[{"id":"L2","height":3.5}]}"""));
    var heightAffected = ProjectEngine.AffectedIds(after, taller);
    Check(taller.Revision == before.Revision + 2);
    foreach (var wall in before.Walls.Where(wall => wall.Level == "L1")) Check(!heightAffected.Contains(wall.Id));
});
Test("example JSON matches demo and validates", () =>
{
    var path = Path.Combine(AppContext.BaseDirectory, "two-storey.json");
    var example = ProjectEngine.Parse(File.ReadAllText(path));
    Check(ProjectEngine.Serialize(example) == ProjectEngine.Serialize(ProjectEngine.CreateDemo()));
});

var passed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        passed++;
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception error)
    {
        Console.WriteLine($"FAIL {test.Name} [{error.GetType().Name}] {error.Message}");
    }
}
Console.WriteLine($"RESULT {passed}/{tests.Count} passed, {tests.Count - passed} failed");
return passed == tests.Count ? 0 : 1;
