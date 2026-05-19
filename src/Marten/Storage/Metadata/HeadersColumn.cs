using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Data.Common;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Services;
using NpgsqlTypes;

namespace Marten.Storage.Metadata;

internal class HeadersColumn: MetadataColumn<Dictionary<string, object>>, IEventTableColumn
{
    public static readonly string ColumnName = "headers";

    public HeadersColumn(): base(ColumnName, x => x.Headers)
    {
        Type = "jsonb";
        Enabled = false;
    }

    internal override async Task ApplyAsync(IMartenSession martenSession, DocumentMetadata metadata, int index,
        DbDataReader reader, CancellationToken token)
    {
        if (await reader.IsDBNullAsync(index, token).ConfigureAwait(false))
        {
            return;
        }

        metadata.Headers = await martenSession.Serializer
            .FromJsonAsync<Dictionary<string, object>>(reader, index, token).ConfigureAwait(false);
    }

    internal override void Apply(IMartenSession martenSession, DocumentMetadata metadata, int index,
        DbDataReader reader)
    {
        if (reader.IsDBNull(index))
        {
            return;
        }

        var json = reader.GetStream(index);
        metadata.Headers = martenSession.Serializer.FromJson<Dictionary<string, object>>(reader, index);
    }

    internal override void RegisterForLinqSearching(DocumentMapping mapping)
    {
        // Nothing
    }

    internal override UpsertArgument ToArgument()
    {
        return new HeadersArgument();
    }

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }

    // Closed-shape read path (#4416 part 2). Headers deserialization needs
    // the session's ISerializer because Npgsql can't map jsonb directly to
    // Dictionary<string, object>. The closed-shape adapter threads
    // ISerializer in via the IEventTableColumn serializer-aware
    // ReadValueSync / Async overloads — the parameterless versions still
    // throw (they're never called by the adapter for this column).

    void IEventTableColumn.ReadValueSync(DbDataReader reader, int index, IEvent @event, ISerializer serializer)
    {
        if (reader.IsDBNull(index)) return;
        @event.Headers = serializer.FromJson<Dictionary<string, object>>(reader, index);
    }

    async Task IEventTableColumn.ReadValueAsync(DbDataReader reader, int index, IEvent @event, ISerializer serializer, CancellationToken cancellation)
    {
        if (await reader.IsDBNullAsync(index, cancellation).ConfigureAwait(false)) return;
        @event.Headers = await serializer
            .FromJsonAsync<Dictionary<string, object>>(reader, index, cancellation)
            .ConfigureAwait(false);
    }
}

internal class HeadersArgument: UpsertArgument
{
    public HeadersArgument()
    {
        Arg = "headerDict";
        Column = HeadersColumn.ColumnName;
        PostgresType = "jsonb";
        DbType = NpgsqlDbType.Jsonb;
    }
}
