#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using JasperFx.Core;

namespace Marten.Linq.Parsing.Operators;

internal class OperatorLibrary
{
    private ImHashMap<string, LinqOperator> _operators = ImHashMap<string, LinqOperator>.Empty;

    public OperatorLibrary()
    {
        Add<TakeOperator>();
        Add<SkipOperator>();
        Add<WhereOperator>();

        Add<LastOperator>();
        Add<LastOrDefaultOperator>();

        AddOrdering(nameof(QueryableExtensions.OrderBy), OrderingDirection.Asc);
        AddOrdering(nameof(QueryableExtensions.ThenBy), OrderingDirection.Asc);

        AddOrdering(nameof(QueryableExtensions.OrderByDescending), OrderingDirection.Desc);
        AddOrdering(nameof(QueryableExtensions.ThenByDescending), OrderingDirection.Desc);

        Add<SelectManyOperator>();
        Add<SelectOperator>();
        Add<AnyOperator>();
        Add<DistinctOperator>();
        Add<IncludeOperator>(); // TODO -- is this necessary?
        Add<IncludePlanOperator>();

        Add<OrderBySqlOperator>();
        Add<ThenBySqlOperator>();

        foreach (var mode in Enum.GetValues<SingleValueMode>()) addSingleValueMode(mode);
    }

    public void Add<T>() where T : LinqOperator, new()
    {
        var op = new T();
        _operators = _operators.AddOrUpdate(op.MethodName, op);
    }

    private void addSingleValueMode(SingleValueMode mode)
    {
        var op = new SingleValueOperator(mode);
        _operators = _operators.AddOrUpdate(mode.ToString(), op);
    }

    public void AddOrdering(string methodName, OrderingDirection direction)
    {
        _operators = _operators.AddOrUpdate(methodName, new OrderingOperator(methodName, direction));
    }

    public bool TryFind(string methodName, [NotNullWhen(true)]out LinqOperator? op)
    {
        return _operators.TryFind(methodName, out op);
    }
}
