using System;
using System.Linq.Expressions;
using JasperFx.Core;
using JasperFx.Core.Reflection;

namespace Marten.Linq.Parsing.Operators;

public class WhereOperator: LinqOperator
{
    private readonly Cache<Type, object> _always
        = new(type => typeof(FuncBuilder<>).CloseAndBuildAs<IFuncBuilder>(type).Build());


    public WhereOperator(): base("Where")
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        var usage = query.CollectionUsageFor(expression);
        usage.AddWhereClause(expression);
    }

    private interface IFuncBuilder
    {
        object Build();
    }

    private class FuncBuilder<T>: IFuncBuilder
    {
        public object Build()
        {
            Expression<Func<T, bool>> filter = _ => true;
            return filter;
        }
    }
}
