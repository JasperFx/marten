#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Exceptions;
using Marten.Events.Daemon.Progress;
using Marten.Internal.Operations;
using Marten.Services;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Internal.Sessions;

public class OperationPage
{
    private IMartenSession _session;
    private readonly BatchBuilder _builder;
    private readonly List<IStorageOperation> _operations = new();

    public OperationPage(IMartenSession session)
    {
        _session = session;
        _builder = new BatchBuilder();
    }

    public OperationPage(IMartenSession session, IReadOnlyList<IStorageOperation> operations) : this(session)
    {
        _operations.AddRange(operations);
        foreach (var operation in operations)
        {
            _builder.StartNewCommand();
            operation.ConfigureCommand(_builder, _session);
        }

        Count = _operations.Count;
    }

    public int Count { get; private set; }
    public IReadOnlyList<IStorageOperation> Operations => _operations;

    public void Append(IStorageOperation operation)
    {
        if (_session == null) return;

        Count++;
        _builder.StartNewCommand();
        operation.ConfigureCommand(
            _builder,
            _session ?? throw new InvalidOperationException("Session already released!")
        );
        _builder.Append(";");
        _operations.Add(operation);
    }

    public NpgsqlBatch Compile()
    {
        return _builder.Compile();
    }

    public void ReleaseSession()
    {
        _session = null;
    }

    public void ApplyCallbacks(DbDataReader reader,
        IList<Exception> exceptions)
    {
        // The sync IStorageOperation.Postprocess overload was removed in Marten 9
        // per the dedup audit (#4351). Until the synchronous session paths
        // (AutoClosingLifetime.Execute etc.) are themselves retired, we bridge
        // here with .GetAwaiter().GetResult(). The bodies of every implementer's
        // former sync Postprocess were structurally identical to the async path
        // they delegate to, so this preserves behavior.
        var first = _operations.First();

        if (first is not NoDataReturnedCall)
        {
            first.PostprocessAsync(reader, exceptions, CancellationToken.None).GetAwaiter().GetResult();
            try
            {
                reader.NextResult();
            }
            catch (Exception e)
            {
                if (first is IExceptionTransform t && t.TryTransform(e, out var transformed))
                {
                    throw transformed;
                }

                throw;
            }
        }

        foreach (var operation in _operations.Skip(1))
        {
            if (operation is NoDataReturnedCall)
            {
                continue;
            }

            operation.PostprocessAsync(reader, exceptions, CancellationToken.None).GetAwaiter().GetResult();

            try
            {
                reader.NextResult();
            }
            catch (Exception e)
            {
                if (operation is IExceptionTransform t && t.TryTransform(e, out var transformed))
                {
                    throw transformed;
                }

                throw;
            }
        }
    }

    public async Task ApplyCallbacksAsync(DbDataReader reader,
        IList<Exception> exceptions,
        CancellationToken token)
    {
        var first = _operations.First();

        if (first is not NoDataReturnedCall)
        {
            await first.PostprocessAsync(reader, exceptions, token).ConfigureAwait(false);
            try
            {
                await reader.NextResultAsync(token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (first is IExceptionTransform t && t.TryTransform(e, out var transformed))
                {
                    throw transformed;
                }

                throw;
            }
        }
        else if (first is AssertsOnCallback)
        {
            await first.PostprocessAsync(reader, exceptions, token).ConfigureAwait(false);
        }

        foreach (var operation in _operations.Skip(1))
        {
            if (operation is NoDataReturnedCall)
            {
                continue;
            }

            await operation.PostprocessAsync(reader, exceptions, token).ConfigureAwait(false);
            try
            {
                await reader.NextResultAsync(token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (operation is IExceptionTransform t && t.TryTransform(e, out var transformed))
                {
                    throw transformed;
                }

                throw;
            }
        }
    }
}
