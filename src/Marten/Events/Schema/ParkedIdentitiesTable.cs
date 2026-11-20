using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

/// <summary>
/// marten#6003: the identities of async projections that the daemon parked after they failed, one row per
/// (projection, version, shard key, tenant, identity). A row is written in the projection batch that moves the
/// shard's progression past the failing event, and deleted in the batch that replays or discards the identity.
/// </summary>
/// <remarks>
/// <para>
/// Always created, like <see cref="EventProgressionTable" /> (#5309): a shape that depends on options would make two
/// stores over one schema drop each other's columns.
/// </para>
/// <para>
/// Rebuild teardown deletes a projection's rows, a rewind deletes the rows at or above the new floor, removing a
/// tenant deletes its rows, and ejecting a retired projection version deletes that version's rows. Unlike the
/// projection floors and the event-range marks, nothing here is meant to outlive a rebuild.
/// </para>
/// </remarks>
internal class ParkedIdentitiesTable: Table
{
    public const string Name = "mt_parked_identities";

    public ParkedIdentitiesTable(EventGraph eventGraph): base(new PostgresqlObjectName(eventGraph.DatabaseSchemaName, Name))
    {
        foreach (var index in eventGraph.IgnoredIndexes)
            IgnoredIndexes.Add(index);

        AddColumn("projection_name", "varchar").NotNull().AsPrimaryKey();
        AddColumn("projection_version", "integer").NotNull().AsPrimaryKey();
        AddColumn("shard_key", "varchar").NotNull().AsPrimaryKey();
        AddColumn("tenant_id", "varchar").NotNull().AsPrimaryKey();

        // The canonical text of the unwrapped identity (ParkedIdentityText), and what it unwraps to
        AddColumn("identity", "varchar").NotNull().AsPrimaryKey();
        AddColumn("identity_type", "varchar(20)").NotNull();

        AddColumn("first_failing_seq", "bigint").NotNull();
        AddColumn("resume_after", "bigint").NotNull();

        // The enum NAME, never the ordinal, as mt_event_progression.failure_category does
        AddColumn("failure_category", "varchar(50)").NotNull();
        AddColumn("exception_type", "varchar").AllowNulls();
        AddColumn("exception_message", "text").AllowNulls();
        AddColumn("event_type", "varchar").AllowNulls();
        AddColumn("event_id", "uuid").AllowNulls();
        AddColumn("failing_stream", "varchar").AllowNulls();

        AddColumn("parked_at", "timestamp with time zone").NotNull()
            .DefaultValueByExpression("(transaction_timestamp())");

        AddColumn("replay_attempts", "integer").NotNull().DefaultValueByExpression("0");
        AddColumn("last_attempt_at", "timestamp with time zone").AllowNulls();
        AddColumn("last_attempt_error", "text").AllowNulls();
        AddColumn("requested_action", "varchar(20)").AllowNulls();
        AddColumn("requested_at", "timestamp with time zone").AllowNulls();

        PrimaryKeyName = "pk_mt_parked_identities";
    }
}
