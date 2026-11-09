using System.Text.Json.Nodes;
using Miki.IceWorld;
using Miki.IceWorld.Rhino;
using Rhino;

namespace Miki.OpenBim.Rhino;

public static partial class RhinoModel
{
    public static JsonObject BuildIceWorld(RhinoDoc document, string warehouseRoot, string exampleId,
        StandardDesignContext? context = null)
    {
        if (document is null) throw new InvalidOperationException("Rhino document is required.");
        var catalog = WarehouseCatalog.Load(warehouseRoot);
        double scale = document.ModelUnitSystem is UnitSystem.None or UnitSystem.Unset or UnitSystem.CustomUnits
            ? throw new InvalidOperationException("Set explicit Rhino document length units before building.")
            : RhinoMath.UnitScale(UnitSystem.Meters, document.ModelUnitSystem);
        var preview = IceWorldRhino.BuildFromCatalog(catalog, exampleId, context, scale, document.ModelAbsoluteTolerance);
        var findings = new JsonArray(preview.Standards.Findings.Select(item => (JsonNode)new JsonObject
        {
            ["check"] = item.Check,
            ["passed"] = item.Passed,
            ["blocking"] = item.Blocking,
            ["message"] = item.Message,
            ["clause"] = item.Clause
        }).ToArray());
        return new JsonObject
        {
            ["example"] = exampleId,
            ["kind"] = preview.Source.Kind,
            ["stage"] = preview.Standards.Stage,
            ["geometry"] = preview.Native.Geometry.Length,
            ["roles"] = new JsonArray(preview.Native.Roles.Select(role => (JsonNode)JsonValue.Create(role)!).ToArray()),
            ["standardBlocking"] = preview.Standards.Blocking,
            ["standardFindings"] = findings,
            ["report"] = preview.Report
        };
    }

    public static JsonObject BakeIceWorld(RhinoDoc document, string warehouseRoot, string exampleId,
        StandardDesignContext? context = null)
    {
        if (document is null) throw new InvalidOperationException("Rhino document is required.");
        var catalog = WarehouseCatalog.Load(warehouseRoot);
        var ids = IceWorldRhino.BakeFromCatalog(document, catalog, exampleId, context);
        return new JsonObject
        {
            ["example"] = exampleId,
            ["created"] = ids.Length,
            ["objectIds"] = new JsonArray(ids.Select(id => (JsonNode)JsonValue.Create(id.ToString())!).ToArray()),
            ["group"] = "MIKI-IW::" + exampleId,
            ["report"] = "Automatically approved and created owned IceWorld objects."
        };
    }
}
