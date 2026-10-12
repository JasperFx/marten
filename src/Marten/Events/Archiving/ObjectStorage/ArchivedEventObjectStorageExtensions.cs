#nullable enable
using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events.Schema;
using Npgsql;

namespace Marten.Events.Archiving.ObjectStorage;

public static class ArchivedEventObjectStorageExtensions
{
    /// <summary>
    ///     Let archived events be moved out of <c>mt_events</c> into object storage with
    ///     <see cref="MoveArchivedEventsToObjectStorageAsync" />. Queries that include archived events
    ///     (<c>MaybeArchived()</c>, <c>IsArchived</c>) then read them back from there; every other event
    ///     query is unchanged.
    /// </summary>
    public static StoreOptions UseObjectStorageForArchivedEvents(this StoreOptions options,
        IArchivedEventObjectStorage storage)
    {
        options.EventGraph.ArchivedEventObjectStorage = storage;
        return options;
    }

    /// <summary>
    ///     Write every archived event still in <c>mt_events</c> to one new object in object storage and
    ///     delete it from <c>mt_events</c>. Returns the number of events moved.
    /// </summary>
    public static async Task<long> MoveArchivedEventsToObjectStorageAsync(this IDocumentStore store,
        CancellationToken token = default)
    {
        var events = ((DocumentStore)store).Options.EventGraph;
        var storage = events.ArchivedEventObjectStorage ?? throw new InvalidOperationException(
            $"No object storage is configured for archived events. Call {nameof(UseObjectStorageForArchivedEvents)}() first.");

        var table = new EventsTable(events);
        var archived = $"{table.Identifier.QualifiedName} where {IsArchivedColumn.ColumnName} = TRUE";

        await using var conn = store.Storage.Database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        // One snapshot for the export and the delete, so an event archived in between is neither
        // exported nor deleted.
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, token).ConfigureAwait(false);

        long first, last;
        await using (var range = new NpgsqlCommand($"select min(seq_id), max(seq_id) from {archived}", conn, tx))
        await using (var reader = await range.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            await reader.ReadAsync(token).ConfigureAwait(false);
            if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
            {
                return 0;
            }

            first = reader.GetInt64(0);
            last = reader.GetInt64(1);
        }

        var columns = table.Columns.Select(x => x.Name).Join(", ");
        var export = storage.ExportSql($"select {columns} from {archived} order by seq_id", $"{first:D19}-{last:D19}");
        await using (var copy = new NpgsqlCommand(export, conn, tx))
        {
            await copy.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        long moved;
        await using (var delete = new NpgsqlCommand($"delete from {archived}", conn, tx))
        {
            moved = await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        await tx.CommitAsync(token).ConfigureAwait(false);
        return moved;
    }
}
