using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

/// <summary>
/// jasperfx#809 (#5740): the persisted start floor of each projection, per tenant — the sequence below which
/// the projection has no meaningful history. Written by <c>AsyncOptions.SubscribeFromFirstRun()</c> on a first
/// start and by an operator through <c>IEventDatabase.SetProjectionFloorAsync</c>; read on every continuous
/// start and every rebuild.
/// </summary>
/// <remarks>
/// <para>
/// A table of its own rather than a column on <see cref="EventProgressionTable" />, because a floor has to
/// <b>survive rebuild teardown</b>, which deletes the progression row. Keeping it off that table also keeps
/// "no progression row" meaning "a fresh shard". For the same reason nothing that resets event data deletes
/// from it: a floor outlives <c>DeleteAllEventDataAsync</c> as it outlives a rebuild. Removing a tenant is the
/// exception (see <c>PerTenantPartitionedCleanup</c>), because a re-added tenant restarts its sequence and
/// would otherwise inherit a floor from its previous incarnation.
/// </para>
/// <para>
/// Keyed by the projection's <c>ShardName.Name</c> <em>without</em> its version, so a version bump inherits
/// the floor, plus the tenant id under per-tenant partitioning. <c>tenant_id</c> is part of the primary key, so
/// a store-global floor is stored under the empty string rather than null.
/// </para>
/// <para>
/// Not gated on any option, for the reason given on <see cref="EventProgressionTable" /> (#5309): an operator
/// can set a floor on any store, and a table whose existence depends on configuration makes two stores over
/// one schema drop each other's objects.
/// </para>
/// </remarks>
internal class ProjectionFloorTable: Table
{
    public const string Name = "mt_event_projection_floors";

    /// <summary>The <c>tenant_id</c> of a store-global floor.</summary>
    public const string GlobalTenant = "";

    public ProjectionFloorTable(EventGraph eventGraph): base(new PostgresqlObjectName(eventGraph.DatabaseSchemaName, Name))
    {
        foreach (var index in eventGraph.IgnoredIndexes)
            IgnoredIndexes.Add(index);

        AddColumn<string>("name").AsPrimaryKey();
        AddColumn<string>("tenant_id").AsPrimaryKey();
        AddColumn("floor", "bigint").NotNull();
        AddColumn("last_updated", "timestamp with time zone")
            .DefaultValueByExpression("(transaction_timestamp())");
    }
}
