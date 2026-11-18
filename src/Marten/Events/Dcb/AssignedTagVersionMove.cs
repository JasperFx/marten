#nullable enable
using Marten.Events.Schema;
using Marten.Storage;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Dcb;

/// <summary>
/// #6078: the statement <c>AssignTagWhere</c> runs after it tags the events, giving the assigned tag a new
/// <c>mt_dcb_tag_version</c> value from the sequence and creating the row where there is none.
/// </summary>
/// <remarks>
/// <para>
/// Tagging existing events changes what a boundary over that tag reads without appending anything, and a boundary
/// only compares the rows it captured. Without this, a boundary that read the tag before the assignment saved
/// straight through it, as if the newly tagged events weren't there. Creating a missing row is what catches a
/// boundary that captured 0: it read no row, and finds one with a sequence value at save.
/// </para>
/// <para>
/// The keys are the tag type's own, and under a <see cref="DcbConflictScope"/> that records them, the
/// <c>{suffix}:{event type}</c> key of each event type the assignment matched. Nothing is written when it matched
/// no event of the session's tenant. The rows are taken in ordinal order, the order appends take them in.
/// </para>
/// </remarks>
internal static class AssignedTagVersionMove
{
    /// <param name="builder">The command, already started.</param>
    /// <param name="events">The event store's configuration.</param>
    /// <param name="tagTableSuffix">The tag type's table suffix, which is the row's tag_table.</param>
    /// <param name="tagValue">The value in <see cref="JasperFx.Events.Tags.TagValueStringifier"/>'s form.</param>
    /// <param name="tenantId">The session's tenant.</param>
    /// <param name="where">The assignment's filter over <c>mt_events as d</c>.</param>
    public static void Append(ICommandBuilder builder, EventGraph events, string tagTableSuffix, string tagValue,
        string tenantId, ISqlFragment where)
    {
        var schema = events.DatabaseSchemaName;
        var recordEventTypes = events.DcbConflictScope != DcbConflictScope.TagValue;

        builder.Append($"insert into {schema}.{DcbTagVersionTable.TableName} (tag_table, tag_value, tenant_id, version) select y.k, ");
        builder.AppendParameter(tagValue).NpgsqlDbType = NpgsqlDbType.Text;
        builder.Append(", ");
        builder.AppendParameter(tenantId).NpgsqlDbType = NpgsqlDbType.Varchar;
        builder.Append($", {DcbTagVersionTable.NextVersionSql(events)} from (select distinct z.k from {schema}.mt_events as d cross join lateral (values (");
        builder.AppendParameter(tagTableSuffix).NpgsqlDbType = NpgsqlDbType.Varchar;
        builder.Append(")");

        if (recordEventTypes)
        {
            builder.Append(", (");
            builder.AppendParameter(tagTableSuffix + ":").NpgsqlDbType = NpgsqlDbType.Varchar;
            builder.Append(" || d.type)");
        }

        builder.Append(") as z(k) where (");
        where.Apply(builder);
        builder.Append(")");

        if (events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" and d.tenant_id = ");
            builder.AppendParameter(tenantId).NpgsqlDbType = NpgsqlDbType.Varchar;
        }

        builder.Append(") y order by y.k collate \"C\"");
        builder.Append(" on conflict (tag_table, tag_value, tenant_id) do update set version = excluded.version");
    }
}
