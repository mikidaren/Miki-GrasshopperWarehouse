using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Miki.IceWorld;

public sealed record CatalogValidation(
    string Portable,
    string Native,
    string Grasshopper,
    string SourceSha256,
    string DefinitionSha256);

public sealed record CatalogExample(
    string Id,
    string Kind,
    string Title,
    string Category,
    string[] Tags,
    string Source,
    string Definition,
    string Units,
    string Runtime,
    string Stage,
    CatalogValidation Validation);

public sealed record WarehouseIndex(
    int SchemaVersion,
    string[] Categories,
    CatalogExample[] Examples);

public sealed class WarehouseCatalog
{
    private static readonly string[] ValidationStates = ["passed", "pending", "failed"];
    private static readonly string[] Stages = ["concept-test-assumptions", "standard-candidate"];
    private readonly Dictionary<string, CatalogExample> entries;

    private WarehouseCatalog(string root, WarehouseIndex index)
    {
        Root = root;
        Index = index;
        entries = index.Examples.ToDictionary(example => example.Id, StringComparer.Ordinal);
    }

    public string Root { get; }
    public WarehouseIndex Index { get; }
    public IReadOnlyList<CatalogExample> Examples => Index.Examples;
    public string IndexPath => Path.Combine(Root, "examples", "index.json");
    public string ExecutionPolicyPath => Path.Combine(Root, "policies", "execution.json");
    public string StandardBaselinePath => Path.Combine(Root, "policies", "iceworld-standard-baseline.json");

