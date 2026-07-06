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
    private readonly List<Weasel.Storage.IStorageOperation> _operations = new();

    public OperationPage(IMartenSession session)
    {
        _session = session;
        _builder = new BatchBuilder();
    }

    public OperationPage(IMartenSession session, IReadOnlyList<Weasel.Storage.IStorageOperation> operations) : this(session)
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
    public IReadOnlyList<Weasel.Storage.IStorageOperation> Operations => _operations;

    public void Append(Weasel.Storage.IStorageOperation operation)
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

    public async Task ApplyCallbacksAsync(DbDataReader reader,
        IList<Exception> exceptions,
        CancellationToken token)
    {
        // 9.0 (#4375): indexed loop avoids the SkipIterator + List enumerator allocations
        // the old `_operations.First()` + `_operations.Skip(1)` pattern triggered per page.
        var first = _operations[0];

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

        for (var i = 1; i < _operations.Count; i++)
        {
            var operation = _operations[i];
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
