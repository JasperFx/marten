#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using Marten.Exceptions;
using Marten.Internal.Operations;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Optimistic</c> closed-shape Upsert. Generates a
/// fresh Guid version, binds it for the SET VersionBinder slot, and
/// binds the caller's expected Guid version at the trailing
/// <c>ON CONFLICT DO UPDATE … WHERE table.mt_version = ?</c> slot.
/// Missing RETURNING row → version mismatch →
/// <see cref="ConcurrencyException"/>. #4659 leaf.
/// </summary>
internal sealed class OptimisticClosedShapeUpsertOperation<TDoc, TId>: ClosedShapeUpsertOperation<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    private readonly Dictionary<TId, Guid>? _versions;
    private readonly Guid _newVersion;

    public OptimisticClosedShapeUpsertOperation(
        TDoc document,
        TId id,
        string tenantId,
        DocumentStorageDescriptor<TDoc, TId> descriptor,
        OperationRole role,
        Dictionary<TId, Guid>? versions)
        : base(document, id, tenantId, descriptor, role)
    {
        _versions = versions;
        _newVersion = CombGuidIdGeneration.NewGuid();
    }

    public override void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        var parameters = builder.AppendWithParameters(_descriptor.UpsertSql, '?');
        var slot = BindPreOnConflictParameters(parameters, session);

        // Trailing WHERE table.mt_version = ? guard. #4667 — null tracker
        // (the UpsertProjected path) treats expected version as DBNull. On
        // the ON CONFLICT branch the WHERE will never match, so existing
        // rows in Optimistic mode are left untouched. The INSERT branch
        // still fires for new rows.
        if (_versions is not null && _versions.TryGetValue(_id, out var expected))
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

        // #4667 — null tracker (the UpsertProjected path) just skips the
        // tracker write. The fresh version is still applied to the document
        // via VersionBinder during command configuration.
        if (_versions is not null)
        {
            _versions[_id] = _newVersion;
        }
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
