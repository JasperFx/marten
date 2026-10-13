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

        // This is ugly, but you need to pick up the id column *just* in case there's a Select()
        // clause that needs it.
        if (selectorStatement.SelectClause is IQueryOnlyDocumentStorage s)
        {
            selectorStatement.SelectClause = s.SelectClauseForIncludes();
        }
        else if (selectorStatement.SelectClause is DuplicatedFieldSelectClause duplicated)
        {
            // An include on a document with duplicated fields selects through this clause instead,
            // which a query session builds without the id column. An include joined on the queried
            // document's own id reads "d.id" from the temp table, so the id has to be there too.
            duplicated.EnsureColumn("d.id");
        }

        ExportName = session.NextTempTableName();
    }

    public Statement Inner { get; }

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
