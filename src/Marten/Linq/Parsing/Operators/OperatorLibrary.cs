#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using ImTools;
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
        Add<GroupJoinOperator>();

        // #5731: refused, each with the GroupJoin() rewrite that answers it. LeftJoin/RightJoin are
        // .NET 10 operators and FullJoin a .NET 11 one; matching on the name alone keeps this
        // independent of the target framework.
        addRefusal(new UnsupportedJoinOperator("Join"));
        addRefusal(new UnsupportedJoinOperator("LeftJoin"));
        addRefusal(new UnsupportedJoinOperator("RightJoin"));
        addRefusal(new UnsupportedJoinOperator("FullJoin"));
        Add<SelectOperator>();
        Add<GroupByOperator>();
        Add<AnyOperator>();
        Add<DistinctOperator>();
        Add<DistinctByOperator>();
        Add<IncludeOperator>(); // TODO -- is this necessary?
        Add<IncludePlanOperator>();

        Add<OrderBySqlOperator>();
        Add<ThenBySqlOperator>();
        Add<OrderByNgramRankOperator>();
        Add<OrderByTextRankOperator>();
        Add<ThenByTextRankOperator>();

        foreach (var mode in Enum.GetValues<SingleValueMode>()) addSingleValueMode(mode);
    }

    public void Add<T>() where T : LinqOperator, new()
    {
        var op = new T();
        _operators = _operators.AddOrUpdate(op.MethodName, op);
    }

    private void addRefusal(LinqOperator op)
    {
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
