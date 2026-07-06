#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using Marten.Exceptions;
using Weasel.Core;
using Weasel.Postgresql;

using Marten.Internal.Storage;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Optimistic</c> closed-shape Insert. Generates a
/// fresh Guid version at construction time, binds it in place of the
/// VersionBinder's parameter slot, then writes it back through
/// <see cref="DocumentVersionBinder{TDoc}.ApplyVersionTo"/> + the
/// session's per-type version tracker on RETURNING success. #4659 leaf.
/// </summary>
internal sealed class OptimisticClosedShapeInsertOperation<TDoc, TId>: ClosedShapeInsertOperation<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    private readonly Dictionary<TId, Guid>? _versions;
    private readonly Guid _newVersion;

    public OptimisticClosedShapeInsertOperation(
        TDoc document,
        TId id,
        string tenantId,
        DocumentStorageDescriptor<TDoc, TId> descriptor,
        Dictionary<TId, Guid>? versions)
        : base(document, id, tenantId, descriptor)
    {
        _versions = versions;
        _newVersion = CombGuidIdGeneration.NewGuid();
    }

    public override void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var parameters = builder.AppendWithDbParameters(_descriptor.InsertSql, '?');
        var slot = BindLeadingParameters(parameters, session);

        foreach (var binder in _descriptor.ClientSideWriteBinders)
        {
            if (ReferenceEquals(binder, _descriptor.VersionBinder))
            {
                parameters[slot].Value = _newVersion;
                _descriptor.Dialect.SetParameterType(parameters[slot], StorageColumnType.Guid);
                _descriptor.VersionBinder!.ApplyVersionTo(_document, _newVersion);
            }
            else
            {
                binder.BindParameter(parameters[slot], _document, session);
            }
            slot++;
        }
    }

    public override async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            exceptions.Add(new DocumentAlreadyExistsException(null, typeof(TDoc), _id));
            return;
        }

        // #4667 — null tracker (the InsertProjected path) just skips the
        // tracker write. The fresh version is still applied to the document
        // via VersionBinder during command configuration.
        if (_versions is not null)
        {
            _versions[_id] = _newVersion;
        }
    }
}
