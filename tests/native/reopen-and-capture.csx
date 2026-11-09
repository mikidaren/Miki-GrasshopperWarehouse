var binaryDirectory = System.Environment.GetEnvironmentVariable("MIKI_OPENBIM_BINARY_DIRECTORY");
var outputDirectory = System.Environment.GetEnvironmentVariable("MIKI_OPENBIM_QA_OUTPUT");
var pidFile = System.Environment.GetEnvironmentVariable("MIKI_OPENBIM_QA_PID_FILE");
if (System.Environment.ProcessId != int.Parse(System.IO.File.ReadAllText(pidFile))) throw new System.InvalidOperationException("Wrong owned QA process.");
var native = System.Reflection.Assembly.LoadFrom(System.IO.Path.Combine(binaryDirectory, "Miki.OpenBim.Rhino.dll"));
var model = native.GetType("Miki.OpenBim.Rhino.RhinoModel");
foreach (var name in new[]{"before","shared-type","after"})
{
    using var reopened = Rhino.RhinoDoc.OpenHeadless(System.IO.Path.Combine(outputDirectory, name + ".3dm"));
    if (reopened == null) throw new System.InvalidOperationException("Cannot reopen native file: " + name);
    var report = (string)model.GetMethod("InspectJson").Invoke(null,new object[]{reopened});
    System.IO.File.WriteAllText(System.IO.Path.Combine(outputDirectory,name + "-reopened-native.json"),report);
    System.Console.WriteLine("PASS reopened actual .3dm: " + name);
}
var view = __rhino_doc__.Views.ActiveView;
var shaded = Rhino.Display.DisplayModeDescription.GetDisplayMode(Rhino.Display.DisplayModeDescription.ShadedId);
if (shaded != null) view.ActiveViewport.DisplayMode = shaded;
view.ActiveViewport.SetCameraDirection(new Rhino.Geometry.Vector3d(-1,-1,-.7), true);
view.ActiveViewport.ZoomExtents();
var capture = new Rhino.Display.ViewCapture { Width=1440, Height=1000, DrawGrid=false, DrawAxes=false, DrawGridAxes=false, ScaleScreenItems=false };
using (var bitmap=capture.CaptureToBitmap(view)) bitmap.Save(System.IO.Path.Combine(outputDirectory,"two-storey-native.png"),System.Drawing.Imaging.ImageFormat.Png);
var visibility = __rhino_doc__.Layers.Where(layer => !layer.IsDeleted).ToDictionary(layer=>layer.Id,layer=>layer.IsVisible);
try
{
    view.ActiveViewport.SetProjection(Rhino.Display.DefinedViewportProjection.Top,"Top",true);
    foreach (var level in new[]{"L1","L2"})
    {
        foreach (var layer in __rhino_doc__.Layers.Where(item=>!item.IsDeleted && item.Name.StartsWith("Miki-")))
        {
            layer.IsVisible=layer.Name.EndsWith("Plan-"+level) || layer.Name.EndsWith("Plan-site");
            layer.CommitChanges();
        }
        view.ActiveViewport.ZoomExtents();
        __rhino_doc__.Views.Redraw();
        using var bitmap=capture.CaptureToBitmap(view);
        bitmap.Save(System.IO.Path.Combine(outputDirectory,"plan-"+level+"-native.png"),System.Drawing.Imaging.ImageFormat.Png);
    }
}
finally
{
    foreach (var layer in __rhino_doc__.Layers.Where(item=>!item.IsDeleted))
        if (visibility.TryGetValue(layer.Id,out bool visible)) { layer.IsVisible=visible; layer.CommitChanges(); }
    view.ActiveViewport.SetProjection(Rhino.Display.DefinedViewportProjection.Perspective,"Perspective",true);
    view.ActiveViewport.SetCameraDirection(new Rhino.Geometry.Vector3d(-1,-1,-.7), true);
    view.ActiveViewport.ZoomExtents();
    __rhino_doc__.Views.Redraw();
}
System.Console.WriteLine("PASS captured actual Rhino geometry and floor plans");
