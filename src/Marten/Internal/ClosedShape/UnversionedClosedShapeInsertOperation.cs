#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten.Exceptions;
using Weasel.Core;
using Weasel.Postgresql;

using Marten.Internal.Storage;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Off</c> closed-shape Insert. Hand-written for the
/// no-version-no-revision shape: bind <c>[tenant_id,] id, data</c> + the
/// client-side metadata binders, then on RETURNING expect a row (otherwise
/// raise <see cref="DocumentAlreadyExistsException"/>). No tracker writes,
/// no version/revision binder special-casing. #4659 leaf.
/// </summary>
internal sealed class UnversionedClosedShapeInsertOperation<TDoc, TId>: ClosedShapeInsertOperation<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    public UnversionedClosedShapeInsertOperation(
        TDoc document,
        TId id,
        string tenantId,
        DocumentStorageDescriptor<TDoc, TId> descriptor)
        : base(document, id, tenantId, descriptor)
    {
    }

    public override void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var parameters = builder.AppendWithDbParameters(_descriptor.InsertSql, '?');
        var slot = BindLeadingParameters(parameters, session);

        // Off mode: every client-side write binder binds its single slot.
        // No version or revision binder is in this array under Off mode.
        foreach (var binder in _descriptor.ClientSideWriteBinders)
        {
            binder.BindParameter(parameters[slot], _document, session);
            slot++;
        }
    }

    public override async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            exceptions.Add(new DocumentAlreadyExistsException(null, typeof(TDoc), _id));
        }
    }
}
