#nullable enable
using Marten.Internal;
using Weasel.Postgresql;

namespace Marten.Linq.SqlGeneration;

internal class DistinctSelectionStatement: Statement
{
    public DistinctSelectionStatement(SelectorStatement parent, ICountClause selectClause, IMartenSession session)
    {
        parent.ConvertToCommonTableExpression(session);

        ConvertToCommonTableExpression(session);

        parent.InsertAfter(this);

        selectClause.OverrideFromObject(this);
        var selector = new SelectorStatement { SelectClause = selectClause };
        InsertAfter(selector);
    }

    protected override void configure(IPostgresqlCommandBuilder sql)
    {
        startCommonTableExpression(sql);
        sql.Append("select distinct(data) from ");
        sql.Append(Previous.ExportName);
        sql.Append(" as d");
        endCommonTableExpression(sql);
    }
}
