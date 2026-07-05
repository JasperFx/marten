#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Optimistic</c> closed-shape Overwrite. Generates a
/// fresh Guid version, binds it for the SET VersionBinder slot, no
/// trailing WHERE guard (Overwrite explicitly skips the version check).
/// PostprocessAsync writes the new version to the session tracker on
/// RETURNING success; a missing row only happens when the tracker dict
/// was supplied null (the projection / fire-and-forget path) — silent
/// no-op. #4659 leaf.
/// </summary>
internal sealed class OptimisticClosedShapeOverwriteOperation<TDoc, TId>: ClosedShapeOverwriteOperation<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    private readonly Dictionary<TId, Guid>? _versions;
    private readonly Guid _newVersion;

    public OptimisticClosedShapeOverwriteOperation(
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
        var parameters = builder.AppendWithParameters(_descriptor.OverwriteSql, '?');
        BindPreOnConflictParameters(parameters, session);
        // Overwrite drops the trailing concurrency-guard slot vs Upsert.
    }

    public override async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            // #4658 — null tracker (the OverwriteProjected path) just
            // skips the tracker write. The fresh version is still
            // applied to the document via VersionBinder during command
            // configuration.
            if (_versions is not null)
            {
                _versions[_id] = _newVersion;
            }
        }
    }

    protected override int BindClientSideBinder(NpgsqlParameter[] parameters, int slot, IDocumentMetadataBinder<TDoc> binder, IStorageSession session)
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
