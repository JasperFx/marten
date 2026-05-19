using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Sessions;
using Marten.Schema;
using Marten.Schema.Arguments;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Storage.Metadata;

internal class CorrelationIdColumn: MetadataColumn<string>, ISelectableColumn, IEventTableColumn
{
    public static readonly string ColumnName = "correlation_id";

    private static readonly Lazy<Action<DbDataReader, int, IEvent>> ReadSync =
        new(() => EventColumnReaders.BuildSync(x => x.CorrelationId));

    private static readonly Lazy<Func<DbDataReader, int, IEvent, CancellationToken, Task>> ReadAsyncDelegate =
        new(() => EventColumnReaders.BuildAsync(x => x.CorrelationId));

    public CorrelationIdColumn(): base(ColumnName, x => x.CorrelationId)
    {
        Enabled = false;
        ShouldUpdatePartials = true;
    }

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        return mapping.Metadata.CorrelationId.EnabledWithMember();
    }

    internal override UpsertArgument ToArgument()
    {
        return new CorrelationIdArgument();
    }

    public override void WriteMetadataInUpdateStatement(ICommandBuilder builder, DocumentSessionBase session)
    {
        builder.Append(ColumnName);
        builder.Append(" = ");
        builder.AppendParameter(session.CorrelationId);
    }

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }

    void IEventTableColumn.ReadValueSync(DbDataReader reader, int index, IEvent @event)
    {
        if (reader.IsDBNull(index)) return;
        ReadSync.Value(reader, index, @event);
    }

    async Task IEventTableColumn.ReadValueAsync(DbDataReader reader, int index, IEvent @event, CancellationToken cancellation)
    {
        if (await reader.IsDBNullAsync(index, cancellation).ConfigureAwait(false)) return;
        await ReadAsyncDelegate.Value(reader, index, @event, cancellation).ConfigureAwait(false);
    }
}

internal class CorrelationIdArgument: UpsertArgument
{
    public CorrelationIdArgument()
    {
        Arg = "correlationId";
        Column = CorrelationIdColumn.ColumnName;
        PostgresType = "varchar";
        DbType = NpgsqlDbType.Varchar;
    }
}
