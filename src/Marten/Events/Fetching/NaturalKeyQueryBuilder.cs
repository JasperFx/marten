using JasperFx.Events;
using Marten.Events.Schema;
using Marten.Storage;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Fetching;

/// <summary>
/// Writes the SQL that resolves a natural key to its stream, against the lookup table of the aggregate
/// that owns the key, in the two shapes a single batched fetch needs: a JOIN that reads the stream's
/// version and identity, and a subselect that stands in for the stream id in the document and event reads.
/// Every statement resolves the key for itself, so a fetch by natural key is one round trip and can sit in
/// a batch query like any other.
/// </summary>
internal class NaturalKeyQueryBuilder
{
    private readonly string _tableName;
    private readonly string _streamsTableName;
    private readonly string _streamIdColumn;
    private readonly bool _filterByTenant;

    /// <param name="events"></param>
    /// <param name="lookup">
    /// The owned key whose lookup table is read: the aggregate's own key, or the source projection's key
    /// for a mapped one.
    /// </param>
    public NaturalKeyQueryBuilder(EventGraph events, NaturalKeyDefinition lookup)
    {
        _tableName = NaturalKeyTable.TableNameFor(events, lookup).QualifiedName;
        _streamsTableName = $"{events.DatabaseSchemaName}.{StreamsTable.TableName}";
        _streamIdColumn = events.StreamIdentity == StreamIdentity.AsGuid ? "stream_id" : "stream_key";
        _filterByTenant = events.TenancyStyle == TenancyStyle.Conjoined &&
                          !events.GlobalAggregates.Contains(lookup.AggregateType);
    }

    /// <summary>
    /// <c>select s.version, nk.stream_id</c> for the key's live stream. No row means no stream has the key.
    /// </summary>
    public void WriteVersionAndStreamId(ICommandBuilder builder, object keyValue, bool forUpdate)
    {
        builder.Append("select s.version, nk.");
        builder.Append(_streamIdColumn);
        writeFromAndWhere(builder, keyValue);

        if (forUpdate)
        {
            builder.Append(" for update of s");
        }
    }

    /// <summary>
    /// <c>select nk.stream_id</c> for the key's live stream, read after the aggregate to check that the key still
    /// names the stream <see cref="WriteVersionAndStreamId"/> found.
    /// </summary>
    public void WriteStreamId(ICommandBuilder builder, object keyValue)
    {
        builder.Append("select nk.");
        builder.Append(_streamIdColumn);
        writeFromAndWhere(builder, keyValue);
    }

    /// <summary>
    /// <c>(select nk.stream_id ...)</c>, to compare a stream id column against.
    /// </summary>
    public void WriteStreamIdSubselect(ICommandBuilder builder, object keyValue)
    {
        builder.Append("(select nk.");
        builder.Append(_streamIdColumn);
        writeFromAndWhere(builder, keyValue);
        builder.Append(')');
    }

    /// <summary>
    /// <c>d.stream_id = (select ...)</c>, for an <see cref="EventStatement"/>.
    /// </summary>
    public ISqlFragment StreamIdFilter(object keyValue) => new StreamIdMatchesKey(this, keyValue);

    private void writeFromAndWhere(ICommandBuilder builder, object keyValue)
    {
        builder.Append(" from ");
        builder.Append(_tableName);
        builder.Append(" nk inner join ");
        builder.Append(_streamsTableName);
        builder.Append(" s on s.id = nk.");
        builder.Append(_streamIdColumn);

        // tenant_id is part of the primary key of both tables, so the join has to match it too or the same
        // key in two tenants would join across them.
        if (_filterByTenant)
        {
            builder.Append(" and s.tenant_id = nk.tenant_id");
        }

        builder.Append(" where nk.natural_key_value = ");
        builder.AppendParameter(keyValue);
        builder.Append(" and nk.is_archived = false");

        // #5344: archival is a property of the *stream*, and nothing on the write side copies it onto the
        // lookup row -- ArchiveStream() and the Archived event both go through mt_archive_stream, which has
        // never touched mt_natural_key_X. nk.is_archived above is kept because it is genuinely maintained
        // under UseArchivedStreamPartitioning, where mt_archive_stream / mt_unarchive_stream copy the lookup
        // row between partitions with its stream (marten#5476).
        builder.Append(" and s.is_archived = false");

        if (_filterByTenant)
        {
            builder.Append(" and nk.tenant_id = ");
            builder.AppendParameter(builder.TenantId);
        }
    }

    private sealed class StreamIdMatchesKey: ISqlFragment
    {
        private readonly NaturalKeyQueryBuilder _parent;
        private readonly object _keyValue;

        public StreamIdMatchesKey(NaturalKeyQueryBuilder parent, object keyValue)
        {
            _parent = parent;
            _keyValue = keyValue;
        }

        public void Apply(ICommandBuilder builder)
        {
            builder.Append("d.stream_id = ");
            _parent.WriteStreamIdSubselect(builder, _keyValue);
        }
    }
}
