using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;

namespace Marten.Linq.Includes;

internal class PassthroughSelectStatement: SelectorStatement
{
    private readonly ISelectClause _innerSelectClause;

    public PassthroughSelectStatement(string tableName, ISelectClause innerSelectClause)
    {
        _innerSelectClause = innerSelectClause;
        SelectClause = _innerSelectClause;
        TableName = tableName;
    }

    public string TableName { get; set; }

    protected override void configure(CommandBuilder sql)
    {
        sql.Append("select * from ");
        sql.Append(TableName);
        sql.Append(" as d;");
    }
}
