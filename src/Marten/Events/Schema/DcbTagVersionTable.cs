using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

/// <summary>
/// Side-table that converts the DCB tag-boundary check from a racy predicate read
/// (SELECT EXISTS over mt_events) into a row-level write conflict. One row per
/// (tag_table, tag_value, tenant_id); the row's <c>version</c> is bumped on every
/// FetchForWritingByTags → SaveChangesAsync round trip, and the save's
/// <c>UPDATE … WHERE version = $captured</c> is the serialization point. See
/// <see cref="Marten.Events.Dcb.DcbTagVersionAssertion"/>. Fixes #4591.
/// </summary>
internal class DcbTagVersionTable: Table
{
    public DcbTagVersionTable(EventGraph events)
        : base(new PostgresqlObjectName(events.DatabaseSchemaName, "mt_dcb_tag_version"))
    {
        // text columns — the table is keyed across heterogeneous tag types, so
        // values are stringified to a canonical form (see TagValueStringifier).
        AddColumn("tag_table", "varchar").NotNull().AsPrimaryKey();
        AddColumn("tag_value", "text").NotNull().AsPrimaryKey();

        // Always present, regardless of tenancy style. In single-tenant stores
        // every row uses the default tenant id ('*DEFAULT*'); in conjoined
        // stores tenant_id is part of the PK so tenant A's bumps never collide
        // with tenant B's.
        // Weasel's DefaultValueByString quotes the value itself, so pass the
        // raw literal *DEFAULT* (not '*DEFAULT*') to get `DEFAULT '*DEFAULT*'`.
        AddColumn("tenant_id", "varchar").DefaultValueByString("*DEFAULT*").NotNull().AsPrimaryKey();

        // No index on version — keeps UPDATEs HOT-eligible (no index touch on
        // bump), which matters because this table sees an UPDATE on every
        // boundary save.
        AddColumn("version", "bigint").DefaultValueByExpression("0").NotNull();

        PrimaryKeyName = "pk_mt_dcb_tag_version";

        // No index on version — keeping that column unindexed means UPDATEs
        // remain HOT-eligible (only the PK index lookup, no index-entry rewrite
        // on bump). A lower heap fillfactor would help further by leaving free
        // space in each page for in-place updates, but Weasel.Postgresql's Table
        // doesn't currently expose `WITH (fillfactor = N)` on the heap table —
        // tracked as a follow-up.
    }
}
