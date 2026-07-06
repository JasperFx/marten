#nullable enable
using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike (M11): <see cref="IDocumentMetadataBinder{TDoc}"/> for the
/// <c>mt_doc_type</c> column on a hierarchical mapping. Writes the
/// runtime type's hierarchy alias (looked up via an agnostic
/// <c>Func&lt;Type, string&gt;</c> the builder captures from
/// <c>DocumentMapping.AliasFor</c> — #4829, so this binder no longer
/// references the Marten mapping type); the alias drives the SubClass
/// storage's WHERE filter and selector dispatch on read.
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
    private readonly Func<Type, string> _resolveAlias;
    private readonly ConcurrentDictionary<Type, string> _aliasCache = new();

    public DocumentDocTypeBinder(Func<Type, string> resolveAlias)
    {
        _resolveAlias = resolveAlias;
    }

    public string ColumnName => Marten.Schema.SchemaConstants.DocumentTypeColumn;

    public string ValueSql => "?";

    public void BindParameter(NpgsqlParameter parameter, TDoc document, IStorageSession session)
    {
        var alias = _aliasCache.GetOrAdd(document.GetType(), t => _resolveAlias(t));
        parameter.Value = alias;
        parameter.NpgsqlDbType = NpgsqlDbType.Varchar;
    }

    public void Apply(DbDataReader reader, int columnOrdinal, TDoc document, IStorageSession session)
    {
        // No-op — the alias is read directly by the selector to dispatch
        // deserialization to the right subclass type, not projected onto
        // the document.
    }

    public BulkColumnValue GetBulkValue(TDoc document)
    {
        var alias = _aliasCache.GetOrAdd(document.GetType(), t => _resolveAlias(t));
        return new BulkColumnValue(alias, StorageColumnType.String);
    }
}
