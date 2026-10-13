#nullable enable
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;

namespace Marten.Linq.Includes;

public class TemporaryTableStatement: Statement
{
    public TemporaryTableStatement(Statement inner, IStorageSession session)
    {
        Inner = inner;
        var selectorStatement = Inner.SelectorStatement();
        selectorStatement.Mode = StatementMode.Inner;

        // #5653: Stats() wraps the real select clause, so look *through* the wrapper rather than
        // past it. Neither case below matches a StatsSelectClause, so without this the temp table
        // for a statistics query was created with no id at all. The statistics column is read back
        // at Inner.SelectFields().Length, so it follows the added id column by itself.
        if (selectorStatement.SelectClause is IStatsSelectClause stats)
        {
            stats.Inner = withIdColumn(stats.Inner);
        }
        else
        {
            selectorStatement.SelectClause = withIdColumn(selectorStatement.SelectClause);
        }

        ExportName = session.NextTempTableName();
    }

    public Statement Inner { get; }

    /// <summary>
    /// This is ugly, but you need to pick up the id column *just* in case there's a Select()
    /// clause that needs it, or an Include() joined on the queried document's own id, which
    /// reads "d.id" straight out of the temp table.
    /// </summary>
    private static ISelectClause withIdColumn(ISelectClause selectClause)
    {
        switch (selectClause)
        {
            case IQueryOnlyDocumentStorage s:
                return s.SelectClauseForIncludes();

            // #5650: with duplicated fields the query selects through this clause instead, which a
            // query session builds from the same id-less column list. In the other session kinds
            // the id is already selected and EnsureColumn does nothing.
            case DuplicatedFieldSelectClause duplicated:
                duplicated.EnsureColumn("d.id");
                return duplicated;

            default:
                return selectClause;
        }
    }

    protected override void configure(ICommandBuilder sql)
    {
        sql.Append("drop table if exists ");
        sql.Append(ExportName);
        sql.Append("; ");
        sql.StartNewCommand();
        sql.Append("create temp table ");
        sql.Append(ExportName);
        sql.Append(" as (");
        Inner.Apply(sql);
        sql.Append(");");
    }
}
