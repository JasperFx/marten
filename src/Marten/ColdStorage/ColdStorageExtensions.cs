#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events.Archiving;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Storage;
using Npgsql;
using Weasel.Postgresql.Tables;

namespace Marten.ColdStorage;

public static class ColdStorageExtensions
{
    /// <summary>
    ///     Let rows be moved out of their PostgreSQL table into <paramref name="provider" />: archived
    ///     events, the older events of a stream, and documents of types marked with
    ///     <c>AllowColdStorage()</c>.
    /// </summary>
    public static StoreOptions UseColdStorage(this StoreOptions options, IColdStorageProvider provider)
    {
        options.ColdStorageProvider = provider;
        return options;
    }

    /// <summary>
    ///     Run the provider's initialization for every table that can have cold rows, each statement in
    ///     its own transaction. Needed once per database before the first cold read.
    /// </summary>
    public static async Task InitializeColdStorageAsync(this IDocumentStore store, CancellationToken token = default)
    {
        var options = ((DocumentStore)store).Options;
        var provider = options.ColdStorageProvider ?? throw new InvalidOperationException(
            $"No cold storage is configured. Call {nameof(UseColdStorage)}() first.");

        var tables = new List<Table> { new EventsTable(options.EventGraph) };
        tables.AddRange(options.Storage.AllDocumentMappings.Where(x => x.UseColdStorage).Select(x => (Table)x.Schema.Table));

        await using var conn = store.Storage.Database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        foreach (var table in tables)
        {
            foreach (var sql in provider.InitializationSql(ColdViews.DatasetFor(table.Identifier), ColdViews.ColumnsOf(table)))
            {
                await using var cmd = new NpgsqlCommand(sql, conn);
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     The query should include cold documents (or events), with a hot row winning over a cold row
    ///     with the same id.
    /// </summary>
    public static bool MaybeCold(this object doc) => true;

    /// <summary>
    ///     Load a document by id from hot storage, falling back to cold storage.
    /// </summary>
    public static async Task<T?> LoadMaybeColdAsync<T>(this IQuerySession session, object id,
        CancellationToken token = default) where T : notnull
    {
        var mapping = ((IMartenSession)session).Options.Storage.MappingFor(typeof(T));
        var doc = Expression.Parameter(typeof(T), "x");
        var idMember = Expression.MakeMemberAccess(doc, mapping.IdMember);
        var filter = Expression.Lambda<Func<T, bool>>(
            Expression.AndAlso(maybeCold(doc), Expression.Equal(idMember, Expression.Constant(id, idMember.Type))), doc);

        var found = await session.Query<T>().Where(filter).ToListAsync(token).ConfigureAwait(false);
        return found.SingleOrDefault();
    }

    internal static async Task<IReadOnlyList<T>> LoadManyMaybeColdAsync<T, TId>(this IQuerySession session,
        TId[] ids, CancellationToken token) where T : notnull
    {
        var mapping = ((IMartenSession)session).Options.Storage.MappingFor(typeof(T));
        var doc = Expression.Parameter(typeof(T), "x");
        var idMember = Expression.MakeMemberAccess(doc, mapping.IdMember);
        // pg_duckdb cannot type an array parameter (id = ANY($1)), so this is id = $1 OR id = $2 ...
        var anyId = ids.Select(id => (Expression)Expression.Equal(idMember, Expression.Constant(id, idMember.Type)))
            .Aggregate(Expression.OrElse);
        var filter = Expression.Lambda<Func<T, bool>>(Expression.AndAlso(maybeCold(doc), anyId), doc);

        return await session.Query<T>().Where(filter).ToListAsync(token).ConfigureAwait(false);
    }

    private static Expression maybeCold(ParameterExpression doc) =>
        Expression.Call(typeof(ColdStorageExtensions).GetMethod(nameof(MaybeCold))!, Expression.Convert(doc, typeof(object)));

    /// <summary>
    ///     Move every archived event still in <c>mt_events</c> to cold storage. Returns the number moved.
    /// </summary>
    public static async Task<long> MoveArchivedEventsToColdStorageAsync(this IDocumentStore store,
        CancellationToken token = default)
    {
        var table = new EventsTable(((DocumentStore)store).Options.EventGraph);
        return await moveAsync(store, table, $"{IsArchivedColumn.ColumnName} = TRUE",
            "tenant_id, stream_id, version", token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Move all but the last event of a live stream to cold storage. The stream row and its last
    ///     event stay hot, so versioning and appends are unchanged, and a live aggregation of the stream
    ///     reads the moved events back.
    /// </summary>
    public static async Task<long> MoveStreamEventsToColdStorageAsync(this IDocumentStore store, Guid streamId,
        CancellationToken token = default)
    {
        var table = new EventsTable(((DocumentStore)store).Options.EventGraph);
        var stream = $"stream_id = '{streamId}'";
        return await moveAsync(store, table,
            $"{stream} AND {IsArchivedColumn.ColumnName} = FALSE AND version < (SELECT max(version) FROM {table.Identifier.QualifiedName} WHERE {stream})",
            "tenant_id, stream_id, version", token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Move the documents of <typeparamref name="T" /> matching <paramref name="predicate" /> to cold
    ///     storage. Duplicated fields go along as typed columns.
    /// </summary>
    public static async Task<long> MoveDocumentsToColdStorageAsync<T>(this IDocumentStore store,
        Expression<Func<T, bool>> predicate, string? tenantId = null, CancellationToken token = default) where T : notnull
    {
        var options = ((DocumentStore)store).Options;
        var mapping = options.Storage.MappingFor(typeof(T));
        if (!mapping.UseColdStorage || options.ColdStorageProvider == null)
        {
            throw new InvalidOperationException(
                $"Document type {typeof(T).Name} does not allow cold storage. Call UseColdStorage() and Schema.For<{typeof(T).Name}>().AllowColdStorage().");
        }

        await using var session = tenantId == null ? store.QuerySession() : store.QuerySession(tenantId);
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);

        var matching = await session.Query<T>().Where(predicate).ToListAsync(token).ConfigureAwait(false);
        var ids = matching.Select(x => mapping.IdMember switch
        {
            PropertyInfo p => p.GetValue(x)!,
            FieldInfo f => f.GetValue(x)!,
            _ => throw new NotSupportedException()
        }).ToList();

        if (ids.Count == 0)
        {
            return 0;
        }

        var table = mapping.Schema.Table;
        var idType = table.Columns.Single(x => x.Name == "id").Type;
        var where = $"id = ANY(ARRAY[{ids.Select(literal).Join(", ")}]::{idType}[])";
        if (mapping.TenancyStyle == TenancyStyle.Conjoined)
        {
            where += $" AND tenant_id = {literal(session.TenantId)}";
        }

        return await moveAsync(store, table, where,
            mapping.TenancyStyle == TenancyStyle.Conjoined ? "tenant_id, id" : "id", token).ConfigureAwait(false);
    }

    // COPY cannot take parameters, so the move's filter is inlined.
    private static string literal(object value) => value switch
    {
        string s => $"'{s.Replace("'", "''")}'",
        Guid g => $"'{g}'",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException($"Cannot move documents with an id of type {value.GetType().Name}")
    };

    private static async Task<long> moveAsync(IDocumentStore store, Table table, string where, string orderBy,
        CancellationToken token)
    {
        var provider = ((DocumentStore)store).Options.ColdStorageProvider ?? throw new InvalidOperationException(
            $"No cold storage is configured. Call {nameof(UseColdStorage)}() first.");

        var hot = table.Identifier.QualifiedName;

        await using var conn = store.Storage.Database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        // One snapshot for the export and the delete, so a row changed in between is neither
        // exported nor deleted.
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, token).ConfigureAwait(false);

        await using (var count = new NpgsqlCommand($"select count(*) from {hot} where {where}", conn, tx))
        {
            if ((long)(await count.ExecuteScalarAsync(token).ConfigureAwait(false))! == 0)
            {
                return 0;
            }
        }

        var columns = table.Columns.Select(x => x.Name).Join(", ");
        var batchKey = $"{DateTime.UtcNow:yyyyMMddHHmmssfffffff}_{Guid.NewGuid():N}";
        var export = provider.ExportSql(ColdViews.DatasetFor(table.Identifier),
            $"select {columns} from {hot} where {where} order by {orderBy}", batchKey);
        await using (var copy = new NpgsqlCommand(export, conn, tx))
        {
            await copy.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        long moved;
        await using (var delete = new NpgsqlCommand($"delete from {hot} where {where}", conn, tx))
        {
            moved = await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        await tx.CommitAsync(token).ConfigureAwait(false);
        return moved;
    }
}
