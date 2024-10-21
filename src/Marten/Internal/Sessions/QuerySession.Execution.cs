#nullable enable

using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Internal.Sessions;

public partial class QuerySession
{
    internal IConnectionLifetime _connection;

    internal record CommandExecution(NpgsqlCommand Command, IConnectionLifetime Lifetime);

    public int Execute(NpgsqlCommand cmd)
    {
        RequestCount++;

        return _resilience.Execute(static e => e.Lifetime.Execute(e.Command), new CommandExecution(cmd, _connection));
    }

    public Task<int> ExecuteAsync(NpgsqlCommand command, CancellationToken token = new())
    {
        RequestCount++;
        return _resilience.ExecuteAsync(static (e, t) => new ValueTask<int>(e.Lifetime.ExecuteAsync(e.Command, t)), new CommandExecution(command, _connection), token).AsTask();
    }

    public DbDataReader ExecuteReader(NpgsqlCommand command)
    {
        RequestCount++;
        return _resilience.Execute(static e => e.Lifetime.ExecuteReader(e.Command), new CommandExecution(command, _connection));
    }

    public Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default)
    {
        RequestCount++;
        return _resilience.ExecuteAsync(static (e, t) => new ValueTask<DbDataReader>(e.Lifetime.ExecuteReaderAsync(e.Command, t)), new CommandExecution(command, _connection), token).AsTask();
    }

    internal record BatchExecution(NpgsqlBatch Batch, IConnectionLifetime Lifetime);

    public DbDataReader ExecuteReader(NpgsqlBatch batch)
    {
        RequestCount++;
        return _resilience.Execute(static e => e.Lifetime.ExecuteReader(e.Batch), new BatchExecution(batch, _connection));
    }

    public Task<DbDataReader> ExecuteReaderAsync(NpgsqlBatch batch, CancellationToken token = default)
    {
        RequestCount++;

        // This is executing via Polly
        return _resilience.ExecuteAsync(static (e, t)
            => new ValueTask<DbDataReader>(e.Lifetime.ExecuteReaderAsync(e.Batch, t)),
            new BatchExecution(batch, _connection), token).AsTask();
    }

    internal T? LoadOne<T>(NpgsqlCommand command, ISelector<T> selector)
    {
        using var reader = ExecuteReader(command);
        return !reader.Read() ? default : selector.Resolve(reader);
    }

    internal async Task<T?> LoadOneAsync<T>(NpgsqlCommand command, ISelector<T> selector, CancellationToken token)
    {
        await using var reader = await ExecuteReaderAsync(command, token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return default;
        }

        return await selector.ResolveAsync(reader, token).ConfigureAwait(false);
    }

    internal async Task<bool> StreamOne(NpgsqlCommand command, Stream stream, CancellationToken token)
    {
        await using var reader = (NpgsqlDataReader)await ExecuteReaderAsync(command, token).ConfigureAwait(false);
        return await reader.StreamOne(stream, token).ConfigureAwait(false) == 1;
    }

    internal async Task<int> StreamMany(NpgsqlCommand command, Stream stream, CancellationToken token)
    {
        await using var reader = (NpgsqlDataReader)await ExecuteReaderAsync(command, token).ConfigureAwait(false);

        return await reader.StreamMany(stream, token).ConfigureAwait(false);
    }

    public async Task<T> ExecuteHandlerAsync<T>(IQueryHandler<T> handler, CancellationToken token)
    {
        var cmd = this.BuildCommand(handler);

        await using var reader = await ExecuteReaderAsync(cmd, token).ConfigureAwait(false);
        return await handler.HandleAsync(reader, this, token).ConfigureAwait(false);
    }

    [Obsolete(QuerySession.SynchronousRemoval)]
    public T ExecuteHandler<T>(IQueryHandler<T> handler)
    {
        var batch = this.BuildCommand(handler);

        using var reader = ExecuteReader(batch);
        return handler.Handle(reader, this);
    }

    public void BeginTransaction()
    {
        if (_connection is IAlwaysConnectedLifetime lifetime)
        {
            lifetime.BeginTransaction();

        }
        else if (_connection is ITransactionStarter starter)
        {
            var tx = starter.Start();
            tx.BeginTransaction();
            _connection = tx;
        }
        else
        {
            throw new InvalidOperationException(
                $"The current lifetime {_connection} is neither a {nameof(IAlwaysConnectedLifetime)} nor a {nameof(ITransactionStarter)}");
        }
    }

    public async ValueTask BeginTransactionAsync(CancellationToken token)
    {
        if (_connection is IAlwaysConnectedLifetime lifetime)
        {
            await lifetime.BeginTransactionAsync(token).ConfigureAwait(false);
        }
        else if (_connection is ITransactionStarter starter)
        {
            var tx = await starter.StartAsync(token).ConfigureAwait(false);
            await tx.BeginTransactionAsync(token).ConfigureAwait(false);
            _connection = tx;
        }
        else
        {
            throw new InvalidOperationException(
                $"The current lifetime {_connection} is neither a {nameof(IAlwaysConnectedLifetime)} nor a {nameof(ITransactionStarter)}");
        }
    }
}
