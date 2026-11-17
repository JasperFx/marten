using System.Linq;
using JasperFx.Core;
using Marten.Schema.Indexing.Unique;
using Marten.Storage.Metadata;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Schema;

public class DocumentIndex: IndexDefinition
{
    private readonly string[] _columns;
    private readonly DocumentMapping _parent;

    public DocumentIndex(DocumentMapping parent, params string[] columns)
    {
        _parent = parent;
        _columns = columns;
    }

    public TenancyScope TenancyScope { get; set; } = TenancyScope.Global;

    public override string[]? Columns
    {
        get
        {
            if (TenancyScope == TenancyScope.Global)
            {
                return _columns;
            }

            return _columns.Concat(new[] { TenantIdColumn.Name }).ToArray();
        }
        set
        {
        }
    }

    public override string ToString()
    {
        return $"DocumentIndex for {_parent.DocumentType} on columns {Columns!.Join(", ")}";
    }

    /// <remarks>
    ///     #5672. Shortened for the same reason the document table's primary key (#5271/#4679) and the
    ///     document foreign key (<see cref="DocumentForeignKey" />) are. This is the index that
    ///     <c>Schema.For&lt;T&gt;().ForeignKey&lt;TReference&gt;(...)</c> adds beside the key, so the two
    ///     names go over PostgreSQL's 63 character limit on the same line of configuration -- fixing only
    ///     the key left the reported failure in place, one identifier further on. <c>Shorten</c> is a no-op
    ///     at 63 characters or fewer, so nothing changes for a name that already fit, and it appends a
    ///     deterministic hash rather than truncating, so two long names that agree for the first 58
    ///     characters do not collide into one index.
    /// </remarks>
    protected override string deriveIndexName()
    {
        return PostgresqlIdentifier.Shorten($"{_parent.TableName.Name}_idx_{_columns.Join("_")}");
    }
}
