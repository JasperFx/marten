#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using Marten.Exceptions;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Optimistic</c> closed-shape Update. Generates a
/// fresh Guid version at construction time, binds it for the SET
/// VersionBinder slot, and binds the caller's expected Guid version
/// (from the session tracker, or DBNull if unknown) at the trailing
/// WHERE concurrency slot. Missing RETURNING row → version mismatch →
/// <see cref="ConcurrencyException"/>. #4659 leaf.
/// </summary>
internal sealed class OptimisticClosedShapeUpdateOperation<TDoc, TId>: ClosedShapeUpdateOperation<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    private readonly Dictionary<TId, Guid> _versions;
    private readonly Guid _newVersion;

    public OptimisticClosedShapeUpdateOperation(
        TDoc document,
        TId id,
        string tenantId,
        DocumentStorageDescriptor<TDoc, TId> descriptor,
        Dictionary<TId, Guid> versions)
        : base(document, id, tenantId, descriptor)
    {
        _versions = versions;
        _newVersion = CombGuidIdGeneration.NewGuid();
    }

    public override void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        var parameters = builder.AppendWithParameters(_descriptor.UpdateSql, '?');
        var slot = BindPreConcurrencyParameters(parameters, session);

        // Trailing WHERE mt_version = ? guard.
        if (_versions.TryGetValue(_id, out var expected))
        {
            parameters[slot].Value = expected;
        }
        else
        {
            parameters[slot].Value = DBNull.Value;
        }
        parameters[slot].NpgsqlDbType = NpgsqlDbType.Uuid;
    }

    public override async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (!IgnoreConcurrencyViolation)
            {
                exceptions.Add(new ConcurrencyException(typeof(TDoc), _id));
            }
            return;
        }

        _versions[_id] = _newVersion;
    }

    protected override int BindClientSideBinder(NpgsqlParameter[] parameters, int slot, IDocumentMetadataBinder<TDoc> binder, IMartenSession session)
    {
        if (ReferenceEquals(binder, _descriptor.VersionBinder))
        {
            parameters[slot].Value = _newVersion;
            parameters[slot].NpgsqlDbType = NpgsqlDbType.Uuid;
            _descriptor.VersionBinder!.ApplyVersionTo(_document, _newVersion);
            return slot + 1;
        }

        binder.BindParameter(parameters[slot], _document, session);
        return slot + 1;
    }
}
