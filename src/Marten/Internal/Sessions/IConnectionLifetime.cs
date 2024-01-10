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
    protected void handleCommandException(IMartenSessionLogger logger, NpgsqlCommand cmd, Exception e)
    {
        logger.LogFailure(cmd, e);

        MartenExceptionTransformer.WrapAndThrow(cmd, e);
    }

    protected void handleCommandException(IMartenSessionLogger logger, NpgsqlBatch batch, Exception e)
    {
        logger.LogFailure(batch, e);

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
    int CommandTimeout { get; }

    int Execute(NpgsqlCommand cmd, IMartenSessionLogger logger);
    Task<int> ExecuteAsync(NpgsqlCommand command, IMartenSessionLogger logger, CancellationToken token = new());

    DbDataReader ExecuteReader(NpgsqlCommand command, IMartenSessionLogger logger);

    Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, IMartenSessionLogger logger,
        CancellationToken token = default);

    DbDataReader ExecuteReader(NpgsqlBatch batch, IMartenSessionLogger logger);

    Task<DbDataReader> ExecuteReaderAsync(NpgsqlBatch batch, IMartenSessionLogger logger,
        CancellationToken token = default);

    void ExecuteBatchPages(IReadOnlyList<OperationPage> pages, IMartenSessionLogger logger, List<Exception> exceptions);
    Task ExecuteBatchPagesAsync(IReadOnlyList<OperationPage> pages, IMartenSessionLogger logger,
        List<Exception> exceptions, CancellationToken token);
}
