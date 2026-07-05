#nullable enable
using JasperFx.Events;
using Marten.Internal;
using Weasel.Postgresql;

namespace Marten.EventStorage;

/// <summary>
/// Single-tenant closed-shape assert-stream-version operation. Emits
/// <c>select version from {schema}.mt_streams where id = $1</c>. Selected by
/// <see cref="EventStorage{TId}.AssertStreamVersion"/> when the events table is
/// not conjoined-tenant.
/// </summary>
internal sealed class SingleTenantAssertStreamVersionOperation<TId>: AssertStreamVersionOperation<TId>
    where TId : notnull
{
    public SingleTenantAssertStreamVersionOperation(string selectVersionByIdPrefix, StreamAction stream)
        : base(selectVersionByIdPrefix, stream)
    {
    }

    public override void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append(SelectVersionByIdPrefix);
        var idParam = builder.AppendParameter(StreamIdentity);
        idParam.DbType = IdDbType;
    }
}
