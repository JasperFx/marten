using System.Linq;
using JasperFx.Core;
using Marten.Schema.Indexing.Unique;
using Marten.Storage.Metadata;
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

    public override string[] Columns
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
        return $"DocumentIndex for {_parent.DocumentType} on columns {Columns.Join(", ")}";
    }

    protected override string deriveIndexName()
    {
        return $"{_parent.TableName.Name}_idx_{_columns.Join("_")}";
    }
}
