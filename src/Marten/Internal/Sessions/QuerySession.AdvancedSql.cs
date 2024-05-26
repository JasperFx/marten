using Marten.Linq.QueryHandlers;
using Marten.Util;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Marten.Internal.Sessions;

public partial class QuerySession: IAdvancedSql
{
    public async IAsyncEnumerable<T> StreamAsync<T>(string sql, [EnumeratorCancellation] CancellationToken token,
        params object[] parameters)
    {
        assertNotDisposed();

        var handler = new AdvancedSqlQueryHandler<T>(this, sql, parameters);

        foreach (var documentType in handler.DocumentTypes)
        {
            await Database.EnsureStorageExistsAsync(documentType, token).ConfigureAwait(false);
        }

        var batch = this.BuildCommand(handler);
        await using var reader = await ExecuteReaderAsync(batch, token).ConfigureAwait(false);

        await foreach (var result in handler.EnumerateResults(reader, token))
        {
            yield return result;
        }
    }

    public async IAsyncEnumerable<(T1, T2)> StreamAsync<T1, T2>(string sql, [EnumeratorCancellation] CancellationToken token,
        params object[] parameters)
    {
        assertNotDisposed();

        var handler = new AdvancedSqlQueryHandler<T1, T2>(this, sql, parameters);

        foreach (var documentType in handler.DocumentTypes)
        {
            await Database.EnsureStorageExistsAsync(documentType, token).ConfigureAwait(false);
        }

        var batch = this.BuildCommand(handler);
        await using var reader = await ExecuteReaderAsync(batch, token).ConfigureAwait(false);

        await foreach (var result in handler.EnumerateResults(reader, token))
        {
            yield return result;
        }
    }

    public async IAsyncEnumerable<(T1, T2, T3)> StreamAsync<T1, T2, T3>(string sql, [EnumeratorCancellation] CancellationToken token,
        params object[] parameters)
    {
        assertNotDisposed();

        var handler = new AdvancedSqlQueryHandler<T1, T2, T3>(this, sql, parameters);

        foreach (var documentType in handler.DocumentTypes)
        {
            await Database.EnsureStorageExistsAsync(documentType, token).ConfigureAwait(false);
        }

        var batch = this.BuildCommand(handler);
        await using var reader = await ExecuteReaderAsync(batch, token).ConfigureAwait(false);

        await foreach (var result in handler.EnumerateResults(reader, token))
        {
            yield return result;
        }
    }
}
