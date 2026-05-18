using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Schema;
using Marten.Internal.CodeGeneration;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Archiving;

internal class IsArchivedColumn: TableColumn, IEventTableColumn
{
    internal const string ColumnName = "is_archived";

    private static readonly Lazy<Action<DbDataReader, int, IEvent>> ReadSync =
        new(() => EventColumnReaders.BuildSync(x => x.IsArchived));

    private static readonly Lazy<Func<DbDataReader, int, IEvent, CancellationToken, Task>> ReadAsyncDelegate =
        new(() => EventColumnReaders.BuildAsync(x => x.IsArchived));

    public IsArchivedColumn(): base(ColumnName, "bool")
    {
        DefaultExpression = "FALSE";
    }

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }

    public void ReadValueSync(DbDataReader reader, int index, IEvent @event)
    {
        if (reader.IsDBNull(index)) return;
        ReadSync.Value(reader, index, @event);
    }

    public async Task ReadValueAsync(DbDataReader reader, int index, IEvent @event, CancellationToken cancellation)
    {
        if (await reader.IsDBNullAsync(index, cancellation).ConfigureAwait(false)) return;
        await ReadAsyncDelegate.Value(reader, index, @event, cancellation).ConfigureAwait(false);
    }
}
