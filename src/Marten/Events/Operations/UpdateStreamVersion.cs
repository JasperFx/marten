using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Schema;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

/// <summary>
/// Legacy base for the <c>mt_streams</c> version-update operation, from the pre-#4821
/// runtime-codegen write path.
/// </summary>
/// <remarks>
/// Superseded by <see cref="Weasel.Storage.UpdateStreamVersionOperationBase"/>, which the
/// closed-shape per-mode storage classes derive their update-stream-version operations from. This
/// copy has no subclasses and no call sites left anywhere in Marten; it is retained only so that a
/// 9.x consumer who subclassed it keeps compiling, and is slated for deletion in v10. See #5339.
/// </remarks>
[Obsolete("Superseded by Weasel.Storage.UpdateStreamVersionOperationBase, which the closed-shape event storage uses. This unused legacy base will be removed in Marten 10. See https://github.com/JasperFx/marten/issues/5339")]
public abstract class UpdateStreamVersion: IStorageOperation
{
    public UpdateStreamVersion(StreamAction stream)
    {
        Stream = stream;
    }

    public StreamAction Stream { get; }

    public abstract void ConfigureCommand(ICommandBuilder builder, IStorageSession session);

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (reader.RecordsAffected == 0)
        {
            exceptions.Add(new EventStreamUnexpectedMaxEventIdException(
                Stream.Key ?? (object)Stream.Id, Stream.AggregateType,
                Stream.ExpectedVersionOnServer.Value, -1));
        }

        return Task.CompletedTask;
    }

    public OperationRole Role()
    {
        return OperationRole.Events;
    }
}
