using Marten.Internal;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;

namespace Marten.Linq.Includes;

public class TemporaryTableStatement: Statement
{
    public TemporaryTableStatement(Statement inner, IMartenSession session)
    {
        Inner = inner;

        Inner.SelectorStatement().Mode = StatementMode.Inner;

        ExportName = session.NextTempTableName();
    }

    public Statement Inner { get; }

    protected override void configure(ICommandBuilder sql)
    {
        sql.Append("drop table if exists ");
        sql.Append(ExportName);
        sql.StartNewCommand();
        sql.Append("create temp table ");
        sql.Append(ExportName);
        sql.Append(" as (");
        Inner.Apply(sql);
        sql.Append(");");
    }
}
