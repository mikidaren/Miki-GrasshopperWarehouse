using System.Text.Json;

namespace Miki.IceWorld;

public sealed record ExecutionPolicy(int Version, int MaximumParts, bool AutoBake)
{
    public static ExecutionPolicy Default => new(1, 5000, true);
    public static ExecutionPolicy Load(string path)
    {
        try
        {
            var policy = JsonSerializer.Deserialize<ExecutionPolicy>(File.ReadAllText(path), ExampleEngine.JsonOptions)
                ?? throw new InvalidOperationException("Missing execution policy.");
            policy.Validate();
            return policy;
        }
        catch (JsonException error) { throw new InvalidOperationException("Invalid execution policy.", error); }
        catch (IOException error) { throw new InvalidOperationException("Execution policy could not be read.", error); }
        catch (UnauthorizedAccessException error) { throw new InvalidOperationException("Execution policy could not be read.", error); }
    }

    public void Validate()
    {
        if (Version != 1 || MaximumParts < 1 || MaximumParts > ExampleEngine.MaximumParts)
            throw new InvalidOperationException("Unsupported policy version or part budget.");
    }
}
public sealed record ApprovalDecision(bool Automatic, string Reason);
public static class ExecutionGate
{
    public static ApprovalDecision Evaluate(string operation, ExampleDefinition source, ExecutionPolicy policy, bool existingObjects)
    {
        try
        {
            policy.Validate();
            var model = ExampleEngine.Build(source);
            if (model.PartCount > policy.MaximumParts) return new(false, "Generated geometry exceeds the local policy budget.");
            return operation switch
            {
                "inspect" or "preview" => new(true, "Validated read-only operation."),
                "bake" when existingObjects => new(false, "Existing example objects require an explicit host-managed update."),
                "bake" when policy.AutoBake => new(true, "Validated new owned objects within the local policy budget."),
                _ => new(false, "This operation requires an explicit host decision.")
            };
        }
        catch (InvalidOperationException error) { return new(false, error.Message); }
    }
}
