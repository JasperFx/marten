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

    protected override void configure(ICommandBuilder sql)
    {
        // A statistics query has already written its count(*) over() into the temp table, so for the
        // clauses that read their columns straight back the whole row is read back, the stats column
        // with it. Re-applying the clause instead would recompute the window function over the temp
        // table, which the inner statement's Take() has already truncated, and the count would come
        // back as the size of the page. #5653 added DataAndIdSelectClause here, which is what a
        // query session's storage becomes once the include's temp table puts the id back.
        if (SelectClause is IDocumentStorage || (SelectClause is IStatsSelectClause stats &&
                                                 stats.Inner is IDocumentStorage
                                                     or DuplicatedFieldSelectClause
                                                     or IDataAndIdSelectClause))
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
