using SchemaAlign.Appliers.CSharp;
using SchemaAlign.Appliers.SqlServer;
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
        Register(TargetType.CSharp, new CSharpEntityApplier());
        Register(TargetType.SqlServerScript, new SqlServerMigrationApplier());
        Register(TargetType.SqlServerDatabase, new SqlServerMigrationApplier());
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
}
