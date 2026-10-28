#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Exceptions;
using JasperFx.Events;
using Weasel.Core;
using Weasel.Storage;

namespace Marten.EventStorage.Quick;

/// <summary>
/// A per-event Quick append that says which event it appends, so verbose connection tracing can emit
/// <c>marten.append.event</c> for it (#5772).
/// </summary>
/// <remarks>
/// Weasel's per-event Quick append that reads its <c>seq_id</c> back (weasel#747, used since #5755) implements
/// <see cref="Weasel.Storage.IStorageOperation"/> directly and is internal, so unlike every other append it is not an
/// <see cref="AppendEventOperationBase"/> and the tracer cannot see its event. Only wrapped when
/// <c>TrackConnections</c> is <c>Verbose</c>, so nothing else pays for it. Implements exactly what the wrapped
/// operation does, and in particular not <see cref="NoDataReturnedCall"/>: it reads a result.
/// </remarks>
internal sealed class TracedQuickAppendEventOperation: Weasel.Storage.IStorageOperation, IExceptionTransform
{
    private readonly Weasel.Storage.IStorageOperation _inner;

    public TracedQuickAppendEventOperation(Weasel.Storage.IStorageOperation inner, StreamAction stream, IEvent e)
    {
        _inner = inner;
        Stream = stream;
        Event = e;
    }

    public StreamAction Stream { get; }
    public IEvent Event { get; }

    public Type DocumentType => _inner.DocumentType;

    public void ConfigureCommand(Weasel.Core.ICommandBuilder builder, Weasel.Storage.IStorageSession session)
    {
        _inner.ConfigureCommand(builder, session);
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return _inner.PostprocessAsync(reader, exceptions, token);
    }

    public OperationRole Role()
    {
        return _inner.Role();
    }

    public bool TryTransform(Exception original, out Exception? transformed)
    {
        if (_inner is IExceptionTransform transform)
        {
            return transform.TryTransform(original, out transformed);
        }

        transformed = null;
        return false;
    }

    public override string ToString()
    {
        return _inner.ToString()!;
    }
}
