var directory=System.Environment.GetEnvironmentVariable("MIKI_OPENBIM_BINARY_DIRECTORY");
var outputDirectory=System.Environment.GetEnvironmentVariable("MIKI_OPENBIM_QA_OUTPUT");
var domain=System.Reflection.Assembly.LoadFrom(System.IO.Path.Combine(directory,"Miki.OpenBim.dll"));
var native=System.Reflection.Assembly.LoadFrom(System.IO.Path.Combine(directory,"Miki.OpenBim.Rhino.dll"));
var engine=domain.GetType("Miki.OpenBim.ProjectEngine");
var model=native.GetType("Miki.OpenBim.Rhino.RhinoModel");
var source=(string)engine.GetMethod("Serialize").Invoke(null,new object[]{engine.GetMethod("CreateDemo").Invoke(null,null)});
var failures=new System.Collections.Generic.List<string>();
int passed=0;
void Check(bool value,string name)
{
    if(value){passed++;System.Console.WriteLine("PASS "+name);}
    else {failures.Add(name);System.Console.WriteLine("FAIL "+name);}
}
string Invoke(string method,params object[] arguments)
{
    try{return (string)model.GetMethod(method).Invoke(null,arguments);}
    catch(System.Reflection.TargetInvocationException error){throw error.InnerException;}
}
using(var doc=Rhino.RhinoDoc.CreateHeadless(null))
{
    doc.ModelUnitSystem=Rhino.UnitSystem.Meters;doc.ModelAbsoluteTolerance=.001;
    Invoke("ApplyJson",doc,source);
    var wall=doc.Objects.GetObjectList(Rhino.DocObjects.ObjectType.Brep).First(item=>item.Attributes.GetUserString("miki.kind")=="wall");
    string id=wall.Attributes.GetUserString("miki.part");
    int layer=doc.Layers.Add("Architect layer",System.Drawing.Color.Crimson);
    int group=doc.Groups.Add("Architect group");
    var attributes=wall.Attributes.Duplicate();
    attributes.LayerIndex=layer;attributes.Name="Architect custom name";
    attributes.ObjectColor=System.Drawing.Color.OrangeRed;attributes.ColorSource=Rhino.DocObjects.ObjectColorSource.ColorFromObject;
    attributes.AddToGroup(group);attributes.SetUserString("architect.note","keep this");
    doc.Objects.ModifyAttributes(wall.Id,attributes,true);
    Invoke("PatchJson",doc,"{\"walls\":[{\"id\":\""+id+"\",\"thickness\":0.30}]}");
    var updated=doc.Objects.FindId(wall.Id);
    Check(updated.Attributes.LayerIndex==layer && updated.Attributes.Name=="Architect custom name","patch preserves manual layer and name");
    Check(updated.Attributes.GetUserString("architect.note")=="keep this","patch preserves custom user strings");
    Check(updated.Attributes.GetGroupList()?.Contains(group)==true,"patch preserves group membership");
    Check(updated.Attributes.ObjectColor.ToArgb()==System.Drawing.Color.OrangeRed.ToArgb() && updated.Attributes.ColorSource==Rhino.DocObjects.ObjectColorSource.ColorFromObject,"patch preserves manual colour");
    var unitChanged=doc.ModelUnitSystem;
    doc.ModelUnitSystem=Rhino.UnitSystem.Millimeters;
    bool refused=false;try{Invoke("InspectJson",doc);}catch(System.InvalidOperationException){refused=true;}
    Check(refused,"manual document unit changes fail closed");
    doc.ModelUnitSystem=unitChanged;
    var geometry=updated.Geometry.Duplicate();geometry.Transform(Rhino.Geometry.Transform.Translation(1,0,0));
    doc.Objects.Replace(updated.Id,geometry,true);
    refused=false;try{Invoke("PatchJson",doc,"{\"levels\":[{\"id\":\"L2\",\"height\":3.4}]}");}catch(System.InvalidOperationException){refused=true;}
    Check(refused,"manual geometry edits cannot be silently overwritten");
}
using(var doc=Rhino.RhinoDoc.CreateHeadless(null))
{
    doc.ModelUnitSystem=Rhino.UnitSystem.Meters;doc.ModelAbsoluteTolerance=.001;
    var plan=System.Text.Json.Nodes.JsonNode.Parse(source).AsObject();
    foreach(var name in new[]{"walls","types","openings","stairs"})plan[name]=new System.Text.Json.Nodes.JsonArray();
    bool accepted=false;
    try{var report=System.Text.Json.Nodes.JsonNode.Parse(Invoke("ApplyJson",doc,plan.ToJsonString()));accepted=report["verified"].GetValue<bool>();}
    catch(System.InvalidOperationException error){System.Console.WriteLine("plan-first diagnostic: "+error.Message);}
    Check(accepted,"plan-first project works without walls or window types");
}
using(var doc=Rhino.RhinoDoc.CreateHeadless(null))
{
    doc.ModelUnitSystem=Rhino.UnitSystem.Meters;doc.ModelAbsoluteTolerance=.001;
    Invoke("ApplyJson",doc,source);
    bool accepted=true;
    try{Invoke("PatchJson",doc,"{\"types\":[{\"id\":\"win-a\",\"kind\":\"window\",\"form\":\"rectangular\",\"width\":0.8,\"height\":1.0,\"depth\":0.12}]}");}
    catch(System.InvalidOperationException error){accepted=false;System.Console.WriteLine("case diagnostic: "+error.Message);}
    Check(accepted,"case-distinct semantic types have distinct native definitions");
}
using(var doc=Rhino.RhinoDoc.CreateHeadless(null))
{
    doc.ModelUnitSystem=Rhino.UnitSystem.Millimeters;doc.ModelAbsoluteTolerance=.01;
    var report=System.Text.Json.Nodes.JsonNode.Parse(Invoke("ApplyJson",doc,source));
    Check(report["verified"].GetValue<bool>() && report["units"].GetValue<string>()=="Millimeters",
        "metre source builds exact solids in a millimetre document");
}
using(var doc=Rhino.RhinoDoc.CreateHeadless(null))
{
    doc.ModelUnitSystem=Rhino.UnitSystem.Meters;doc.ModelAbsoluteTolerance=.001;
    Invoke("ApplyJson",doc,source);
    var wall=doc.Objects.GetObjectList(Rhino.DocObjects.ObjectType.Brep).First(item=>item.Attributes.GetUserString("miki.kind")=="wall");
    string id=wall.Attributes.GetUserString("miki.part");
    var parent=new Rhino.DocObjects.Layer{Name="Architect"};int parentIndex=doc.Layers.Add(parent);
    var nested=new Rhino.DocObjects.Layer{Name=doc.Layers[wall.Attributes.LayerIndex].Name,ParentLayerId=doc.Layers[parentIndex].Id};
    int nestedIndex=doc.Layers.Add(nested);
    var attributes=wall.Attributes.Duplicate();attributes.LayerIndex=nestedIndex;doc.Objects.ModifyAttributes(wall.Id,attributes,true);
    Invoke("PatchJson",doc,"{\"walls\":[{\"id\":\""+id+"\",\"thickness\":0.30}]}");
    Check(doc.Objects.FindId(wall.Id).Attributes.LayerIndex==nestedIndex,"custom nested layer with a managed leaf name remains assigned");
}
var summary=new System.Text.Json.Nodes.JsonObject{["passed"]=passed,["failed"]=failures.Count,["failures"]=new System.Text.Json.Nodes.JsonArray(failures.Select(name=>(System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(name)).ToArray())};
System.IO.File.WriteAllText(System.IO.Path.Combine(outputDirectory,"native-regressions.json"),summary.ToJsonString());
System.Console.WriteLine("NATIVE_REGRESSIONS "+passed+" passed, "+failures.Count+" failed");
if(failures.Count>0)throw new System.InvalidOperationException("Native regression failures remain.");
