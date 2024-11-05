using Marten.Internal.Storage;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;

namespace Marten.Linq.Includes;

internal class PassthroughSelectStatement: SelectorStatement
{
    public PassthroughSelectStatement(string tableName, ISelectClause innerSelectClause)
    {
        SelectClause = innerSelectClause;
        TableName = tableName;
    }

    public override string FromObject => TableName;

    public string TableName { get; set; }

    protected override void configure(IPostgresqlCommandBuilder sql)
    {
        if (SelectClause is IDocumentStorage || (SelectClause is IStatsSelectClause stats && (stats.Inner is IDocumentStorage || stats.Inner is DuplicatedFieldSelectClause)))
        {
            sql.Append("select * from ");
            sql.Append(TableName);
            sql.Append(" as d;");
        }
        else
        {
            // Hack, but makes SelectMany() work where the exact table gets lost
            if (SelectClause is IModifyableFromObject o) o.FromObject = TableName;

            SelectClause.Apply(sql);
        }


    }
}
