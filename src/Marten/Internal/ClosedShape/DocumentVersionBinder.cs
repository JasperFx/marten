#nullable enable
using System;
using System.Data.Common;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike (M1): <see cref="IDocumentMetadataBinder{TDoc}"/> for the
/// <c>mt_version</c> column. Generates a new <see cref="Guid"/> per write
/// via <c>CombGuidIdGeneration.NewGuid()</c>, projects it onto the
/// document's <c>[Version]</c>-annotated member when one exists, binds
/// it as a Uuid parameter. Read path projects the stored version back
/// onto the same member if one exists.
/// </summary>
/// <remarks>
/// The W3 spike doesn't implement optimistic concurrency yet — when that
/// lands (M3), the new version is also stashed on the operation so
/// <c>Postprocess</c> can compare against the row's prior version and
/// raise <c>ConcurrencyException</c>. Today's spike just writes the new
/// version unconditionally.
/// </remarks>
internal sealed class DocumentVersionBinder<TDoc>: IDocumentMetadataBinder<TDoc>
    where TDoc : notnull
{
    private readonly Action<TDoc, Guid>? _setter;

    public DocumentVersionBinder(MemberInfo? versionMember)
    {
        if (versionMember is not null)
        {
            _setter = LambdaBuilder.Setter<TDoc, Guid>(versionMember);
        }
    }

    public string ColumnName => Marten.Schema.SchemaConstants.VersionColumn;

    public string ValueSql => "?";

    public void BindParameter(NpgsqlParameter parameter, TDoc document, IStorageSession session)
    {
        var newVersion = CombGuidIdGeneration.NewGuid();
        _setter?.Invoke(document, newVersion);

        parameter.Value = newVersion;
        parameter.NpgsqlDbType = NpgsqlDbType.Uuid;
    }

    public void Apply(DbDataReader reader, int columnOrdinal, TDoc document, IStorageSession session)
    {
        if (_setter is null) return;
        if (reader.IsDBNull(columnOrdinal)) return;

        var version = reader.GetFieldValue<Guid>(columnOrdinal);
        _setter(document, version);
    }

    /// <summary>
    /// W3 spike (M7): write a Guid version directly onto the document's
    /// <c>[Version]</c>-annotated member, bypassing the reader. Used from
    /// the operation's Postprocess after it confirms the row's returned
    /// version matches the just-written one.
    /// </summary>
    public void ApplyVersionTo(TDoc document, Guid version)
        => _setter?.Invoke(document, version);

    public Task WriteToBulkAsync(NpgsqlBinaryImporter writer, TDoc document,
        IStorageSerializer serializer, CancellationToken cancellation)
    {
        var newVersion = CombGuidIdGeneration.NewGuid();
        _setter?.Invoke(document, newVersion);
        return writer.WriteAsync(newVersion, NpgsqlDbType.Uuid, cancellation);
    }
}
