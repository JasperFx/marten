using System.IO;
using JasperFx.Events;
using Marten.Storage;
using Marten.Storage.Metadata;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

/// <summary>
/// Non-partitioned tracking table whose only job is to assert global uniqueness
/// of stream identity (id / key). Created when
/// <see cref="EventGraph.EnableStrictStreamIdentityEnforcement"/> is true.
///
/// PostgreSQL forbids unique constraints on a partitioned table that don't
/// include the partition key. Under <see cref="EventGraph.UseArchivedStreamPartitioning"/>
/// the <c>mt_streams</c> primary key is automatically extended to
/// <c>(id, is_archived)</c>, which means a stream id can legally appear once in
/// the active partition and once in the archived partition without collision.
///
/// This sibling table holds just the identity tuple — never archived, never
/// partitioned — so its primary key truly enforces "this id has been used,
/// regardless of archive state." A duplicate INSERT here is the trigger for
/// <see cref="JasperFx.Events.ExistingStreamIdCollisionException"/>.
/// </summary>
internal class StreamIdentityEnforcementTable: Table
{
    public const string TableName = "mt_streams_identity";

    private readonly EventGraph _events;

    public StreamIdentityEnforcementTable(EventGraph events)
        : base(new PostgresqlObjectName(events.DatabaseSchemaName, TableName))
    {
        _events = events;

        // Per-tenant identity for conjoined tenancy (tenant_id leads in PK so
        // tenants don't accidentally fight each other for stream ids).
        if (events.TenancyStyle == TenancyStyle.Conjoined)
        {
            AddColumn<TenantIdColumn>().AsPrimaryKey();
        }

        var idType = events.StreamIdentity == StreamIdentity.AsGuid ? "uuid" : "varchar";
        AddColumn("id", idType).AsPrimaryKey();
    }

    /// <summary>
    /// #6051: creating the table also records every stream already in <c>mt_streams</c>, archived ones included.
    /// Without it, an event store that turns the flag on (every store upgraded to 10.0, where it is on by default)
    /// starts with an empty identity table beside a full <c>mt_streams</c>, and an archived stream's id can be
    /// started again. <c>mt_streams</c> is created or migrated ahead of this table, and under archive
    /// partitioning an id can be in both of its partitions, hence <c>on conflict do nothing</c>.
    /// </summary>
    public override void WriteCreateStatement(Migrator migrator, TextWriter writer)
    {
        base.WriteCreateStatement(migrator, writer);

        var columns = _events.TenancyStyle == TenancyStyle.Conjoined ? "tenant_id, id" : "id";
        writer.WriteLine();
        writer.WriteLine(
            $"insert into {Identifier} ({columns}) select {columns} from {_events.DatabaseSchemaName}.{StreamsTable.TableName} on conflict do nothing;");
    }
}
