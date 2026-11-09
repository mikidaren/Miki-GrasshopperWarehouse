using Grasshopper.Kernel;
using Miki.IceWorld.Rhino;
using Rhino;
using System.Drawing;
using System.Text.Json;

namespace Miki.IceWorld.Grasshopper;

public sealed class IceWorldAssemblyInfo : GH_AssemblyInfo
{
    public override string Name => "Miki IceWorld";
    public override string Description => "Parametric concept examples with bounded execution approval.";
    public override Guid Id => new("62ae8cce-c7f8-4d5b-953c-bc245baec614");
    public override string AuthorName => "Miki";
    public override string AuthorContact => "https://github.com/mikidaren/Miki-GrasshopperWarehouse";
    public override Bitmap? Icon => null;
}

public abstract class ExampleComponent : GH_Component
{
    public abstract string Kind { get; }
    protected ExampleComponent(string name) : base(name, name, "Metre-based concept geometry. Dimensions are test assumptions.", "Miki", "IceWorld") { }
    protected override Bitmap? Icon => null;
    protected override void RegisterInputParams(GH_InputParamManager manager)
    {
        foreach (var parameter in ExampleEngine.Parameters(Kind))
            manager.AddNumberParameter(parameter.Name, parameter.Name, $"{parameter.Minimum}–{parameter.Maximum}; {(parameter.Integer ? "integer count" : "lengths in metres, angle in degrees")}", GH_ParamAccess.item, parameter.Default);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager manager)
    {
        manager.AddBrepParameter("Geometry", "G", "Closed native Breps, preview only", GH_ParamAccess.list);
        manager.AddTextParameter("Roles", "R", "Per-object output classification", GH_ParamAccess.list);
        manager.AddTextParameter("Source", "S", "Validated JSON for Approved Bake", GH_ParamAccess.item);
        manager.AddTextParameter("Report", "I", "Validation and assumption record", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess access)
    {
        try
        {
            var parameters = ExampleEngine.Parameters(Kind);
            var values = new Dictionary<string, double>();
            for (int i = 0; i < parameters.Length; i++)
            {
                double value = parameters[i].Default;
                if (!access.GetData(i, ref value)) return;
                values.Add(parameters[i].Name, value);
            }
            var source = new ExampleDefinition("IW-" + Kind.ToUpperInvariant(), Kind, values);
            // Preview follows Rhino document units just as the enforced bake entry point does.
            var document = RhinoDoc.ActiveDoc;
            double scale = document is null ? 1 : RhinoMath.UnitScale(UnitSystem.Meters, document.ModelUnitSystem);
            var native = IceWorldRhino.Build(source, scale, .001 * scale);
            access.SetDataList(0, native.Geometry); access.SetDataList(1, native.Roles);
            access.SetData(2, JsonSerializer.Serialize(source, ExampleEngine.JsonOptions));
            access.SetData(3, $"{Kind} | {native.Geometry.Length} closed Breps | source units m | TEST DIMENSIONS | project dimensions/capacity undecided");
        }
        catch (InvalidOperationException error) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error.Message); }
    }
}

public sealed class StraightStairComponent() : ExampleComponent("Straight Stair")
{
    public override string Kind => "straight-stair";
    public override Guid ComponentGuid => new("881fcad1-e94b-4e87-852f-a6301d56d801");
}
public sealed class SwitchbackStairComponent() : ExampleComponent("Switchback Stair")
{
    public override string Kind => "switchback-stair";
    public override Guid ComponentGuid => new("881fcad1-e94b-4e87-852f-a6301d56d802");
}
public sealed class PlatformRailingComponent() : ExampleComponent("Platform Railing")
{
    public override string Kind => "platform-railing";
    public override Guid ComponentGuid => new("881fcad1-e94b-4e87-852f-a6301d56d803");
}
public sealed class QueueBarriersComponent() : ExampleComponent("Queue Barriers")
{
    public override string Kind => "queue-barriers";
    public override Guid ComponentGuid => new("881fcad1-e94b-4e87-852f-a6301d56d804");
}
public sealed class CurvedIceWallComponent() : ExampleComponent("Curved Ice Wall")
{
    public override string Kind => "curved-ice-wall";
    public override Guid ComponentGuid => new("881fcad1-e94b-4e87-852f-a6301d56d805");
}
public sealed class SpiralOrnamentComponent() : ExampleComponent("Spiral Ornament")
{
    public override string Kind => "spiral-ornament";
    public override Guid ComponentGuid => new("881fcad1-e94b-4e87-852f-a6301d56d806");
}

public sealed class ApprovedBakeComponent() : GH_Component("Approved Bake", "Bake", "Policy-enforced new-object bake on a button edge.", "Miki", "IceWorld")
{
    private bool previousTrigger;
    private bool observedTrigger;
    public override Guid ComponentGuid => new("881fcad1-e94b-4e87-852f-a6301d56d807");
    protected override Bitmap? Icon => null;
    protected override void RegisterInputParams(GH_InputParamManager manager)
    {
        manager.AddTextParameter("Source", "S", "Validated example JSON", GH_ParamAccess.item);
        manager.AddBooleanParameter("Bake", "B", "Connect a Button; rising edge only", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager manager) => manager.AddTextParameter("Decision", "D", "Approval reason or created-object count", GH_ParamAccess.item);
    protected override void SolveInstance(IGH_DataAccess access)
    {
        string json = ""; bool trigger = false;
        if (!access.GetData(0, ref json) || !access.GetData(1, ref trigger)) return;
        bool requested = observedTrigger && trigger && !previousTrigger;
        observedTrigger = true;
        previousTrigger = trigger;
        try
        {
            var source = ExampleEngine.Parse(json);
            // COFF-loaded assemblies have an empty Assembly.Location. Grasshopper retains the original library path.
            string libraryPath = global::Grasshopper.Instances.ComponentServer.FindAssemblyByObject(ComponentGuid)?.Location
                ?? GetType().Assembly.Location;
            string? libraryDirectory = Path.GetDirectoryName(libraryPath);
            if (string.IsNullOrWhiteSpace(libraryDirectory))
                throw new InvalidOperationException("Could not resolve the installed IceWorld library directory.");
            string policyPath = Path.Combine(libraryDirectory, "execution.json");
            var policy = ExecutionPolicy.Load(policyPath);
            if (!requested)
            {
                access.SetData(0, ExecutionGate.Evaluate("preview", source, policy, false).Reason);
                return;
            }
            var document = RhinoDoc.ActiveDoc ?? throw new InvalidOperationException("Open a Rhino document before baking.");
            var catalog = FindCatalog(libraryDirectory);
            StandardBaseline? standards = null;
            StandardDesignContext? context = null;
            if (catalog is not null && catalog.Find(source.Id) is { } entry)
            {
                standards = catalog.LoadStandardBaseline();
                context = StandardDesignContext.FromCatalog(entry);
            }
            var ids = IceWorldRhino.Bake(document, source, policy, standards, context);
            access.SetData(0, $"Automatically approved and created {ids.Length} owned objects." +
                (standards is null ? " No warehouse catalog entry supplied a standards stage." : ""));
        }
        catch (InvalidOperationException error) { access.SetData(0, "Requires host decision — " + error.Message); }
    }

    private static WarehouseCatalog? FindCatalog(string libraryDirectory)
    {
        DirectoryInfo? directory = new(libraryDirectory);
        for (int depth = 0; directory is not null && depth < 4; depth++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "examples", "index.json")))
                return WarehouseCatalog.Load(directory.FullName);
        }
        return null;
    }
}
