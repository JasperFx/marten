#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Internal;
using Marten.Internal.Operations;
using Weasel.Postgresql;

namespace Marten.EventStorage;

/// <summary>
/// Closed-shape "assert expected stream version, append nothing" operation for
/// the <c>AlwaysEnforceConsistency</c> zero-events path. Two concrete variants
/// ship — <see cref="SingleTenantAssertStreamVersionOperation{TId}"/> and
/// <see cref="ConjoinedAssertStreamVersionOperation{TId}"/> — and the owning
/// <see cref="EventStorage{TId}"/> picks which to instantiate based on the
/// descriptor's tenancy style. That keeps the tenancy branch in the storage
/// layer (resolved from configuration) rather than re-evaluating it on every
/// <see cref="ConfigureCommand"/> call, and the stream-identity variation
/// (<see cref="Guid"/> vs <see cref="string"/>) is fixed by the closed
/// generic <typeparamref name="TId"/>.
/// </summary>
internal abstract class AssertStreamVersionOperation<TId>: IStorageOperation where TId : notnull
{
    /// <summary>
    /// The <c>select version from {schema}.mt_streams where id = </c> prefix,
    /// built once per store by the dialect and threaded through the descriptor.
    /// Subclasses append the identity parameter (and, when conjoined, the
    /// trailing <c>and tenant_id = $N</c>).
    /// </summary>
    protected readonly string SelectVersionByIdPrefix;

    protected AssertStreamVersionOperation(string selectVersionByIdPrefix, StreamAction stream)
    {
        SelectVersionByIdPrefix = selectVersionByIdPrefix;
        Stream = stream;
    }

    public StreamAction Stream { get; }

    /// <summary>
    /// The value bound as the <c>id</c> predicate — <see cref="StreamAction.Id"/>
    /// for Guid-identified streams, <see cref="StreamAction.Key"/> for string.
    /// The closed generic fixes the choice, so there is no per-call branch.
    /// </summary>
    protected object StreamIdentity => typeof(TId) == typeof(Guid) ? Stream.Id : Stream.Key!;

    /// <summary>
    /// <see cref="DbType"/> for the bound <c>id</c> parameter, fixed by the
    /// closed generic <typeparamref name="TId"/>. Mirrors
    /// <c>ClosedShapeStreamStateQueryHandler</c>.
    /// </summary>
    protected static readonly DbType IdDbType = typeof(TId) == typeof(Guid) ? DbType.Guid : DbType.String;

    public abstract void ConfigureCommand(ICommandBuilder builder, IStorageSession session);

    public Type DocumentType => typeof(IEvent);

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            exceptions.Add(new EventStreamUnexpectedMaxEventIdException(StreamIdentity, Stream.AggregateType,
                Stream.ExpectedVersionOnServer!.Value, 0));
            return;
        }

        var actualVersion = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
        if (actualVersion != Stream.ExpectedVersionOnServer!.Value)
        {
            exceptions.Add(new EventStreamUnexpectedMaxEventIdException(StreamIdentity, Stream.AggregateType,
                Stream.ExpectedVersionOnServer.Value, actualVersion));
        }
    }

    public OperationRole Role() => OperationRole.Events;
}
