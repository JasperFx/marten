using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Services;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

/// <summary>
/// Legacy base for the per-event append operation, from the pre-#4821 runtime-codegen write path.
/// </summary>
/// <remarks>
/// Superseded by <see cref="Weasel.Storage.AppendEventOperationBase"/>, which the closed-shape
/// hierarchy's Rich append operation derives from and which
/// <c>EventTracingConnectionLifetime</c> type-tests against. This copy has no subclasses and no
/// call sites left anywhere in Marten; it is retained only so that a 9.x consumer who subclassed
/// it keeps compiling, and is slated for deletion in v10. See #5339.
/// </remarks>
[Obsolete("Superseded by Weasel.Storage.AppendEventOperationBase, which the closed-shape event storage uses. This unused legacy base will be removed in Marten 10. See https://github.com/JasperFx/marten/issues/5339")]
public abstract class AppendEventOperationBase: IStorageOperation, NoDataReturnedCall
{
    public AppendEventOperationBase(StreamAction stream, IEvent e)
    {
        Stream = stream;
        Event = e;

        if (e.Version == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(e), "Version cannot be 0");
        }
    }

    public StreamAction Stream { get; }
    public IEvent Event { get; }

    public abstract void ConfigureCommand(ICommandBuilder builder, IStorageSession session);

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role()
    {
        return OperationRole.Events;
    }

    public override string ToString()
    {
        return $"Insert Event to Stream {Stream.Key ?? Stream.Id.ToString()}, Version {Event.Version}";
    }
}

