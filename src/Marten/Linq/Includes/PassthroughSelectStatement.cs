using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
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

            // #5654: a Select() projection has to be re-applied against the temp table -- unlike the
            // branch above it cannot just read the row back. Its statistics column must not be
            // re-applied with it. The temp table already carries the count, computed over the whole
            // match set because PostgreSQL evaluates a window function before LIMIT; emitting
            // count(*) over() a second time here counts the rows the inner statement's Take() left,
            // so a paged projection reported the size of its own page as the total.
            if (SelectClause is IStatsSelectClause projected)
            {
                sql.Append("select ");
                projected.Inner.ApplySelectFields(sql);
                sql.Append(", d.");
                sql.Append(LinqConstants.StatsColumnName);
                sql.Append(" from ");
                sql.Append(TableName);
                sql.Append(" as d");

                return;
            }

            SelectClause.Apply(sql);
        }


    }
}