    public static WarehouseCatalog Load(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("Warehouse root is required.");
        string fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot)) throw new InvalidOperationException("Warehouse root does not exist.");
        string indexPath = Path.Combine(fullRoot, "examples", "index.json");
        string json = ReadText(indexPath, 1024 * 1024, "warehouse index");
        WarehouseIndex index;
        try
        {
            RejectDuplicateProperties(json);
            index = JsonSerializer.Deserialize<WarehouseIndex>(json, ExampleEngine.JsonOptions)
                ?? throw new InvalidOperationException("Warehouse index must be an object.");
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException("Invalid warehouse index JSON.", error);
        }
        ValidateIndex(fullRoot, index);
        return new WarehouseCatalog(fullRoot, index);
    }

    public CatalogExample? Find(string id) =>
        id is not null && entries.TryGetValue(id, out var example) ? example : null;

    public CatalogExample Require(string id) =>
        Find(id) ?? throw new InvalidOperationException("Unknown warehouse example " + id);

    public ExampleDefinition LoadDefinition(string id) => LoadDefinition(Require(id));

    public ExampleDefinition LoadDefinition(CatalogExample example)
    {
        if (example is null || !ReferenceEquals(entries.GetValueOrDefault(example.Id), example)
            && entries.GetValueOrDefault(example.Id) != example)
            throw new InvalidOperationException("Example does not belong to this warehouse catalog.");
        string path = ResolveArtifact(example.Source);
        var definition = ExampleEngine.Parse(ReadText(path, 65536, "example source"));
        if (definition.Id != example.Id || definition.Kind != example.Kind)
            throw new InvalidOperationException("Example source identity does not match the catalog.");
        return definition;
    }

    public StandardBaseline LoadStandardBaseline() => StandardBaseline.Load(StandardBaselinePath);

    public ExecutionPolicy LoadExecutionPolicy() => ExecutionPolicy.Load(ExecutionPolicyPath);

    public StandardAssessment Assess(string id, StandardDesignContext? context = null)
    {
        var example = Require(id);
        return LoadStandardBaseline().Assess(LoadDefinition(example),
            context ?? StandardDesignContext.FromCatalog(example));
    }

    public string ResolveArtifact(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidOperationException("Artifact path is required.");
        string path = Path.GetFullPath(Path.Combine(Root, relativePath));
        string relative = Path.GetRelativePath(Root, path);
        if (Path.IsPathRooted(relative) || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Artifact path escapes the warehouse root.");
        return path;
    }

    private static void ValidateIndex(string root, WarehouseIndex index)
    {
        if (index.SchemaVersion != 1 || index.Categories is null || index.Examples is null)
            throw new InvalidOperationException("Unsupported warehouse index schema.");
        if (index.Categories.Length == 0 || index.Categories.Distinct(StringComparer.Ordinal).Count() != index.Categories.Length
            || index.Categories.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Warehouse categories are missing or duplicated.");
        var categories = index.Categories.ToHashSet(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var sources = new HashSet<string>(StringComparer.Ordinal);
        var definitions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var example in index.Examples)
        {
            if (example is null || example.Id is null
                || !Regex.IsMatch(example.Id, "^[A-Z0-9][A-Z0-9._-]{0,127}$"))
                throw new InvalidOperationException("Warehouse example has an invalid stable ID.");
            string id = example.Id;
            if (!ids.Add(id)) throw new InvalidOperationException("Duplicate warehouse example ID " + id);
            if (string.IsNullOrWhiteSpace(example.Kind) || string.IsNullOrWhiteSpace(example.Title)
                || !categories.Contains(example.Category) || example.Tags is null || example.Tags.Length == 0)
                throw new InvalidOperationException("Warehouse example metadata is incomplete for " + example.Id);
            if (example.Units != "m" || example.Runtime != "rhino9-net8-grasshopper"
                || !Stages.Contains(example.Stage, StringComparer.Ordinal))
                throw new InvalidOperationException("Unsupported warehouse example units, runtime or stage for " + example.Id);
            ValidateArtifact(example.Source, ".json", sources, "source", example.Id);
            ValidateArtifact(example.Definition, ".ghx", definitions, "definition", example.Id);
            if (example.Validation is null)
                throw new InvalidOperationException("Warehouse example validation record is missing for " + example.Id);
            string[] states = [example.Validation.Portable, example.Validation.Native, example.Validation.Grasshopper];
            if (states.Any(state => !ValidationStates.Contains(state, StringComparer.Ordinal)))
                throw new InvalidOperationException("Warehouse example has an unknown validation state for " + example.Id);
            VerifyHash(root, example.Source, example.Validation.SourceSha256, example.Id);
            VerifyHash(root, example.Definition, example.Validation.DefinitionSha256, example.Id);
            string sourcePath = FullArtifactPath(root, example.Source, "source", example.Id);
            var definition = ExampleEngine.Parse(ReadText(sourcePath, 65536, "example source"));
            if (definition.Id != example.Id || definition.Kind != example.Kind)
                throw new InvalidOperationException("Warehouse source identity does not match its index for " + example.Id);
        }
        string actualSources = Path.Combine(root, "examples", "iceworld");
        if (Directory.Exists(actualSources))
        {
            var actual = Directory.GetFiles(actualSources, "*.json").Select(path =>
                Path.GetRelativePath(root, path).Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
            if (!actual.SetEquals(sources))
                throw new InvalidOperationException("Warehouse index does not match the example source files.");
        }
    }

    private static void ValidateArtifact(string relative, string suffix, HashSet<string> seen, string label, string id)
    {
        string pattern = "^examples/iceworld/[a-z0-9]+(?:-[a-z0-9]+)*" + Regex.Escape(suffix) + "$";
        if (string.IsNullOrWhiteSpace(relative) || !Regex.IsMatch(relative, pattern)
            || !seen.Add(relative))
            throw new InvalidOperationException("Warehouse example has an invalid or duplicate " + label + " path for " + id);
    }

    private static string FullArtifactPath(string root, string relative, string label, string id)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative));
        string resolvedRelative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(resolvedRelative) || resolvedRelative == ".."
            || resolvedRelative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !File.Exists(path))
            throw new InvalidOperationException("Warehouse " + label + " artifact is missing or outside the root for " + id);
        return path;
    }

    private static void VerifyHash(string root, string relative, string expected, string id)
    {
        string path = FullArtifactPath(root, relative, "hashed", id);
        string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        if (expected is null || !string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException("Warehouse artifact hash is stale for " + id);
    }

    private static string ReadText(string path, int maximumCharacters, string label)
    {
        try
        {
            string text = File.ReadAllText(path);
            if (text.Length > maximumCharacters)
                throw new InvalidOperationException("Warehouse " + label + " exceeds its size limit.");
            return text;
        }
        catch (IOException error)
        {
            throw new InvalidOperationException("Warehouse " + label + " could not be read.", error);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new InvalidOperationException("Warehouse " + label + " could not be read.", error);
        }
    }

    internal static void RejectDuplicateProperties(string json)
    {
        using var document = JsonDocument.Parse(json);
        Visit(document.RootElement);

        static void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new InvalidOperationException("Duplicate JSON property " + property.Name);
                    Visit(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray()) Visit(item);
            }
        }
    }
}
