using SchemaAlign.Cli.Services;

namespace SchemaAlign.Appliers;

/// <summary>
/// Registry mapping target types to their corresponding schema appliers.
/// </summary>
public class ApplierRegistry
{
    private readonly Dictionary<TargetType, ISchemaApplier> _appliers = new();

    public ApplierRegistry()
    {
        // Dynamically register CSharpEntityApplier if present in assembly
        TryRegister("SchemaAlign.Appliers.CSharp.CSharpEntityApplier, SchemaAlign", TargetType.CSharp);
        // Dynamically register SqlServerMigrationApplier if present in assembly
        TryRegister("SchemaAlign.Appliers.SqlServer.SqlServerMigrationApplier, SchemaAlign", TargetType.SqlServerScript);
        TryRegister("SchemaAlign.Appliers.SqlServer.SqlServerMigrationApplier, SchemaAlign", TargetType.SqlServerDatabase);
    }

    /// <summary>
    /// Registers a schema applier instance for a specific target type.
    /// </summary>
    /// <param name="targetType">The target type to associate with the applier.</param>
    /// <param name="applier">The schema applier instance.</param>
    public void Register(TargetType targetType, ISchemaApplier applier)
    {
        _appliers[targetType] = applier;
    }

    /// <summary>
    /// Resolves the schema applier registered for the specified target type.
    /// </summary>
    /// <param name="targetType">The target type to resolve.</param>
    /// <returns>The registered schema applier.</returns>
    /// <exception cref="NotSupportedException">Thrown when no applier is registered for the specified target type.</exception>
    public ISchemaApplier Resolve(TargetType targetType)
    {
        if (_appliers.TryGetValue(targetType, out var applier))
        {
            return applier;
        }

        throw new NotSupportedException($"No schema applier is registered or available for target type '{targetType}'.");
    }

    private void TryRegister(string typeName, TargetType targetType)
    {
        try
        {
            var type = Type.GetType(typeName);
            if (type != null && typeof(ISchemaApplier).IsAssignableFrom(type))
            {
                var instance = (ISchemaApplier)Activator.CreateInstance(type)!;
                _appliers[targetType] = instance;
            }
        }
        catch
        {
            // Ignore reflection activation errors if dependencies not yet merged
        }
    }
}
