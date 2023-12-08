using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Includes;
using Marten.Linq.Members;
using Marten.Linq.Parsing.Operators;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing;

internal class LinqQueryParser: ExpressionVisitor, ILinqQuery
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

    public IQueryHandler<IReadOnlyList<T>> BuildListHandler<T>()
    {
        if (!_collectionUsages.Any())
        {
            var usage = new CollectionUsage(Session.Options, _provider.SourceType);
            _collectionUsages.Insert(0, usage);
        }

        var statements = BuildStatements();

        var handler =
            statements.MainSelector.SelectClause.BuildHandler<IReadOnlyList<T>>(Session, statements.Top,
                statements.MainSelector);

        if (_provider.AllIncludes.Any())
        {
            return new IncludeQueryHandler<IReadOnlyList<T>>(handler,
                _provider.AllIncludes.Select(x => x.BuildReader(Session)).ToArray());
        }

        return handler;
    }

    public IQueryHandler<TResult> BuildHandler<TResult>()
    {
        if (!_collectionUsages.Any())
        {
            var usage = new CollectionUsage(Session.Options, _provider.SourceType);
            _collectionUsages.Insert(0, usage);
        }

        var statements = BuildStatements();

        var handler = buildHandlerForCurrentStatement<TResult>(statements.Top, statements.MainSelector);

        if (_provider.AllIncludes.Any())
        {
            return new IncludeQueryHandler<TResult>(handler,
                _provider.AllIncludes.Select(x => x.BuildReader(Session)).ToArray());
        }

        return handler;
    }

    private IQueryHandler<TResult> buildHandlerForCurrentStatement<TResult>(Statement top, SelectorStatement selector)
    {
        if (selector.SingleValue)
        {
            return selector.BuildSingleResultHandler<TResult>(Session, top);
        }

        return selector.SelectClause.BuildHandler<TResult>(Session, top, selector);
    }

    public IEnumerable<Type> DocumentTypes()
    {
        if (_collectionUsages.Any())
        {
            yield return _collectionUsages[0].ElementType;
        }

        foreach (var plan in _provider.AllIncludes)
        {
            yield return plan.DocumentType;
        }
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

    internal StatementQuery BuildStatements()
    {
        if (!_collectionUsages.Any())
        {
            var usage = new CollectionUsage(Session.Options, _provider.SourceType);
            _collectionUsages.Insert(0, usage);
        }

        var top = _collectionUsages[0];

        for (var i = 1; i < _collectionUsages.Count; i++)
        {
            _collectionUsages[i - 1].Inner = _collectionUsages[i];
        }

        var documentStorage = Session.StorageFor(top.ElementType);
        var collection = documentStorage.QueryMembers;

        // In case the single value mode is passed through by the MartenLinqProvider
        if (ValueMode != null)
        {
            _collectionUsages.Last().SingleValueMode = ValueMode;
        }

        var statement = top.BuildTopStatement(Session, collection, documentStorage);
        var selectionStatement = statement.SelectorStatement();


        parseIncludeExpressions(top, collection);

        if (_provider.AllIncludes.Any())
        {
            var inner = statement.Top();

            if (inner is SelectorStatement { SelectClause: IDocumentStorage storage } select)
            {
                select.SelectClause = storage.SelectClauseWithDuplicatedFields;
            }

            var temp = new TemporaryTableStatement(inner, Session);
            foreach (var include in _provider.AllIncludes) include.AppendStatement(temp, Session);

            temp.AddToEnd(new PassthroughSelectStatement(temp.ExportName, selectionStatement.SelectClause));


            // Deal with query statistics
            if (_provider.Statistics != null)
            {
                selectionStatement.SelectClause = selectionStatement.SelectClause.UseStatistics(_provider.Statistics);
            }

            return new StatementQuery(selectionStatement, temp);
        }


        // Deal with query statistics
        if (_provider.Statistics != null)
        {
            selectionStatement.SelectClause = selectionStatement.SelectClause.UseStatistics(_provider.Statistics);
        }

        return new StatementQuery(selectionStatement, selectionStatement.Top());
    }

    private void parseIncludeExpressions(CollectionUsage top, IQueryableMemberCollection collection)
    {
        if (_hasParsedIncludes)
        {
            return;
        }

        _hasParsedIncludes = true;

        top.ParseIncludes(collection, Session);
        _provider.AllIncludes.AddRange(top.Includes);
    }

    public void BuildDiagnosticCommand(FetchType fetchType, CommandBuilder sql)
    {
        var statements = BuildStatements();

        switch (fetchType)
        {
            case FetchType.Any:
                statements.MainSelector.ToAny();
                break;

            case FetchType.Count:
                statements.MainSelector.ToCount<long>();
                break;

            case FetchType.FetchOne:
                statements.MainSelector.Limit = 1;
                break;
        }

        statements.Top.Apply(sql);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IQueryHandler<TResult> BuildHandler<TDocument, TResult>(ISelector<TDocument> selector,
        ISqlFragment statement)
    {
        if (typeof(TResult).CanBeCastTo<IEnumerable<TDocument>>())
        {
            return (IQueryHandler<TResult>)new ListQueryHandler<TDocument>(statement, selector);
        }

        throw new NotSupportedException("Marten does not know how to use result type " +
                                        typeof(TResult).FullNameInCode());
    }

    internal record struct StatementQuery(SelectorStatement MainSelector, Statement Top);
}
