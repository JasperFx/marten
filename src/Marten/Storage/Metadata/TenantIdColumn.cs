using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Schema;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage.Metadata;

internal class TenantIdColumn: MetadataColumn<string>, ISelectableColumn, IEventTableColumn, IStreamTableColumn
{
    public static new readonly string Name = StorageConstants.TenantIdColumn;

    private static readonly Lazy<Action<DbDataReader, int, IEvent>> ReadSync =
        new(() => EventColumnReaders.BuildSync(x => x.TenantId));

    private static readonly Lazy<Func<DbDataReader, int, IEvent, CancellationToken, Task>> ReadAsyncDelegate =
        new(() => EventColumnReaders.BuildAsync(x => x.TenantId));

    public TenantIdColumn(): base(Name, x => x.TenantId)
    {
        DefaultExpression = $"'{StorageConstants.DefaultTenantId}'";
    }

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        return Member != null;
    }

    bool IStreamTableColumn.Reads => true;

    bool IStreamTableColumn.Writes => true;

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
