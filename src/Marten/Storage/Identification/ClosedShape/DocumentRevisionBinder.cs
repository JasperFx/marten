#nullable enable
using System;
using System.Data.Common;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M8): <see cref="IDocumentMetadataBinder{TDoc}"/> for the
/// <c>mt_version</c> column when the mapping uses
/// <see cref="ConcurrencyMode.Numeric"/>. The column is bigint rather
/// than uuid; the binder projects the loaded revision onto the
/// document's <c>[Version]</c>-annotated long member when one exists.
/// </summary>
/// <remarks>
/// Operations bind this slot themselves with the appropriate value
/// (caller-supplied <c>Revision</c> or auto-increment placeholder),
/// so <see cref="BindParameter"/> is only used when the operation
/// doesn't recognize the binder as the version slot — a defensive
/// default that writes <c>0</c> and lets the SQL's <c>CASE</c>
/// expression compute the effective value.
/// </remarks>
internal sealed class DocumentRevisionBinder<TDoc>: IDocumentMetadataBinder<TDoc>
    where TDoc : notnull
{
    private readonly Action<TDoc, long>? _setter;

    public DocumentRevisionBinder(MemberInfo? revisionMember)
    {
        if (revisionMember is not null)
        {
            _setter = LambdaBuilder.Setter<TDoc, long>(revisionMember);
        }
    }

    public string ColumnName => Marten.Schema.SchemaConstants.VersionColumn;

    public string ValueSql => "?";

    public void BindParameter(NpgsqlParameter parameter, TDoc document, IMartenSession session)
    {
        parameter.Value = 0L;
        parameter.NpgsqlDbType = NpgsqlDbType.Bigint;
    }

    public void Apply(DbDataReader reader, int columnOrdinal, TDoc document)
    {
        if (_setter is null) return;
        if (reader.IsDBNull(columnOrdinal)) return;

        var revision = reader.GetFieldValue<long>(columnOrdinal);
        _setter(document, revision);
    }

    /// <summary>
    /// W3 spike (M8): write a long revision directly onto the document's
    /// <c>[Version]</c>-annotated member.
    /// </summary>
    public void ApplyRevisionTo(TDoc document, long revision)
        => _setter?.Invoke(document, revision);
}
