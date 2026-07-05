#nullable enable
using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Schema;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike (M11): <see cref="IDocumentMetadataBinder{TDoc}"/> for the
/// <c>mt_doc_type</c> column on a hierarchical mapping. Writes the
/// runtime type's hierarchy alias (looked up via
/// <see cref="DocumentMapping.AliasFor"/>); the alias drives the
/// SubClass storage's WHERE filter and selector dispatch on read.
/// </summary>
/// <remarks>
/// Read participation is handled directly by the selectors when the
/// descriptor has hierarchy mode on — <see cref="Apply"/> is a no-op
/// because the alias controls deserialization-type dispatch, not a
/// document member projection. If the document has a
/// <c>[DocumentTypeMember]</c>-annotated string, the codegen path
/// writes it there; deferred for the spike.
/// </remarks>
internal sealed class DocumentDocTypeBinder<TDoc>: IDocumentMetadataBinder<TDoc>
    where TDoc : notnull
{
    private readonly DocumentMapping _mapping;
    private readonly ConcurrentDictionary<Type, string> _aliasCache = new();

    public DocumentDocTypeBinder(DocumentMapping mapping)
    {
        _mapping = mapping;
    }

    public string ColumnName => Marten.Schema.SchemaConstants.DocumentTypeColumn;

    public string ValueSql => "?";

    public void BindParameter(NpgsqlParameter parameter, TDoc document, IStorageSession session)
    {
        var alias = _aliasCache.GetOrAdd(document.GetType(), t => _mapping.AliasFor(t));
        parameter.Value = alias;
        parameter.NpgsqlDbType = NpgsqlDbType.Varchar;
    }

    public void Apply(DbDataReader reader, int columnOrdinal, TDoc document, IStorageSession session)
    {
        // No-op — the alias is read directly by the selector to dispatch
        // deserialization to the right subclass type, not projected onto
        // the document.
    }

    public Task WriteToBulkAsync(NpgsqlBinaryImporter writer, TDoc document,
        ISerializer serializer, CancellationToken cancellation)
    {
        var alias = _aliasCache.GetOrAdd(document.GetType(), t => _mapping.AliasFor(t));
        return writer.WriteAsync(alias, NpgsqlDbType.Varchar, cancellation);
    }
}
