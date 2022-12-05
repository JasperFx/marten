using Marten.Internal;
using Marten.Linq.Fields;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

internal class IdSelectorStatement: Statement
{
    public IdSelectorStatement(IMartenSession session, IFieldMapping fields, Statement parent): base(fields)
    {
        parent.InsertAfter(this);

        ConvertToCommonTableExpression(session);

        // Important when doing n-deep sub-collection querying
        // And you need to remember the original FromObject
        // of the original parent rather than looking at Previous.ExportName
        // in the configure() method
        FromObject = parent.ExportName;
    }

    protected override bool IsSubQuery => true;

    protected override void configure(CommandBuilder sql)
    {
        startCommonTableExpression(sql);
        sql.Append("select ctid, data from ");
        sql.Append(FromObject);
        sql.Append(" as d");
        writeWhereClause(sql);
        endCommonTableExpression(sql);
    }
}

internal class WhereCtIdInSubQuery: ISqlFragment, IReversibleWhereFragment
{
    private readonly string _tableName;

    internal WhereCtIdInSubQuery(string tableName, SubQueryStatement subQueryStatement)
    {
        _tableName = tableName;
        SubQueryStatement = subQueryStatement;
    }

    public SubQueryStatement SubQueryStatement { get; }

    /// <summary>
    ///     Psych! Should there be a NOT in front of the sub query
    /// </summary>
    public bool Not { get; set; }

    public ISqlFragment Reverse()
    {
        Not = !Not;
        return this;
    }

    public void Apply(CommandBuilder builder)
    {
        if (Not)
        {
            builder.Append("NOT(");
        }

        builder.Append("d.ctid in (select ctid from ");
        builder.Append(_tableName);
        builder.Append(")");

        if (Not)
        {
            builder.Append(")");
        }
    }

    public bool Contains(string sqlText)
    {
        return false;
    }
}
