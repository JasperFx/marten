using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Linq.Parsing.Operators;

namespace Marten.Linq.Parsing;

internal partial class LinqQueryParser: ExpressionVisitor, ILinqQuery
{
    // TODO -- inject somehow later. See https://github.com/JasperFx/marten/issues/2709
    private static readonly OperatorLibrary _operators = new();
    private readonly List<CollectionUsage> _collectionUsages = new();

    private readonly MartenLinqQueryProvider _provider;


    private bool _hasParsedIncludes;

    public LinqQueryParser(MartenLinqQueryProvider provider, IMartenSession session,
        Expression expression, SingleValueMode? valueMode = null)
    {
        ValueMode = valueMode;
        _provider = provider;
        Session = session;

        Visit(expression);
    }

    public SingleValueMode? ValueMode { get; }

    public IMartenSession Session { get; }

    public CollectionUsage CollectionUsageFor(MethodCallExpression expression)
    {
        var argument = expression.Arguments.First();
        return CollectionUsageForArgument(argument);
    }

    public CollectionUsage CollectionUsageForArgument(Expression argument)
    {
        var elementType = argument.Type.GetGenericArguments()[0];
        if (CurrentUsage == null || CurrentUsage.ElementType != elementType)
        {
            CurrentUsage = new CollectionUsage(Session.Options, elementType);
            _collectionUsages.Insert(0, CurrentUsage);

            return CurrentUsage;
        }

        return CurrentUsage;
    }

    public CollectionUsage StartNewCollectionUsageFor(MethodCallExpression expression)
    {
        var elementType = expression.Arguments[0].Type.GetGenericArguments()[0];
        CurrentUsage = new CollectionUsage(Session.Options, elementType);
        _collectionUsages.Insert(0, CurrentUsage);

        return CurrentUsage;
    }

    public CollectionUsage CollectionUsageFor(Type elementType)
    {
        if (CurrentUsage == null || CurrentUsage.ElementType != elementType)
        {
            CurrentUsage = new CollectionUsage(Session.Options, elementType);
            _collectionUsages.Insert(0, CurrentUsage);

            return CurrentUsage;
        }

        return CurrentUsage;
    }

    public CollectionUsage CurrentUsage { get; private set; }

    public IEnumerable<Type> DocumentTypes()
    {
        if (_collectionUsages.Any())
        {
            yield return _collectionUsages[0].ElementType;
        }

        foreach (var plan in _provider.AllIncludes) yield return plan.DocumentType;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (_operators.TryFind(node.Method.Name, out var op))
        {
            op.Apply(this, node);

            // Do NOT visit deep into the inner expression of a WHERE or ORDER BY
            return Visit(node.Arguments[0]);
        }

        throw new BadLinqExpressionException($"Marten does not (yet) support Linq operator '{node.Method.Name}'");
    }
}
