var binaryDirectory = System.Environment.GetEnvironmentVariable("MIKI_OPENBIM_BINARY_DIRECTORY");
var outputDirectory = System.Environment.GetEnvironmentVariable("MIKI_OPENBIM_QA_OUTPUT");
var pidFile = System.Environment.GetEnvironmentVariable("MIKI_OPENBIM_QA_PID_FILE");
if (string.IsNullOrWhiteSpace(binaryDirectory) || string.IsNullOrWhiteSpace(outputDirectory) || string.IsNullOrWhiteSpace(pidFile))
    throw new System.InvalidOperationException("Set the three MIKI_OPENBIM QA environment variables before starting an owned Rhino process.");
if (System.Environment.ProcessId != int.Parse(System.IO.File.ReadAllText(pidFile)) || __rhino_doc__.Objects.Count != 0)
    throw new System.InvalidOperationException("The test requires an empty document in its explicitly owned QA process.");
__rhino_doc__.ModelAbsoluteTolerance=System.Math.Min(__rhino_doc__.ModelAbsoluteTolerance,
    .001*Rhino.RhinoMath.UnitScale(Rhino.UnitSystem.Meters,__rhino_doc__.ModelUnitSystem));
System.IO.Directory.CreateDirectory(outputDirectory);
var domain = System.Reflection.Assembly.LoadFrom(System.IO.Path.Combine(binaryDirectory, "Miki.OpenBim.dll"));
var native = System.Reflection.Assembly.LoadFrom(System.IO.Path.Combine(binaryDirectory, "Miki.OpenBim.Rhino.dll"));
var engine = domain.GetType("Miki.OpenBim.ProjectEngine");
var model = native.GetType("Miki.OpenBim.Rhino.RhinoModel");
int passed = 0;
void Check(bool value, string name)
{
    if (!value) throw new System.InvalidOperationException("FAIL " + name);
    passed++;
    System.Console.WriteLine("PASS " + name);
}
string Invoke(string method, params object[] arguments)
{
    try { return (string)model.GetMethod(method).Invoke(null, arguments); }
    catch (System.Reflection.TargetInvocationException error) { throw error.InnerException; }
}
string Snapshot(string name)
{
    var result = Invoke("InspectJson", __rhino_doc__);
    System.IO.File.WriteAllText(System.IO.Path.Combine(outputDirectory, name + "-native.json"), result);
    System.IO.File.WriteAllText(System.IO.Path.Combine(outputDirectory, name + "-project.json"), Invoke("ReadProjectJson", __rhino_doc__));
    var options = new Rhino.FileIO.FileWriteOptions { SuppressDialogBoxes = true };
    Check(__rhino_doc__.WriteFile(System.IO.Path.Combine(outputDirectory, name + ".3dm"), options), name + " native file saved");
    return result;
}
var source = (string)engine.GetMethod("Serialize").Invoke(null, new object[] {engine.GetMethod("CreateDemo").Invoke(null, null)});
var undo = __rhino_doc__.BeginUndoRecord("Miki OpenBIM create");
try { Invoke("ApplyJson", __rhino_doc__, source); }
finally { __rhino_doc__.EndUndoRecord(undo); }
var before = System.Text.Json.Nodes.JsonNode.Parse(Snapshot("before"));
Check(before["verified"].GetValue<bool>() && before["levels"].GetValue<int>() == 2, "two storeys verified from actual geometry");
var beforeObjects = __rhino_doc__.Objects.GetObjectList(Rhino.DocObjects.ObjectType.AnyObject)
    .Where(item => item.Attributes.GetUserString("miki.part") != null)
    .ToDictionary(item => item.Attributes.GetUserString("miki.part"), item => new {item.Id, Crc=item.Geometry.DataCRC(0)});
var beforeWindowObjects = __rhino_doc__.Objects.GetObjectList(Rhino.DocObjects.ObjectType.InstanceReference)
    .Cast<Rhino.DocObjects.InstanceObject>().ToArray();
Check(beforeWindowObjects.Length >= 5, "doors and windows are actual Block instances");
Check(beforeWindowObjects.Where(item => item.Attributes.GetUserString("miki.type") == "WIN-A")
    .Select(item => item.InstanceDefinition.Id).Distinct().Count() == 1, "same window type shares one definition");
