#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Npgsql;

namespace Marten.Internal.Sessions;

internal class ConnectionLifetimeBase
{
    public IMartenSessionLogger Logger { get; set; }

    protected void handleCommandException(NpgsqlCommand cmd, Exception e)
    {
        Logger.LogFailure(cmd, e);

        MartenExceptionTransformer.WrapAndThrow(cmd, e);
    }

    protected void handleCommandException(NpgsqlBatch batch, Exception e)
    {
        Logger.LogFailure(batch, e);

        MartenExceptionTransformer.WrapAndThrow(batch, e);
    }
}

public interface ITransactionStarter
{
    IAlwaysConnectedLifetime Start();
    Task<IAlwaysConnectedLifetime> StartAsync(CancellationToken token);
}

public interface IAlwaysConnectedLifetime : IConnectionLifetime
{
    NpgsqlConnection Connection { get; }

    void BeginTransaction();
    ValueTask BeginTransactionAsync(CancellationToken token);
}


public interface IConnectionLifetime: IAsyncDisposable, IDisposable
{
    IMartenSessionLogger Logger { get; set; }
    int CommandTimeout { get; }

    int Execute(NpgsqlCommand cmd);
    Task<int> ExecuteAsync(NpgsqlCommand command, CancellationToken token = new());

    DbDataReader ExecuteReader(NpgsqlCommand command);

    Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command,
        CancellationToken token = default);

    DbDataReader ExecuteReader(NpgsqlBatch batch);

    Task<DbDataReader> ExecuteReaderAsync(NpgsqlBatch batch,
        CancellationToken token = default);

    void ExecuteBatchPages(IReadOnlyList<OperationPage> pages, List<Exception> exceptions);
    Task ExecuteBatchPagesAsync(IReadOnlyList<OperationPage> pages,
        List<Exception> exceptions, CancellationToken token);
}
