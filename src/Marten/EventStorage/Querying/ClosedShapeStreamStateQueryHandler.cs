#nullable enable
using System;
using System.Data;
using Marten.Events.Querying;
using Marten.Internal;
using Marten.Storage.Metadata;
using Weasel.Postgresql;

namespace Marten.EventStorage.Querying;

/// <summary>
/// Non-codegen <see cref="StreamStateQueryHandler"/> implementation for the
/// closed-shape hierarchy (#4412). Composes the SELECT clause from the
/// descriptor with a per-call <c>where id = $1 [and tenant_id = $2]</c> tail.
/// Row reading is inherited from the base, which delegates to the
/// <c>ISelector&lt;StreamState&gt;</c> implementation on
/// <c>EventDocumentStorage</c> — same definition site as the codegen path
/// (see JasperFx/marten#4347).
/// </summary>
/// <remarks>
/// Per-call dispatch is a single instance with cached SQL + identity values;
/// the tenancy-conjoined branch is decided at construction, not on each
/// <see cref="ConfigureCommand"/> call.
/// </remarks>
internal sealed class ClosedShapeStreamStateQueryHandler<TId>: StreamStateQueryHandler
{
    private readonly string _selectSql;
    private readonly TId _streamId;
    private readonly string? _tenantId;
    private readonly DbType _idDbType;

    public ClosedShapeStreamStateQueryHandler(string selectSql, TId streamId, string? tenantId)
    {
        _selectSql = selectSql;
        _streamId = streamId;
        _tenantId = tenantId;
        _idDbType = typeof(TId) == typeof(Guid) ? DbType.Guid : DbType.String;
    }

    public override void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append(_selectSql);
        builder.Append(" where id = ");
        var idParam = builder.AppendParameter((object)_streamId!);
        idParam.DbType = _idDbType;

        if (_tenantId is not null)
        {
            builder.Append(" and ");
            builder.Append(TenantIdColumn.Name);
            builder.Append(" = ");
            var tenantParam = builder.AppendParameter((object)_tenantId);
            tenantParam.DbType = DbType.String;
        }
    }
}
