using JasperFx.Events;
using Marten.Storage;
using Marten.Storage.Metadata;
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
/// <see cref="Marten.Exceptions.ExistingStreamIdCollisionException"/>.
/// </summary>
internal class StreamIdentityEnforcementTable: Table
{
    public const string TableName = "mt_streams_identity";

    public StreamIdentityEnforcementTable(EventGraph events)
        : base(new PostgresqlObjectName(events.DatabaseSchemaName, TableName))
    {
        // Per-tenant identity for conjoined tenancy (tenant_id leads in PK so
        // tenants don't accidentally fight each other for stream ids).
        if (events.TenancyStyle == TenancyStyle.Conjoined)
        {
            AddColumn<TenantIdColumn>().AsPrimaryKey();
        }

        var idType = events.StreamIdentity == StreamIdentity.AsGuid ? "uuid" : "varchar";
        AddColumn("id", idType).AsPrimaryKey();
    }
}
