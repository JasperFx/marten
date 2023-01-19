using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Exceptions;
using JasperFx.Core.Reflection;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Util;

namespace Marten.Internal;

public class UpdateBatch: IUpdateBatch
{
    private readonly IList<Exception> _exceptions = new List<Exception>();
    private readonly IReadOnlyList<IStorageOperation> _operations;

    public UpdateBatch(IReadOnlyList<IStorageOperation> operations)
    {
        _operations = operations;
    }

    public void ApplyChanges(IMartenSession session)
    {
        try
        {
            if (_operations.Count < session.Options.UpdateBatchSize)
            {
                var command = session.BuildCommand(_operations);
                using var reader = session.ExecuteReader(command);
                ApplyCallbacks(_operations, reader, _exceptions);
            }
            else
            {
                var count = 0;

                while (count < _operations.Count)
                {
                    var operations = _operations
                        .Skip(count)
                        .Take(session.Options.UpdateBatchSize)
                        .ToArray();

                    var command = session.BuildCommand(operations);
                    using var reader = session.ExecuteReader(command);
                    ApplyCallbacks(operations, reader, _exceptions);

                    count += session.Options.UpdateBatchSize;
                }
            }
        }
        catch (Exception e)
        {
            _operations.OfType<IExceptionTransform>().TransformAndThrow(e);
        }

        throwExceptionsIfAny();
    }

    public async Task ApplyChangesAsync(IMartenSession session, CancellationToken token)
    {
        // I know this smells to high heaven, but it works
        await session.As<DocumentSessionBase>().BeginTransactionAsync(token).ConfigureAwait(false);
        if (_operations.Count < session.Options.UpdateBatchSize)
        {
            var command = session.BuildCommand(_operations);
            try
            {
                await using var reader = await session.ExecuteReaderAsync(command, token).ConfigureAwait(false);
                await ApplyCallbacksAsync(_operations, reader, _exceptions, token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _operations.OfType<IExceptionTransform>().TransformAndThrow(e);
            }
        }
        else
        {
            var count = 0;

            while (count < _operations.Count)
            {
                var operations = _operations
                    .Skip(count)
                    .Take(session.Options.UpdateBatchSize)
                    .ToArray();

                var command = session.BuildCommand(operations);
                try
                {
                    await using var reader =
                        await session.ExecuteReaderAsync(command, token).ConfigureAwait(false);
                    await ApplyCallbacksAsync(operations, reader, _exceptions, token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _operations.OfType<IExceptionTransform>().TransformAndThrow(e);
                }

                count += session.Options.UpdateBatchSize;
            }
        }

        throwExceptionsIfAny();
    }

    private void throwExceptionsIfAny()
    {
        switch (_exceptions.Count)
        {
            case 0:
                return;

            case 1:
                throw _exceptions.Single();

            default:
                throw new AggregateException(_exceptions);
        }
    }

    public static void ApplyCallbacks(IReadOnlyList<IStorageOperation> operations, DbDataReader reader,
        IList<Exception> exceptions)
    {
        var first = operations.First();

        if (!(first is NoDataReturnedCall))
        {
            first.Postprocess(reader, exceptions);
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

        foreach (var operation in operations.Skip(1))
        {
            if (operation is NoDataReturnedCall)
            {
                continue;
            }

            operation.Postprocess(reader, exceptions);

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

    public static async Task ApplyCallbacksAsync(IReadOnlyList<IStorageOperation> operations, DbDataReader reader,
        IList<Exception> exceptions,
        CancellationToken token)
    {
        var first = operations.First();

        if (!(first is NoDataReturnedCall))
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

        foreach (var operation in operations.Skip(1))
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
