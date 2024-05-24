using Marten.Internal;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

/// <summary>
///     This statement explodes a collection element to a series
///     of rows
/// </summary>
internal class ExplodeCollectionStatement: Statement
{
    private readonly string _locator;
    private readonly string _sourceTable;

    public ExplodeCollectionStatement(Statement parent, string locator, IMartenSession session)
    {
        _locator = locator;
        _sourceTable = parent.ExportName;
        parent.InsertAfter(this);
        ConvertToCommonTableExpression(session);
    }

    public ExplodeCollectionStatement(IMartenSession session, SelectorStatement selectorStatement,
        string locator)
    {
        _locator = locator;
        _sourceTable = selectorStatement.SelectClause.FromObject;
        ConvertToCommonTableExpression(session);

        selectorStatement.InsertBefore(this);
    }

    public ISqlFragment? Where { get; set; }

    protected override void configure(ICommandBuilder sql)
    {
        startCommonTableExpression(sql);

        sql.Append("select ctid, ");
        sql.Append(_locator);
        sql.Append(" as data from ");

        sql.Append(_sourceTable);

        if (Where != null)
        {
            sql.Append(" as d WHERE ");
            Where.Apply(sql);

            endCommonTableExpression(sql);
        }
        else
        {
            endCommonTableExpression(sql, " as d");
        }
    }
}
