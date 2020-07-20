using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Operations;
using Marten.Schema.Arguments;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Internal
{
    public class UpdateBatch
    {
        private readonly IReadOnlyList<IStorageOperation> _operations;

        private readonly IList<Exception> _exceptions = new List<Exception>();

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
                    var command = buildCommand(session, _operations);
                    using var reader = session.Database.ExecuteReader(command);
                    applyCallbacks(_operations, reader);
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

                        var command = buildCommand(session, operations);
                        using var reader = session.Database.ExecuteReader(command);
                        applyCallbacks(operations, reader);

                        count += session.Options.UpdateBatchSize;
                    }
                }
            }
            catch (Exception e)
            {
                Exception transformed = null;

                if (_operations.OfType<IExceptionTransform>().Any(x => x.TryTransform(e, out transformed)))
                {
                    throw transformed;
                }
                throw;
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

        public async Task ApplyChangesAsync(IMartenSession session, CancellationToken token)
        {
            if (_operations.Count < session.Options.UpdateBatchSize)
            {
                var command = buildCommand(session, _operations);
                try
                {
                    using var reader = await session.Database.ExecuteReaderAsync(command, token).ConfigureAwait(false);
                    await applyCallbacksAsync(_operations, reader, token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Exception transformed = null;
                    if (_operations.OfType<IExceptionTransform>().Any(x => x.TryTransform(e, out transformed)))
                    {
                        throw transformed;
                    }
                    throw;
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

                    var command = buildCommand(session, operations);
                    try
                    {
                        using var reader = await session.Database.ExecuteReaderAsync(command, token).ConfigureAwait(false);
                        await applyCallbacksAsync(operations, reader, token).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                            Exception transformed = null;
                            if (operations.OfType<IExceptionTransform>().Any(x => x.TryTransform(e, out transformed)))
                            {
                                throw transformed;
                            }
                            throw;
                    }

                    count += session.Options.UpdateBatchSize;
                }
            }

            throwExceptionsIfAny();
        }

        private void applyCallbacks(IReadOnlyList<IStorageOperation> operations, DbDataReader reader)
        {
            var first = operations.First();

            if (!(first is NoDataReturnedCall))
            {
                first.Postprocess(reader, _exceptions);
                try
                {
                    reader.NextResult();
                }
                catch (Exception e)
                {
                    Exception transformed = null;

                    if (operations.OfType<IExceptionTransform>().Any(x => x.TryTransform(e, out transformed)))
                    {
                        throw transformed;
                    }
                    throw;
                }
            }

            foreach (var operation in operations.Skip(1))
            {
                if (!(operation is NoDataReturnedCall))
                {
                    operation.Postprocess(reader, _exceptions);
                    try
                    {
                        reader.NextResult();
                    }
                    catch (Exception e)
                    {
                        Exception transformed = null;

                        if (operations.OfType<IExceptionTransform>().Any(x => x.TryTransform(e, out transformed)))
                        {
                            throw transformed;
                        }
                        throw;
                    }
                }
            }
        }

        private async Task applyCallbacksAsync(IReadOnlyList<IStorageOperation> operations, DbDataReader reader, CancellationToken token)
        {
            var first = operations.First();

            if (!(first is NoDataReturnedCall))
            {
                await first.PostprocessAsync(reader, _exceptions, token).ConfigureAwait(false);
                await reader.NextResultAsync(token).ConfigureAwait(false);
            }

            foreach (var operation in operations.Skip(1))
            {
                if (!(operation is NoDataReturnedCall))
                {
                    await operation.PostprocessAsync(reader, _exceptions, token).ConfigureAwait(false);
                    await reader.NextResultAsync(token).ConfigureAwait(false);
                }
            }
        }

        private NpgsqlCommand buildCommand(IMartenSession session, IEnumerable<IStorageOperation> operations)
        {
            var command = new NpgsqlCommand();
            var builder = new CommandBuilder(command);

            foreach (var operation in operations)
            {
                operation.ConfigureCommand(builder, session);
                builder.Append(';');
            }

            // Duplication here!
            command.CommandText = builder.ToString();

            // TODO -- Like this to be temporary
            if (command.CommandText.Contains(CommandBuilder.TenantIdArg))
            {
                command.AddNamedParameter(TenantIdArgument.ArgName, session.Tenant.TenantId);
            }

            return command;
        }


    }
}
