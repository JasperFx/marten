#nullable enable
using System.Data.Common;
using Marten.Internal;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M1): <see cref="IDocumentMetadataBinder{TDoc}"/> for the
/// <c>mt_dotnet_type</c> column. Stores the full .NET type name on each
/// write. The binder is closed-shape over <typeparamref name="TDoc"/> —
/// the type name is the same string for every row this binder writes,
/// cached as a readonly field.
/// </summary>
/// <remarks>
/// In the hierarchical case the read path would consult this column to
/// pick the right subtype to deserialize as. That branch lands with M5 —
/// for now <see cref="Apply"/> is a no-op (the deserializer already knows
/// the type).
/// </remarks>
internal sealed class DocumentDotNetTypeBinder<TDoc>: IDocumentMetadataBinder<TDoc>
    where TDoc : notnull
{
    private static readonly string _typeName = typeof(TDoc).FullName!;

    public string ColumnName => Marten.Schema.SchemaConstants.DotNetTypeColumn;

    public string ValueSql => "?";

    public void BindParameter(NpgsqlParameter parameter, TDoc document, IMartenSession session)
    {
        parameter.Value = _typeName;
        parameter.NpgsqlDbType = NpgsqlDbType.Varchar;
    }

    public void Apply(DbDataReader reader, int columnOrdinal, TDoc document)
    {
        // No-op for the non-hierarchical case. Hierarchical dispatch lives
        // at M5.
    }
}
