using Marten.Storage;
using Weasel.Core;
using Weasel.Core.Migrations;

namespace Marten.TimescaleDB.Internal;

/// <summary>
/// A custom Marten feature schema that emits the TimescaleDB hypertable / continuous-aggregate
/// DDL. Because it lives in the Marten.TimescaleDB assembly (not Marten core), Marten yields it
/// LAST in <c>StorageFeatures.AllActiveFeatures</c>, so every document, projection, and event
/// table it references has already been created by the time these objects run.
/// </summary>
internal sealed class TimescaleDBFeatureSchema: IFeatureSchema
{
    private readonly StoreOptions _options;
    private readonly IReadOnlyList<IHypertableTarget> _targets;

    public TimescaleDBFeatureSchema(StoreOptions options, IReadOnlyList<IHypertableTarget> targets)
    {
        _options = options;
        _targets = targets;
    }

    public IEnumerable<Type> DependentTypes()
    {
        // Ensure the eventstore feature (flat-table projections + event tables) is provisioned first
        // on the lazy, per-feature EnsureStorageExistsAsync path.
        yield return typeof(Marten.Events.EventGraph);
    }

    public ISchemaObject[] Objects => BuildObjects().ToArray();

    public Type StorageType => typeof(TimescaleDBFeatureSchema);

    public string Identifier => "timescaledb";

    public Migrator Migrator => _options.Advanced.Migrator;

    public void WritePermissions(Migrator rules, TextWriter writer)
    {
        // no-op
    }

    private IEnumerable<ISchemaObject> BuildObjects()
    {
        foreach (var target in _targets)
        {
            var table = target.ResolveTable(_options);
            if (table == null)
            {
                continue;
            }

            target.AssertValid(_options);

            yield return new HypertableSchemaObject(table, target.Options);

            foreach (var aggregate in target.Options.ContinuousAggregates)
            {
                yield return new ContinuousAggregateSchemaObject(table, target.Options.TimeColumn, aggregate);
            }
        }
    }
}
