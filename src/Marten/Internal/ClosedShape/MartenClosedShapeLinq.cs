#nullable enable
using System.Linq;
using JasperFx.Core;
using Marten.Internal.Storage;
using Marten.Linq.SqlGeneration;

namespace Marten.Internal.ClosedShape;

/// <summary>
///     The parts of Marten's LINQ facet that are more than a forward, shared by the four closed-shape
///     storages (#5733).
/// </summary>
internal static class MartenClosedShapeLinq
{
    /// <summary>
    ///     The select clause with the duplicated-field columns appended, for the temporary tables of
    ///     <c>Include()</c>. A new instance every time, because an Include() batch renames its
    ///     <c>FromObject</c>.
    /// </summary>
    public static ISelectClause SelectClauseWithDuplicatedFields<TDoc>(ILinqDocumentStorage storage)
        where TDoc : notnull
    {
        if (!storage.DuplicatedFields.Any())
        {
            return storage;
        }

        var selectFields = storage.SelectFields();
        var duplicatedFields = storage.DuplicatedFields.Select(x => "d." + x.ColumnName)
            .Where(x => !selectFields.Contains(x));
        var allFields = selectFields.Concat(duplicatedFields).ToArray();

        return new DuplicatedFieldSelectClause(storage.TableName.QualifiedName,
            $"select {allFields.Join(", ")} from {storage.TableName.QualifiedName} as d", allFields, typeof(TDoc),
            storage);
    }
}
