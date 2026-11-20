using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

/// <summary>
/// jasperfx#1215 / #1222: the event-range marks of a stored-event rewrite and a projection backfill, one sequence
/// per string key and tenant. A run's ceiling is stored under its own key (<c>rewrite:{name}</c>) and its cursor
/// under <c>{key}:cursor</c>, which the rewrite writes in the same transaction as each page.
/// </summary>
/// <remarks>
/// <para>
/// Kept like <see cref="ProjectionFloorTable" />, and for the same reasons: a table of its own that <b>survives
/// rebuild teardown</b> and every reset of the event data, so a finished or half-finished run is never repeated
/// because a projection was rebuilt or the events were cleaned. Removing a tenant is the exception (see
/// <c>PerTenantPartitionedCleanup</c>), because a re-added tenant restarts its sequence.
/// </para>
/// <para>
/// <c>tenant_id</c> is part of the primary key, so a store-global mark is stored under the empty string rather than
/// null. Not gated on any option, for the reason given on <see cref="EventProgressionTable" /> (#5309).
/// </para>
/// </remarks>
internal class EventRangeMarkTable: Table
{
    public const string Name = "mt_event_range_marks";

    /// <summary>The <c>tenant_id</c> of a store-global mark.</summary>
    public const string GlobalTenant = "";

    public EventRangeMarkTable(EventGraph eventGraph): base(new PostgresqlObjectName(eventGraph.DatabaseSchemaName, Name))
    {
        foreach (var index in eventGraph.IgnoredIndexes)
            IgnoredIndexes.Add(index);

        AddColumn<string>("name").AsPrimaryKey();
        AddColumn<string>("tenant_id").AsPrimaryKey();
        AddColumn("mark", "bigint").NotNull();
        AddColumn("last_updated", "timestamp with time zone")
            .DefaultValueByExpression("(transaction_timestamp())");
    }
}
