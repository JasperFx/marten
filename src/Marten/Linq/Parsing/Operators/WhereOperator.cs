#nullable enable
using System;
using System.Linq.Expressions;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Linq.Parsing.Operators;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
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
