using System.Text.Json;

namespace Miki.IceWorld;

public sealed record StandardDocument(
    string Id,
    string Title,
    string Status,
    string AppliesTo,
    string Source,
    string VerifiedOn);

public sealed record StandardDimension(
    double? MinimumM = null,
    double? PreferredM = null,
    double? MaximumM = null,
    double? Minimum = null,
    double? Maximum = null,
    string Clause = "",
    string Applicability = "");

public sealed record DesignProfile(string Description, Dictionary<string, StandardDimension> Dimensions);
public sealed record ManualReview(string Id, string Requirement, string Reason);

public sealed record StandardBaseline(
    int SchemaVersion,
    string Jurisdiction,
    string Scope,
    string ReviewStatus,
    StandardDocument[] Standards,
    Dictionary<string, DesignProfile> Profiles,
    ManualReview[] ManualReview)
{
    public static StandardBaseline Load(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            if (json.Length > 1024 * 1024)
                throw new InvalidOperationException("Standard baseline exceeds 1 MiB.");
            WarehouseCatalog.RejectDuplicateProperties(json);
            var baseline = JsonSerializer.Deserialize<StandardBaseline>(json, ExampleEngine.JsonOptions)
                ?? throw new InvalidOperationException("Standard baseline must be an object.");
            baseline.Validate();
            return baseline;
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException("Invalid standard baseline JSON.", error);
        }
        catch (IOException error)
        {
            throw new InvalidOperationException("Standard baseline could not be read.", error);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new InvalidOperationException("Standard baseline could not be read.", error);
        }
    }

    public void Validate()
    {
        if (SchemaVersion != 1 || Jurisdiction != "CN-HLJ" || string.IsNullOrWhiteSpace(Scope)
            || ReviewStatus is not ("candidate" or "approved") || Standards is null
            || Profiles is null || ManualReview is null)
            throw new InvalidOperationException("Unsupported or incomplete standard baseline.");
        if (Standards.Length == 0 || Standards.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id)
            || item.Status != "current" || string.IsNullOrWhiteSpace(item.Title)
            || string.IsNullOrWhiteSpace(item.AppliesTo)))
            throw new InvalidOperationException("Standard baseline documents are incomplete.");
        if (!Profiles.TryGetValue("temporary-exhibition", out var profile) || profile is null
            || profile.Dimensions is null || profile.Dimensions.Count == 0)
            throw new InvalidOperationException("The temporary-exhibition design profile is required.");
        if (ManualReview.Length == 0 || ManualReview.Any(item => item is null
            || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Requirement)))
            throw new InvalidOperationException("Manual review obligations are incomplete.");
    }

    public StandardAssessment Assess(ExampleDefinition source, StandardDesignContext context)
    {
        Validate();
        if (source is null) throw new InvalidOperationException("Example source is required.");
        if (context is null) throw new InvalidOperationException("Standard evaluation context is required.");
        context.Validate();
        if (!Profiles.TryGetValue("temporary-exhibition", out var profile))
            throw new InvalidOperationException("The temporary-exhibition design profile is missing.");
        _ = ExampleEngine.Build(source);
        var values = ExampleEngine.Parameters(source.Kind).ToDictionary(
            parameter => parameter.Name, parameter => parameter.Default, StringComparer.Ordinal);
        foreach (var (name, value) in source.Parameters) values[name] = value;
        var findings = new List<StandardFinding>();
        bool blocking = context.Stage == StandardDesignContext.StandardCandidate;

        void Check(string name, bool passed, string message)
        {
            var dimension = profile.Dimensions[name];
            findings.Add(new(name, passed, blocking && !passed, message, dimension.Clause));
        }

        if (source.Kind is "straight-stair" or "switchback-stair")
        {
            bool returning = source.Kind == "switchback-stair";
            double flightRise = values["rise"] / (returning ? 2 : 1);
            double going = values["run"] / values["risers"];
            double riser = flightRise / values["risers"];
            double width = values["width"];
            double guard = values["railHeight"];
            int steps = (int)values["risers"];
            Check("treadGoing", going >= profile.Dimensions["treadGoing"].MinimumM,
                $"Stair going {going:F3} m is below {profile.Dimensions["treadGoing"].MinimumM:F2} m.");
            Check("riserHeight", riser <= profile.Dimensions["riserHeight"].MaximumM,
                $"Stair riser {riser:F3} m exceeds {profile.Dimensions["riserHeight"].MaximumM:F2} m.");
            Check("stairWidth", width >= profile.Dimensions["stairWidth"].MinimumM,
                $"Stair width {width:F3} m is below {profile.Dimensions["stairWidth"].MinimumM:F2} m.");
            Check("guardHeight", guard >= profile.Dimensions["guardHeight"].MinimumM,
                $"Rail height {guard:F3} m is below the candidate guard baseline {profile.Dimensions["guardHeight"].MinimumM:F2} m.");
            var flight = profile.Dimensions["stairFlightSteps"];
            Check("stairFlightSteps", steps >= flight.Minimum && steps <= flight.Maximum,
                $"Stair flight has {steps} steps; the candidate profile permits {flight.Minimum}-{flight.Maximum}.");
            if (context.OutdoorEgressStair)
            {
                double pitch = Math.Atan2(flightRise, values["run"]) * 180 / Math.PI;
                Check("outdoorEgressStairPitchDegrees",
                    pitch <= profile.Dimensions["outdoorEgressStairPitchDegrees"].Maximum,
                    $"Outdoor egress stair pitch {pitch:F1}° exceeds {profile.Dimensions["outdoorEgressStairPitchDegrees"].Maximum:F0}°.");
            }
        }
        else if (source.Kind == "platform-railing")
        {
            double guard = values["railHeight"];
            Check("guardHeight", guard >= profile.Dimensions["guardHeight"].MinimumM,
                $"Platform rail height {guard:F3} m is below the candidate guard baseline {profile.Dimensions["guardHeight"].MinimumM:F2} m.");
        }

        return new StandardAssessment(context.Stage, findings.Any(item => item.Blocking),
            findings.ToArray(), ManualReview);
    }
}

public sealed record StandardDesignContext(
    string Stage = StandardDesignContext.Concept,
    bool OutdoorEgressStair = false)
{
    public const string Concept = "concept-test-assumptions";
    public const string StandardCandidate = "standard-candidate";

    public static StandardDesignContext FromCatalog(CatalogExample example) =>
        new(example.Stage);

    public void Validate()
    {
        if (Stage is not (Concept or StandardCandidate))
            throw new InvalidOperationException("Unknown standard design stage " + Stage);
    }
}

public sealed record StandardFinding(
    string Check,
    bool Passed,
    bool Blocking,
    string Message,
    string Clause);

public sealed record StandardAssessment(
    string Stage,
    bool Blocking,
    StandardFinding[] Findings,
    ManualReview[] ManualReview)
{
    public bool Passed => !Blocking;
}
