using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Miki.IceWorld;

public sealed record ExampleDefinition(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] Dictionary<string, double> Parameters);
public sealed record Parameter(string Name, double Default, double Minimum, double Maximum, bool Integer = false);
public sealed record Point(double X, double Y, double Z);
public sealed record Prism(string Role, Point[] Boundary, double Height);
public sealed record Rail(string Role, Point[] Path, double Radius);
public sealed record ExampleModel(Prism[] Prisms, Rail[] Rails)
{
    public int PartCount => Prisms.Length + Rails.Length;
}

public static class ExampleEngine
{
    public const int MaximumParts = 5000;
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };
    public static readonly string[] Kinds = ["straight-stair", "switchback-stair", "platform-railing", "queue-barriers", "curved-ice-wall", "spiral-ornament"];

    public static Parameter[] Parameters(string kind)
    {
        Parameter[] rails = [new("railHeight", 1.1, .5, 2), new("railRadius", .025, .01, .1), new("postSpacing", 1, .25, 3)];
        return kind switch
        {
            "straight-stair" or "switchback-stair" =>
            [new("rise", kind == "straight-stair" ? 1.8 : 3.6, .3, 8), new("run", 3.6, 1, 15),
                new("width", 1.5, .6, 5), new("risers", 12, 2, 64, true),
                .. (kind == "switchback-stair" ? new Parameter[] { new("gap", .2, .1, 2) } : []), .. rails],
            "platform-railing" => [new("length", 6, 1, 30), new("width", 4, 1, 30), new("elevation", 1.8, .3, 8), .. rails],
            "queue-barriers" => [new("length", 8, 2, 30), new("laneWidth", 1.5, .8, 3), new("lanes", 4, 1, 20, true), new("turnGap", 1.8, .8, 4), .. rails],
            "curved-ice-wall" => [new("radius", 5, .5, 30), new("angle", 120, 10, 300), new("height", 2.4, .3, 8),
                new("thickness", .5, .1, 2), new("segments", 12, 2, 256, true), new("courses", 4, 1, 32, true), new("joint", .01, .001, .03)],
            "spiral-ornament" => [new("radius", 2, .5, 10), new("height", 3, .5, 10), new("turns", 3, 1, 12, true), new("tubeRadius", .04, .01, .15)],
            _ => throw new InvalidOperationException("Unknown example kind.")
        };
    }

    public static ExampleDefinition Parse(string json)
    {
        if (json.Length > 65536) throw new InvalidOperationException("Example JSON exceeds 64 KiB.");
        try
        {
            using var document = JsonDocument.Parse(json);
            UniqueProperties(document.RootElement);
            var result = JsonSerializer.Deserialize<ExampleDefinition>(json, JsonOptions)
                ?? throw new InvalidOperationException("Example must be an object.");
            Build(result);
            return result;
        }
        catch (JsonException error) { throw new InvalidOperationException("Invalid example JSON.", error); }
    }

    private static void UniqueProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new InvalidOperationException("Duplicate JSON property.");
            UniqueProperties(property.Value);
        }
    }

    public static ExampleModel Build(ExampleDefinition definition)
    {
        if (definition is null || definition.Id is null || !Regex.IsMatch(definition.Id, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$") || definition.Parameters is null)
            throw new InvalidOperationException("A valid stable ID and numeric parameters are required.");
        var specifications = Parameters(definition.Kind);
        var values = specifications.ToDictionary(p => p.Name, p => p.Default, StringComparer.Ordinal);
        foreach (var (name, value) in definition.Parameters)
        {
            var specification = specifications.SingleOrDefault(p => p.Name == name)
                ?? throw new InvalidOperationException("Unknown parameter " + name);
            if (!double.IsFinite(value) || value < specification.Minimum || value > specification.Maximum || specification.Integer && value != Math.Truncate(value))
                throw new InvalidOperationException("Parameter outside its range or count domain — " + name);
            values[name] = value;
        }
        double V(string name) => values[name];
        var prisms = new List<Prism>();
        var rails = new List<Rail>();
        void Box(string role, double x, double y, double z, double length, double width, double height) =>
            prisms.Add(new(role, [new(x, y, z), new(x + length, y, z), new(x + length, y + width, z), new(x, y + width, z)], height));
        void Post(double x, double y, double baseZ) => rails.Add(new("post", [new(x, y, baseZ), new(x, y, baseZ + V("railHeight"))], V("railRadius")));
        void Barrier(Point[] path)
        {
            rails.Add(new("handrail", path.Select(p => p with { Z = p.Z + V("railHeight") }).ToArray(), V("railRadius")));
            for (int edge = 0; edge < path.Length - 1; edge++)
            {
                var start = path[edge]; var end = path[edge + 1];
                int divisions = (int)Math.Ceiling(Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2)) / V("postSpacing"));
                for (int index = 0; index < divisions; index++)
                {
                    double fraction = (double)index / divisions;
                    Post(start.X + (end.X - start.X) * fraction, start.Y + (end.Y - start.Y) * fraction, start.Z);
                }
            }
            Post(path[^1].X, path[^1].Y, path[^1].Z);
        }

        switch (definition.Kind)
        {
            case "straight-stair":
            case "switchback-stair":
            {
                bool returning = definition.Kind == "switchback-stair";
                double rise = V("rise") / (returning ? 2 : 1), run = V("run"), width = V("width");
                int count = (int)V("risers");
                double going = run / count, riser = rise / count, gap = returning ? V("gap") : 0;
                if (riser > .4 || going < .1) throw new InvalidOperationException("Stair exceeds the concept generator's rise/going bounds.");
                Box("lower-platform", -width, 0, -.2, width, width, .2);
                for (int i = 0; i < count; i++)
                {
                    Box("step", i * going, 0, 0, going, width, (i + 1) * riser);
                    if (returning) Box("step", run - (i + 1) * going, width + gap, rise, going, width, (i + 1) * riser);
                }
                Box(returning ? "middle-platform" : "upper-platform", run, 0, rise - .2, width, returning ? 2 * width + gap : width, .2);
                if (returning) Box("upper-platform", -width, width + gap, 2 * rise - .2, width, width, .2);
                foreach (int side in new[] { 0, 1 })
                {
                    double y = side * width;
                    var path = new List<Point> { new(-.3, y, V("railHeight")), new(0, y, riser + V("railHeight")),
                        new(run - going, y, rise + V("railHeight")), new(run, y, rise + V("railHeight")) };
                    if (returning)
                    {
                        double secondY = side == 0 ? 2 * width + gap : width + gap;
                        double turnX = run + width * (side == 0 ? .75 : .25);
                        path.AddRange([new(turnX, y, rise + V("railHeight")), new(turnX, secondY, rise + V("railHeight")),
                            new(run + .3, secondY, rise + V("railHeight")), new(run, secondY, rise + riser + V("railHeight")),
                            new(going, secondY, 2 * rise + V("railHeight")), new(-.3, secondY, 2 * rise + V("railHeight"))]);
                    }
                    else path.Add(new(run + .3, y, rise + V("railHeight")));
                    rails.Add(new("handrail", path.ToArray(), V("railRadius")));
                    int stride = Math.Max(1, (int)Math.Floor(V("postSpacing") / going));
                    var postIndices = Enumerable.Range(0, count).Where(i => i % stride == 0 || i == count - 1);
                    foreach (int i in postIndices)
                    {
                        Post(i * going, y, (i + 1) * riser);
                        if (returning) Post(run - i * going, side == 0 ? 2 * width + gap : width + gap, rise + (i + 1) * riser);
                    }
                    if (returning)
                    {
                        double secondY = side == 0 ? 2 * width + gap : width + gap;
                        double turnX = run + width * (side == 0 ? .75 : .25);
                        Post(turnX, y, rise); Post(turnX, secondY, rise);
                    }
                }
                break;
            }
            case "platform-railing":
                Box("platform", 0, 0, V("elevation") - .2, V("length"), V("width"), .2);
                Barrier([new(0, 0, V("elevation")), new(V("length"), 0, V("elevation")),
                    new(V("length"), V("width"), V("elevation")), new(0, V("width"), V("elevation"))]);
                break;
            case "queue-barriers":
                if (V("turnGap") >= V("length") / 2) throw new InvalidOperationException("Queue turning gap must be less than half its length.");
                for (int i = 0; i <= (int)V("lanes"); i++)
                {
                    double start = i > 0 && i < V("lanes") && i % 2 == 0 ? V("turnGap") : 0;
                    double end = i > 0 && i < V("lanes") && i % 2 == 1 ? V("length") - V("turnGap") : V("length");
                    Barrier([new(start, i * V("laneWidth"), 0), new(end, i * V("laneWidth"), 0)]);
                }
                break;
            case "curved-ice-wall":
            {
                double inner = V("radius") - V("thickness") / 2, outer = V("radius") + V("thickness") / 2;
                if (inner <= 0) throw new InvalidOperationException("Wall inner radius must be positive.");
                if (V("segments") * V("courses") > MaximumParts) throw new InvalidOperationException("Generated part budget exceeded.");
                double increment = V("angle") * Math.PI / 180 / V("segments"), angularJoint = V("joint") / inner;
                double courseHeight = V("height") / V("courses") - V("joint");
                if (angularJoint >= increment || courseHeight <= 0) throw new InvalidOperationException("Joint consumes a block.");
                for (int row = 0; row < (int)V("courses"); row++)
                for (int segment = 0; segment < (int)V("segments"); segment++)
                {
                    double a = segment * increment + angularJoint / 2, b = (segment + 1) * increment - angularJoint / 2;
                    var boundary = new List<Point>();
                    foreach (double radius in new[] { outer, inner })
                    for (int j = 0; j <= 6; j++)
                    {
                        double angle = radius == outer ? a + (b - a) * j / 6 : b - (b - a) * j / 6;
                        boundary.Add(new(radius * Math.Cos(angle), radius * Math.Sin(angle), row * V("height") / V("courses")));
                    }
                    prisms.Add(new("ice-block", boundary.ToArray(), courseHeight));
                }
                break;
            }
            case "spiral-ornament":
            {
                int segments = (int)V("turns") * 48;
                var path = Enumerable.Range(0, segments + 1).Select(i =>
                {
                    double fraction = (double)i / segments, angle = fraction * V("turns") * 2 * Math.PI;
                    return new Point(V("radius") * Math.Cos(angle), V("radius") * Math.Sin(angle), fraction * V("height"));
                }).ToArray();
                rails.Add(new("ornament", path, V("tubeRadius")));
                break;
            }
        }
        var result = new ExampleModel(prisms.ToArray(), rails.ToArray());
        if (result.PartCount > MaximumParts) throw new InvalidOperationException("Generated part budget exceeded.");
        return result;
    }
}