undo = __rhino_doc__.BeginUndoRecord("Miki OpenBIM shared type");
try { Invoke("PatchJson", __rhino_doc__, "{\"types\":[{\"id\":\"WIN-A\",\"width\":1.5}]}"); }
finally { __rhino_doc__.EndUndoRecord(undo); }
var typeReport = System.Text.Json.Nodes.JsonNode.Parse(Invoke("InspectJson", __rhino_doc__));
double scale = Rhino.RhinoMath.UnitScale(Rhino.UnitSystem.Meters, __rhino_doc__.ModelUnitSystem);
var matching = __rhino_doc__.Objects.GetObjectList(Rhino.DocObjects.ObjectType.InstanceReference)
    .Cast<Rhino.DocObjects.InstanceObject>().Where(item => item.Attributes.GetUserString("miki.type") == "WIN-A").ToArray();
foreach (var instance in matching)
{
    var bounds = Rhino.Geometry.BoundingBox.Empty;
    foreach (var component in instance.InstanceDefinition.GetObjects()) bounds.Union(component.Geometry.GetBoundingBox(true));
    Check(System.Math.Abs((bounds.Max.X-bounds.Min.X)/scale-1.5)<1e-6, "shared definition width updated: " + instance.Attributes.GetUserString("miki.part"));
    Check(instance.Id == beforeObjects[instance.Attributes.GetUserString("miki.part")].Id, "instance GUID preserved");
}
var otherInstances = __rhino_doc__.Objects.GetObjectList(Rhino.DocObjects.ObjectType.InstanceReference)
    .Cast<Rhino.DocObjects.InstanceObject>().Where(item => item.Attributes.GetUserString("miki.type") != "WIN-A").ToArray();
Check(otherInstances.All(item => beforeObjects[item.Attributes.GetUserString("miki.part")].Crc == item.Geometry.DataCRC(0)), "different Block types unchanged");
Snapshot("shared-type");
var lower = __rhino_doc__.Objects.GetObjectList(Rhino.DocObjects.ObjectType.AnyObject)
    .Where(item => item.Attributes.GetUserString("miki.level") == "L1" && item.Attributes.GetUserString("miki.part") != null)
    .ToDictionary(item => item.Attributes.GetUserString("miki.part"), item => new {item.Id, Crc=item.Geometry.DataCRC(0)});
undo = __rhino_doc__.BeginUndoRecord("Miki OpenBIM second-storey height");
try { Invoke("PatchJson", __rhino_doc__, "{\"levels\":[{\"id\":\"L2\",\"height\":3.5}]}"); }
finally { __rhino_doc__.EndUndoRecord(undo); }
var after = System.Text.Json.Nodes.JsonNode.Parse(Snapshot("after"));
Check(lower.All(pair => {var item=__rhino_doc__.Objects.FindId(pair.Value.Id); return item != null && item.Geometry.DataCRC(0) == pair.Value.Crc;}), "L2 height edit preserves all L1 object IDs and geometry");
Check(after["revision"].GetValue<int>() == before["revision"].GetValue<int>() + 2, "two compact edits increment revision exactly twice");
var sourceAfter = System.Text.Json.Nodes.JsonNode.Parse(Invoke("ReadProjectJson", __rhino_doc__));
foreach (var wall in sourceAfter["walls"].AsArray().Where(item => item["level"].GetValue<string>() == "L2" && item["height"] == null))
    Check(System.Math.Abs(after["bounds"][wall["id"].GetValue<string>()]["max"][2].GetValue<double>() - 6.7) < 1e-6, "second-storey wall height follows level");
Check(!__rhino_doc__.Objects.GetObjectList(Rhino.DocObjects.ObjectType.AnyObject).Any(item => item.Geometry is Rhino.Geometry.Mesh || item.Geometry is Rhino.Geometry.SubD), "no mesh or SubD deliverables");
__rhino_doc__.Views.ActiveView.ActiveViewport.ZoomExtents();
__rhino_doc__.Views.Redraw();
System.IO.File.WriteAllText(System.IO.Path.Combine(outputDirectory, "native-test-summary.json"), "{\"verified\":true,\"checks\":" + passed + ",\"processId\":" + System.Environment.ProcessId + "}");
System.Console.WriteLine("NATIVE_OPENBIM_PASS " + passed);
