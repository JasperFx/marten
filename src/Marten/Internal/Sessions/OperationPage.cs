#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
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
    private readonly NpgsqlBatch _batch = new();
    private readonly BatchBuilder _builder;
    private readonly List<Weasel.Storage.IStorageOperation> _operations = new();

    // The index of each operation's LAST statement in the batch. Most operations write one statement, but
    // some write several (PatchOperation, DcbTagVersionAssertion, DeleteStreamOperation), so operation i
    // is not statement i.
    private readonly List<int> _lastStatements = new();
    private NpgsqlBatch? _compiled;
    private bool _completionIsReadable;

    public OperationPage(IMartenSession session)
    {
        _session = session;
        _builder = new BatchBuilder(_batch);
    }

    public OperationPage(IMartenSession session, IReadOnlyList<Weasel.Storage.IStorageOperation> operations) : this(session)
    {
        _operations.AddRange(operations);
        foreach (var operation in operations)
        {
            _builder.StartNewCommand();
            operation.ConfigureCommand(_builder, _session);
            _lastStatements.Add(_batch.BatchCommands.Count - 1);
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
        _lastStatements.Add(_batch.BatchCommands.Count - 1);
    }

    public NpgsqlBatch Compile()
    {
        // #5502: kept so ApplyCallbacksAsync can read each statement's OWN RecordsAffected.
        // _lastStatements maps each operation to its last statement, because an operation can start
        // commands of its own. Every caller compiles a page and applies its callbacks back to
        // back on the same instance (see TransactionalConnection.ExecuteBatchPagesAsync and its
        // three siblings), so this is the same batch the reader was opened over.
        _compiled = _builder.Compile();

        // #5914: Npgsql records each statement's StatementType as its CommandComplete arrives and never
        // resets it, so it can only say which statements completed on a batch that has not run before. The
        // resilience pipeline re-executes this very batch, and then the positional window alone decides.
        _completionIsReadable = true;
        for (var i = 0; i < _compiled.BatchCommands.Count; i++)
        {
            if (_compiled.BatchCommands[i].StatementType != StatementType.Unknown)
            {
                _completionIsReadable = false;
                break;
            }
        }

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
        // #5914: the operations whose statements the reader is executing through right now. An error belongs
        // to one of them and to nobody else, so it is never offered to the operation the reader just LEFT.
        int windowStart = 0, windowEnd = -1;

        try
        {
            // 9.0 (#4375): indexed loop avoids the SkipIterator + List enumerator allocations
            // the old `_operations.First()` + `_operations.Skip(1)` pattern triggered per page.
            for (var i = 0; i < _operations.Count; i++)
            {
                var operation = _operations[i];
                if (operation is NoDataReturnedCall)
                {
                    // Only ever in first position, as before: a projection progress write that asserts
                    // without a result set of its own
                    if (i == 0 && operation is AssertsOnCallback)
                    {
                        (windowStart, windowEnd) = (i, i);

                        // marten#6003: the reader's RecordsAffected is cumulative, so a row-affecting statement
                        // between this one and the first result set (a per-identity rebuild's HardDeleteById, a
                        // parked row's delete) hid a stale progression update and let the batch commit. By now
                        // the reader is past this statement, so its own count is filled in: use it when it is.
                        if (operation is IAssertsRecordsAffected asserts &&
                            ownRecordsAffected(i) is var own and >= 0 &&
                            asserts.DetectRecordsAffectedProblem(own) is { } problem)
                        {
                            throw problem;
                        }

                        await operation.PostprocessAsync(reader, exceptions, token).ConfigureAwait(false);
                    }

                    continue;
                }

                (windowStart, windowEnd) = (i, i);
                await operation.PostprocessAsync(reader, exceptions, token).ConfigureAwait(false);

                // #5914: advancing executes every statement up to and including the next one with a result
                // set -- the NoDataReturnedCall statements in between have none -- and it is one of THOSE
                // that raised anything thrown here. This used to be offered to operation i.
                (windowStart, windowEnd) = (i + 1, nextResultSetOwner(i));
                await reader.NextResultAsync(token).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            if (tryTransform(e, windowStart, windowEnd, out var transformed))
            {
                throw transformed;
            }

            throw;
        }

        await assertRecordsAffectedAsync(reader, token).ConfigureAwait(false);
    }

    /// <summary>
    ///     #5914: translate a failure thrown while OPENING the reader over this page's batch. The reader opens
    ///     on the first result set, so it executed every statement up to the first operation that has one, and
    ///     only those operations may translate it -- the same rule as <see cref="ApplyCallbacksAsync" />.
    /// </summary>
    internal bool TryTransformExecuteFailure(Exception e, [NotNullWhen(true)] out Exception? transformed)
        => tryTransform(e, 0, nextResultSetOwner(-1), out transformed);

    /// <summary>
    ///     The index of the first operation after <paramref name="index" /> whose statement returns a result
    ///     set, or the last operation when none does: how far the reader executes when it advances.
    /// </summary>
    private int nextResultSetOwner(int index)
    {
        for (var i = index + 1; i < _operations.Count; i++)
        {
            if (_operations[i] is not NoDataReturnedCall)
            {
                return i;
            }
        }

        return _operations.Count - 1;
    }

    /// <summary>
    ///     #5914: offer <paramref name="e" /> to the operations whose statements could have raised it, in
    ///     statement order, and mark it -- or what it became -- as attributed, so that
    ///     <see cref="TransformsFor" /> does not offer it to the rest of the batch afterwards.
    /// </summary>
    /// <remarks>
    ///     Npgsql 10.0.3 leaves <c>NpgsqlException.BatchCommand</c> null for an error raised by any statement
    ///     but the first (measured; weasel's <c>operation_batch_executor_against_postgresql</c> pins the same),
    ///     so it cannot name the statement. The reader's position can: an error surfaces from the call that
    ///     executed its statement. That is exact whenever the window holds one operation -- every statement
    ///     with a result set, which is every quick-append event write -- and otherwise narrows it to a run of
    ///     <see cref="NoDataReturnedCall" /> statements and the one statement that ends it, within which
    ///     <see cref="firstIncompleteStatement" /> names the failing one on a batch's first execution. A
    ///     re-executed batch falls back to offering that run in statement order.
    /// </remarks>
    private bool tryTransform(Exception e, int windowStart, int windowEnd, [NotNullWhen(true)] out Exception? transformed)
    {
        transformed = null;
        if (windowStart > windowEnd || windowStart < 0 || windowEnd >= _operations.Count)
        {
            // Nothing was executing: leave it to TransformsFor's fallback
            return false;
        }

        markAttributed(e);

        // Several statements ran in this window: on a first execution, the one that failed is the first that
        // never completed
        var failed = windowStart == windowEnd ? windowStart : firstIncompleteStatement(windowStart, windowEnd);
        if (failed >= 0)
        {
            (windowStart, windowEnd) = (failed, failed);
        }

        for (var i = windowStart; i <= windowEnd; i++)
        {
            if (_operations[i] is IExceptionTransform transform && transform.TryTransform(e, out transformed))
            {
                markAttributed(transformed);
                return true;
            }
        }

        transformed = null;
        return false;
    }

    /// <summary>
    ///     #5914: the first operation in the window whose statement has no CommandComplete yet, or -1 when that
    ///     cannot be read. Only for a NoDataReturnedCall run, where the reader's position cannot tell the
    ///     statements apart: PostgreSQL completes statements in order and stops at the first error, so every
    ///     statement before the failing one has completed and the failing one has not.
    /// </summary>
    private int firstIncompleteStatement(int windowStart, int windowEnd)
    {
        if (!_completionIsReadable || _compiled == null || windowEnd >= _lastStatements.Count ||
            _lastStatements[windowEnd] >= _compiled.BatchCommands.Count)
        {
            return -1;
        }

        for (var i = windowStart; i <= windowEnd; i++)
        {
            // An operation of several statements has failed when its last one never completed: statements
            // complete in order, and an error in an earlier one stops the rest
            var command = _compiled.BatchCommands[_lastStatements[i]];

            // An empty statement answers EmptyQueryResponse, never CommandComplete
            if (command.StatementType == StatementType.Unknown && command.CommandText.AsSpan().Trim(" \t\r\n;").Length > 0)
            {
                return i;
            }
        }

        return -1;
    }

    // The rows operation i's own statement affected, or -1 when Npgsql hasn't reported it (yet)
    private int ownRecordsAffected(int i)
    {
        if (_compiled == null || i >= _lastStatements.Count) return -1;

        var statement = _lastStatements[i];
        return statement < _compiled.BatchCommands.Count ? _compiled.BatchCommands[statement].RecordsAffected : -1;
    }

    private const string AttributedKey = "Marten.OperationPage.Attributed";

    private static void markAttributed(Exception e)
    {
        try
        {
            e.Data[AttributedKey] = true;
        }
        catch (Exception)
        {
            // An exception type with a read-only Data dictionary only loses the narrowing, never the error
        }
    }

    private static bool isAttributed(Exception e)
    {
        for (var current = e; current != null; current = current.InnerException)
        {
            if (current.Data.Contains(AttributedKey))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     #5914: the operation transforms that may still translate a failure of these pages. One that a page
    ///     already offered to the operations whose statements raised it -- in <see cref="ApplyCallbacksAsync" />
    ///     or <see cref="TryTransformExecuteFailure" /> -- is offered to no operation again: handing it to every
    ///     operation in order let the first one that recognised the SQLSTATE claim it, whichever statement
    ///     raised it. Anything else (a failure outside every statement) is offered to every operation, as before.
    /// </summary>
    internal static IEnumerable<IExceptionTransform> TransformsFor(IReadOnlyList<OperationPage> pages, Exception e)
    {
        return isAttributed(e)
            ? []
            : pages.SelectMany(x => x.Operations).OfType<IExceptionTransform>();
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

        var count = Math.Min(_operations.Count, _lastStatements.Count);

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

            var statement = _lastStatements[i];
            if (statement >= _compiled.BatchCommands.Count)
            {
                continue;
            }

            var problem = asserts.DetectRecordsAffectedProblem(_compiled.BatchCommands[statement].RecordsAffected);
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
