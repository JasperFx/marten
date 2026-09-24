#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Exceptions;
using JasperFx.Core.Reflection;
using Marten.Events.Daemon.Progress;
using Marten.Internal.Operations;
using Marten.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Internal.Sessions;

public class OperationPage
{
    private IMartenSession _session;
    private readonly BatchBuilder _builder;
    private readonly List<Weasel.Storage.IStorageOperation> _operations = new();
    private NpgsqlBatch? _compiled;

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
        // #5502: kept so ApplyCallbacksAsync can read each statement's OWN RecordsAffected.
        // BatchBuilder.StartNewCommand() appends exactly one NpgsqlBatchCommand per Append() call
        // above and no Marten operation starts a command of its own, so _operations[i] is always
        // _compiled.BatchCommands[i]. Every caller compiles a page and applies its callbacks back to
        // back on the same instance (see TransactionalConnection.ExecuteBatchPagesAsync and its
        // three siblings), so this is the same batch the reader was opened over.
        _compiled = _builder.Compile();
        return _compiled;
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

        await assertRecordsAffectedAsync(reader, token).ConfigureAwait(false);
    }

    /// <summary>
    /// #5502: the per-statement row-count pass. Operations marked <see cref="NoDataReturnedCall" />
    /// are skipped by the loop above without ever reaching a callback, so an
    /// <see cref="IAssertsRecordsAffected" /> operation anywhere but the first position was never
    /// checked at all — which for a composite projection means the parent's progression is verified
    /// and every member's is not, since <c>ExecutionStage</c> records each member's progress into the
    /// parent's shared batch.
    /// </summary>
    /// <remarks>
    /// This runs as a separate pass, after the reader is drained, because Npgsql fills
    /// <c>NpgsqlBatchCommand.RecordsAffected</c> lazily as the reader advances: a statement's own
    /// count still reads -1 while the reader is parked on an earlier one, so there is no point inside
    /// the loop above at which an operation could read it.
    ///
    /// <para>
    /// It <b>logs</b> rather than throws. Making the check effective converts stale progression
    /// writes that have been silently no-opping into hard shard failures, and
    /// <c>ShardFailure</c> categorizes <c>ProgressionProgressOutOfOrderException</c> as a split-brain
    /// double-daemon — an actively misleading diagnosis for what is usually a loader bug (#5501 was
    /// one). Surfacing it costs nothing; promoting it to a throw is a breaking change and belongs in
    /// a major release. The exception the operation hands back is the one that would be thrown.
    /// </para>
    /// </remarks>
    private async Task assertRecordsAffectedAsync(DbDataReader reader, CancellationToken token)
    {
        if (_compiled == null || _session == null)
        {
            return;
        }

        var count = Math.Min(_operations.Count, _compiled.BatchCommands.Count);

        var anyToCheck = false;
        for (var i = 0; i < count; i++)
        {
            if (_operations[i] is IAssertsRecordsAffected)
            {
                anyToCheck = true;
                break;
            }
        }

        if (!anyToCheck)
        {
            return;
        }

        // Drain whatever result sets are left so Npgsql has read every statement's CommandComplete
        // and the per-statement counts are populated. Non-row-returning statements occupy no result
        // set of their own, so this is cheap: it steps over the remaining data-returning ones.
        try
        {
            while (await reader.NextResultAsync(token).ConfigureAwait(false))
            {
            }
        }
        catch (Exception)
        {
            // A reader that will not advance cannot tell us any row counts, and this pass is
            // diagnostic. The real failure is already on its way up from the loop above.
            return;
        }

        ILogger? logger = null;

        for (var i = 0; i < count; i++)
        {
            if (_operations[i] is not IAssertsRecordsAffected asserts)
            {
                continue;
            }

            var problem = asserts.DetectRecordsAffectedProblem(_compiled.BatchCommands[i].RecordsAffected);
            if (problem == null)
            {
                continue;
            }

            logger ??= _session.Options.LogFactory?.CreateLogger<OperationPage>()
                       ?? _session.Options.DotNetLogger
                       ?? NullLogger.Instance;

            logger.LogWarning(problem,
                "Operation {Operation} at position {Position} of this batch affected no rows. This will become a thrown exception in a future major release. See https://github.com/JasperFx/marten/issues/5502",
                _operations[i].GetType().FullNameInCode(), i);
        }
    }
}
