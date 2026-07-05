#nullable enable
using System.Data;
using JasperFx.Events;
using Marten.Internal;
using Marten.Storage.Metadata;
using Weasel.Postgresql;

namespace Marten.EventStorage;

/// <summary>
/// Conjoined-tenant closed-shape assert-stream-version operation. Emits
/// <c>select version from {schema}.mt_streams where id = $1 and tenant_id = $2</c>.
/// Selected by <see cref="EventStorage{TId}.AssertStreamVersion"/> when the
/// events table is conjoined-tenant. The trailing tenant predicate restores the
/// <c>(tenant_id, id)</c> primary-key point seek and scopes the version check to
/// the fetching tenant (see #4803).
/// </summary>
internal sealed class ConjoinedAssertStreamVersionOperation<TId>: AssertStreamVersionOperation<TId>
    where TId : notnull
{
    public ConjoinedAssertStreamVersionOperation(string selectVersionByIdPrefix, StreamAction stream)
        : base(selectVersionByIdPrefix, stream)
    {
    }

    public override void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append(SelectVersionByIdPrefix);
        var idParam = builder.AppendParameter(StreamIdentity);
        idParam.DbType = IdDbType;

        builder.Append(" and ");
        builder.Append(TenantIdColumn.Name);
        builder.Append(" = ");
        var tenantParam = builder.AppendParameter((object)Stream.TenantId);
        tenantParam.DbType = DbType.String;
    }
}
