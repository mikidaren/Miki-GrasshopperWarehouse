using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using System.Text.Json;

namespace Miki.IceWorld.Grasshopper;

public sealed record NativeExample(Brep[] Geometry, string[] Roles);

public static class NativeExamples
{
    public static NativeExample Build(ExampleDefinition source, double scale = 1, double tolerance = .001)
    {
        if (!double.IsFinite(scale) || scale <= 0 || !double.IsFinite(tolerance) || tolerance <= 0)
            throw new InvalidOperationException("Positive scale and tolerance are required.");
        var model = ExampleEngine.Build(source);
        var geometry = new List<Brep>(); var roles = new List<string>();
        Point3d P(Point p) => new(p.X * scale, p.Y * scale, p.Z * scale);
        foreach (var prism in model.Prisms)
        {
            var points = prism.Boundary.Select(P).ToList(); points.Add(points[0]);
            var profile = new PolylineCurve(points);
            var extrusion = Extrusion.Create(profile, prism.Height * scale, true)
                ?? throw new InvalidOperationException("Could not extrude " + prism.Role);
            geometry.Add(extrusion.ToBrep()); roles.Add(prism.Role);
        }
        foreach (var rail in model.Rails)
        {
            var points = rail.Path.Select(P).ToArray();
            Curve path = points.Length == 2 ? new LineCurve(points[0], points[1]) : Curve.CreateInterpolatedCurve(points, 1);
            var pipes = Brep.CreatePipe(path, rail.Radius * scale, false, PipeCapMode.Flat, true, tolerance, .01);
            if (pipes is not { Length: 1 }) throw new InvalidOperationException("Could not create one continuous solid " + rail.Role);
            geometry.Add(pipes[0]); roles.Add(rail.Role);
        }
        if (geometry.Any(b => !b.IsValid || !b.IsSolid)) throw new InvalidOperationException("Native geometry validity/closure check failed.");
        return new(geometry.ToArray(), roles.ToArray());
    }

    // The caller supplies its UI-thread context. The gate is evaluated here even if the host already checked it.
    public static Guid[] Bake(RhinoDoc document, ExampleDefinition source, ExecutionPolicy policy)
    {
        string key = "MIKI-IW::" + source.Id;
        bool exists = document.Objects.GetObjectList(ObjectType.AnyObject).Any(o => o.Attributes.GetUserString("miki.iceworld.id") == source.Id);
        var decision = ExecutionGate.Evaluate("bake", source, policy, exists);
        if (!decision.Automatic) throw new InvalidOperationException(decision.Reason);
        if (document.ModelUnitSystem == UnitSystem.None || document.ModelUnitSystem == UnitSystem.CustomUnits)
            throw new InvalidOperationException("Set explicit Rhino document length units before baking.");
        double scale = RhinoMath.UnitScale(UnitSystem.Meters, document.ModelUnitSystem);
        if (document.ModelAbsoluteTolerance > .002 * scale)
            throw new InvalidOperationException("Document tolerance exceeds the example's 2 mm limit.");
        var native = Build(source, scale, document.ModelAbsoluteTolerance);
        if (native.Geometry.Length > policy.MaximumParts) throw new InvalidOperationException("Native part budget exceeded.");
        var ids = new List<Guid>(); var createdLayers = new List<int>();
        int group = -1;
        uint undo = document.BeginUndoRecord("IceWorld " + source.Id);
        try
        {
            group = document.Groups.Add(key);
            if (group < 0) throw new InvalidOperationException("Could not create the example group.");
            for (int i = 0; i < native.Geometry.Length; i++)
            {
                string layerName = "MIKI-IW-" + native.Roles[i];
                int layer = document.Layers.FindByFullPath(layerName, -1);
                if (layer < 0)
                {
                    layer = document.Layers.Add(layerName, System.Drawing.Color.SteelBlue);
                    if (layer < 0) throw new InvalidOperationException("Could not create the example layer.");
                    createdLayers.Add(layer);
                }
                var attributes = new ObjectAttributes { Name = source.Id + "/" + native.Roles[i] + "/" + i, LayerIndex = layer };
                attributes.SetUserString("miki.iceworld.id", source.Id);
                attributes.SetUserString("miki.iceworld.role", native.Roles[i]);
                attributes.SetUserString("miki.iceworld.source", JsonSerializer.Serialize(source, ExampleEngine.JsonOptions));
                attributes.AddToGroup(group);
                Guid id = document.Objects.AddBrep(native.Geometry[i], attributes);
                if (id == Guid.Empty) throw new InvalidOperationException("Could not add example geometry.");
                ids.Add(id);
            }
            return ids.ToArray();
        }
        catch
        {
            foreach (Guid id in ids) document.Objects.Delete(id, true);
            if (group >= 0) document.Groups.Delete(group);
            foreach (int layer in createdLayers.AsEnumerable().Reverse()) document.Layers.Delete(layer, true);
            throw;
        }
        finally { document.EndUndoRecord(undo); document.Views.Redraw(); }
    }
}
