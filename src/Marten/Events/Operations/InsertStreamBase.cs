using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Exceptions;
using JasperFx.Events;
using Marten.Events.Schema;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Services;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

/// <summary>
/// Legacy base for the <c>mt_streams</c> insert operation, from the pre-#4821 runtime-codegen
/// write path.
/// </summary>
/// <remarks>
/// Superseded by <see cref="Weasel.Storage.InsertStreamOperationBase"/>. Its stream-id-collision
/// translation now lives in <c>PostgresEventStoreDialect.MapInsertStreamException</c>, which the
/// dialect installs on the descriptor as the neutral base's
/// <c>TransformInsertStreamException</c> closure. This copy has no subclasses and no call sites
/// left anywhere in Marten; it is retained only so that a 9.x consumer who subclassed it keeps
/// compiling, and is slated for deletion in v10. See #5339.
/// </remarks>
[Obsolete("Superseded by Weasel.Storage.InsertStreamOperationBase, which the closed-shape event storage uses; the collision translation now lives in PostgresEventStoreDialect.MapInsertStreamException. This unused legacy base will be removed in Marten 10. See https://github.com/JasperFx/marten/issues/5339")]
public abstract class InsertStreamBase: IStorageOperation, IExceptionTransform, NoDataReturnedCall
{
    public InsertStreamBase(StreamAction stream)
    {
        Stream = stream;
    }

    public StreamAction Stream { get; }

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
        return $"InsertStream: {Stream.Key ?? Stream.Id.ToString()}";
    }

    private static bool matches(Exception e)
    {
        return e is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            TableName: StreamsTable.TableName
                or StreamIdentityEnforcementTable.TableName
        };
    }

    public bool TryTransform(Exception original, out Exception transformed)
    {
        if (original is MartenCommandException mce)
        {
            if (mce.InnerException != null &&
                matches(mce.InnerException))
            {
                transformed =
                    new ExistingStreamIdCollisionException((object)Stream.Key ?? Stream.Id, Stream.AggregateType);
                return true;
            }
        }

        if (matches(original))
        {
            transformed =
                new ExistingStreamIdCollisionException((object)Stream.Key ?? Stream.Id, Stream.AggregateType);
            return true;
        }

        transformed = original;
        return false;
    }
}
