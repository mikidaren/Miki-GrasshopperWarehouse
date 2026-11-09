using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;
using Miki.IceWorld;
using Miki.IceWorld.Grasshopper;
using Rhino;
using Rhino.Runtime.InProcess;

public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: IceWorld.NativeQA <Rhino System directory> <warehouse root> <QA output directory>");
            return 2;
        }
        string system = Path.GetFullPath(args[0]);
        string grasshopper = Path.GetFullPath(Path.Combine(system, "../Plug-ins/Grasshopper"));
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            foreach (string directory in new[] { AppContext.BaseDirectory, system, grasshopper })
            foreach (string extension in new[] { ".dll", ".gha" })
            {
                string file = Path.Combine(directory, name.Name + extension);
                if (File.Exists(file)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
            }
            return null;
        };
        RhinoInside.Resolver.Initialize(system);
        try { return Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])); }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string repository, string output)
    {
        using var core = new RhinoCore(["/nosplash", "/notemplate"], WindowStyle.NoWindow);
        return RunInRhino(repository, output);
    }

    public static int RunInRhino(string repository, string output)
    {
        Directory.CreateDirectory(output);
        Console.WriteLine("Rhino initialized " + RhinoApp.Version);
        Console.WriteLine("Loading component server");
        var server = Instances.ComponentServer;
        Console.WriteLine("Component server available");
        // Instances.ComponentServer already registers Grasshopper's built-in assembly.
        // Registering Grasshopper.dll again creates duplicate component-ID dialogs.
        RegisterWarehouse(server);
        int registered = server.ObjectProxies.Count();
        RegisterWarehouse(server);
        Check(server.ObjectProxies.Count() == registered, "warehouse registration is idempotent");
        Check(server.IsObjectCached(new Guid("57da07bd-ecab-415d-9d86-af36d7073abc")), "built-in slider remains registered");
        CheckInitialBakeSuppressed();
        var coffAssembly = Assembly.Load(File.ReadAllBytes(typeof(ApprovedBakeComponent).Assembly.Location));
        Check(string.IsNullOrEmpty(coffAssembly.Location), "COFF regression uses a byte-loaded assembly");
        var coffComponent = (GH_Component)Activator.CreateInstance(coffAssembly.GetType(typeof(ApprovedBakeComponent).FullName!)!)!;
        CheckInitialBakeSuppressed(coffComponent);
        Console.WriteLine("Grasshopper initialized without re-registering built-in components");
        Func<ExampleComponent>[] components = [() => new StraightStairComponent(), () => new SwitchbackStairComponent(),
            () => new PlatformRailingComponent(), () => new QueueBarriersComponent(), () => new CurvedIceWallComponent(), () => new SpiralOrnamentComponent()];
        var results = new List<object>();
        foreach (var factory in components)
        {
            var component = factory();
            var source = new ExampleDefinition("IW-" + component.Kind.ToUpperInvariant(), component.Kind,
                ExampleEngine.Parameters(component.Kind).ToDictionary(p => p.Name, p => p.Default));
            string basePath = Path.Combine(repository, "examples/iceworld", component.Kind);
            File.WriteAllText(basePath + ".json", JsonSerializer.Serialize(source, ExampleEngine.JsonOptions).Replace("\r\n", "\n") + "\n");
            using var nativeDoc = RhinoDoc.CreateHeadless(null);
            nativeDoc.ModelUnitSystem = UnitSystem.Meters; nativeDoc.ModelAbsoluteTolerance = .001;
            var generated = NativeExamples.Build(source);
            PreviewRenderer.Render(Path.Combine(output, component.Kind + ".png"), component.Name, generated);
            Check(generated.Geometry.Length == ExampleEngine.Build(source).PartCount, "native part count " + component.Kind);
            var ids = NativeExamples.Bake(nativeDoc, source, ExecutionPolicy.Default);
            Check(ids.Length == generated.Geometry.Length, "baked closed Breps " + component.Kind);
            try { NativeExamples.Bake(nativeDoc, source, ExecutionPolicy.Default); throw new Exception("Duplicate bake accepted"); }
            catch (InvalidOperationException) { }
            Check(nativeDoc.Objects.Count == ids.Length, "repeated bake preserved existing objects");
            string modelPath = Path.Combine(output, component.Kind + ".3dm");
            Check(nativeDoc.WriteFile(modelPath, new Rhino.FileIO.FileWriteOptions { SuppressDialogBoxes = true }), "saved native model");
            using (var readback = Rhino.FileIO.File3dm.Read(modelPath))
                Check(readback.Objects.Count == ids.Length && readback.Objects.All(o => o.Geometry is Rhino.Geometry.Brep b && b.IsValid && b.IsSolid), "reopened closed native solids");
            using (var millimetres = RhinoDoc.CreateHeadless(null))
            {
                millimetres.ModelUnitSystem = UnitSystem.Millimeters; millimetres.ModelAbsoluteTolerance = .1;
                var scaledIds = NativeExamples.Bake(millimetres, source, ExecutionPolicy.Default);
                double expected = generated.Geometry[0].GetBoundingBox(true).Diagonal.Length * 1000;
                double actual = millimetres.Objects.FindId(scaledIds[0]).Geometry.GetBoundingBox(true).Diagonal.Length;
                Check(Math.Abs(actual - expected) < .01, "millimetre bake scale");
            }
            using var document = new GH_Document();
            document.Enabled = true;
            component.CreateAttributes(); component.Attributes.Pivot = new PointF(470, 180);
            document.AddObject(component, false);
            var sliders = new List<Guid>();
            var parameters = ExampleEngine.Parameters(component.Kind);
            for (int i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var slider = new GH_NumberSlider(); slider.CreateAttributes();
                slider.NickName = parameter.Name;
                slider.Slider.Type = parameter.Integer ? Grasshopper.GUI.Base.GH_SliderAccuracy.Integer : Grasshopper.GUI.Base.GH_SliderAccuracy.Float;
                slider.Slider.DecimalPlaces = parameter.Integer ? 0 : 3;
                slider.Slider.Minimum = (decimal)parameter.Minimum; slider.Slider.Maximum = (decimal)parameter.Maximum;
                slider.SetSliderValue((decimal)parameter.Default); slider.Attributes.Pivot = new PointF(100, 100 + 45 * i);
                document.AddObject(slider, false); component.Params.Input[i].AddSource(slider); sliders.Add(slider.InstanceGuid);
            }
            var bake = new ApprovedBakeComponent(); bake.CreateAttributes(); bake.Attributes.Pivot = new PointF(770, 350);
            document.AddObject(bake, false); bake.Params.Input[0].AddSource(component.Params.Output[2]);
            var button = new GH_ButtonObject(); button.CreateAttributes(); button.Attributes.Pivot = new PointF(590, 420);
            document.AddObject(button, false); bake.Params.Input[1].AddSource(button);
            var report = new GH_Panel(); report.CreateAttributes(); report.Attributes.Pivot = new PointF(770, 100);
            report.Attributes.Bounds = new RectangleF(770, 100, 420, 180);
            document.AddObject(report, false); report.AddSource(component.Params.Output[3]);
            Group(document, "01 INPUTS — metres / test assumptions", sliders, Color.FromArgb(80, 90, 160, 210));
            Group(document, "02 GENERATION — preview geometry", [component.InstanceGuid], Color.FromArgb(80, 100, 190, 150));
            Group(document, "03 OUTPUT / APPROVAL — button to bake", [bake.InstanceGuid, button.InstanceGuid, report.InstanceGuid], Color.FromArgb(80, 220, 175, 90));
            var archive = new GH_Archive(); Check(archive.AppendObject(document, "Definition"), "archived GH document");
            Check(archive.WriteToFile(basePath + ".ghx", true, false), "saved GHX");
            File.WriteAllText(basePath + ".ghx", File.ReadAllText(basePath + ".ghx").Replace("\r\n", "\n"));
            var io = new GH_DocumentIO(); Check(io.Open(basePath + ".ghx"), "reopened GHX");
            using var reopened = io.Document;
            Check(reopened.Objects.OfType<ExampleComponent>().Count() == 1, "restored custom component");
            Check(reopened.Objects.OfType<GH_NumberSlider>().Count() == parameters.Length, "restored connected sliders");
            GH_Document.EnableSolutions = true; reopened.Enabled = true; reopened.NewSolution(false);
            var restored = reopened.Objects.OfType<ExampleComponent>().Single();
            string[] errors = restored.RuntimeMessages(GH_RuntimeMessageLevel.Error).ToArray();
            Check(errors.Length == 0, "GH solution errors " + string.Join(" | ", errors));
            Check(restored.Params.Output[0].VolatileDataCount == generated.Geometry.Length, "GH solution native geometry count");
            var changedSlider = reopened.Objects.OfType<GH_NumberSlider>().First();
            changedSlider.SetSliderValue((decimal)(parameters[0].Default * 1.1));
            changedSlider.ExpireSolution(false); reopened.NewSolution(false);
            Check(restored.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0, "changed slider solved");
            string changedSource = restored.Params.Output[2].VolatileData.AllData(true).Single().ToString()!;
            Check(Math.Abs(ExampleEngine.Parse(changedSource).Parameters[parameters[0].Name] - parameters[0].Default * 1.1) < 1e-6, "changed slider updates source");
            results.Add(new { source.Id, source.Kind, closedBreps = ids.Length, ghReopened = true, ghSolved = true, sliderChanged = true,
                millimetres = true, duplicateBakeRejected = true,
                sourceSha256 = Hash(basePath + ".json"), definitionSha256 = Hash(basePath + ".ghx") });
            Console.WriteLine("PASS " + component.Kind + " " + ids.Length + " closed Breps; GHX reopened and solved");
        }
        var codeFiles = Directory.GetFiles(Path.Combine(repository, "src/Miki.IceWorld"), "*.cs")
            .Concat(Directory.GetFiles(Path.Combine(repository, "src/Miki.IceWorld.Grasshopper"), "*.cs")).Order(StringComparer.Ordinal);
        string codeHashes = string.Join("\n", codeFiles.Select(file => Path.GetRelativePath(repository, file).Replace('\\', '/') + " " + Hash(file)));
        string kernelSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(codeHashes))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(output, "native-validation.json"), JsonSerializer.Serialize(new { validatedOn = "2026-11-10", rhino = RhinoApp.Version.ToString(),
            registrationIdempotent = true, initialBakeSuppressed = true, coffPolicyResolved = true, kernelSha256, results }, ExampleEngine.JsonOptions));
        return 0;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static void RegisterWarehouse(GH_ComponentServer server)
    {
        Guid[] ids = [new StraightStairComponent().ComponentGuid, new SwitchbackStairComponent().ComponentGuid,
            new PlatformRailingComponent().ComponentGuid, new QueueBarriersComponent().ComponentGuid,
            new CurvedIceWallComponent().ComponentGuid, new SpiralOrnamentComponent().ComponentGuid,
            new ApprovedBakeComponent().ComponentGuid];
        if (ids.All(server.IsObjectCached)) return;
        if (ids.Any(server.IsObjectCached)) throw new InvalidOperationException("Partial warehouse registration detected. Use a fresh QA process.");
        var parse = typeof(GH_ComponentServer).GetMethod("ParseGHA", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, [typeof(Assembly), typeof(string), typeof(GH_LoadingMechanism)], null)
            ?? throw new InvalidOperationException("Grasshopper targeted registration is unavailable.");
        var assembly = typeof(StraightStairComponent).Assembly;
        parse.Invoke(server, [assembly, assembly.Location, GH_LoadingMechanism.Direct]);
        Check(ids.All(server.IsObjectCached), "all warehouse components registered");
    }

    private static void CheckInitialBakeSuppressed(GH_Component? component = null)
    {
        using var document = new GH_Document { Enabled = true };
        var bake = component ?? new ApprovedBakeComponent(); bake.CreateAttributes(); document.AddObject(bake, false);
        ((Param_String)bake.Params.Input[0]).SetPersistentData("{\"id\":\"IW-LOAD-TEST\",\"kind\":\"straight-stair\",\"parameters\":{}}");
        var trigger = (Param_Boolean)bake.Params.Input[1]; trigger.PersistentData.Clear(); trigger.SetPersistentData(true);
        GH_Document.EnableSolutions = true; document.NewSolution(false);
        string decision = bake.Params.Output[0].VolatileData.AllData(true).Single().ToString()!;
        Check(decision == "Validated read-only operation.", "initial true bake input remains preview-only");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("FAIL " + message);
    }

    private static void Group(GH_Document document, string name, IEnumerable<Guid> ids, Color color)
    {
        var group = new GH_Group { NickName = name, Colour = color };
        group.CreateAttributes(); foreach (Guid id in ids) group.AddObject(id); document.AddObject(group, false);
    }
}
