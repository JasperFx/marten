using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Marten.Linq.Parsing.Operators;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq;

public partial class CollectionUsage
{
    public List<Ordering> OrderingExpressions { get; } = new();
    public List<Expression> WhereExpressions { get; } = new();
    public Expression? SelectExpression { get; set; }

    public void AddWhereClause(MethodCallExpression expression)
    {
        if (expression.Arguments.Count == 1)
        {
            return;
        }

        var where = expression.Arguments[1];
        if (where is UnaryExpression e)
        {
            where = e.Operand;
        }

        if (where is LambdaExpression l)
        {
            where = l.Body;
        }

        WhereExpressions.Add(where);
    }

    public void AddSelectClause(MethodCallExpression expression)
    {
        var select = expression.Arguments[1];
        if (select is UnaryExpression e)
        {
            select = e.Operand;
        }

        if (select is LambdaExpression l)
        {
            select = l.Body;
        }

        SelectExpression = select;
    }
}
