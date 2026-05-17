#nullable enable
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <see cref="IDocumentMetadataBinder{TDoc}"/> for the
/// <c>mt_dotnet_type</c> column. Stores the full .NET type name of the
/// concrete instance — uses <c>document.GetType().FullName</c> so
/// hierarchical mappings record the subclass name, not the base type
/// <typeparamref name="TDoc"/>.
/// </summary>
internal sealed class DocumentDotNetTypeBinder<TDoc>: IDocumentMetadataBinder<TDoc>
    where TDoc : notnull
{
    // Fast path for non-hierarchical mappings: cached because
    // document.GetType() == typeof(TDoc) for every row.
    private static readonly string _baseTypeName = typeof(TDoc).FullName!;

    public string ColumnName => Marten.Schema.SchemaConstants.DotNetTypeColumn;

    public string ValueSql => "?";

    public void BindParameter(NpgsqlParameter parameter, TDoc document, IMartenSession session)
    {
        parameter.Value = ResolveTypeName(document);
        parameter.NpgsqlDbType = NpgsqlDbType.Varchar;
    }

    public void Apply(DbDataReader reader, int columnOrdinal, TDoc document, IMartenSession session)
    {
        // No-op — dotnet_type isn't projected back onto the document.
    }

    public Task WriteToBulkAsync(NpgsqlBinaryImporter writer, TDoc document,
        ISerializer serializer, CancellationToken cancellation)
        => writer.WriteAsync(ResolveTypeName(document), NpgsqlDbType.Varchar, cancellation);

    private static string ResolveTypeName(TDoc document)
    {
        var actual = document.GetType();
        return actual == typeof(TDoc) ? _baseTypeName : actual.FullName!;
    }
}
