#nullable enable

using System;
using System.Diagnostics;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration;
using Marten.Linq.SqlGeneration.Filters;

namespace Marten.Linq;

public partial class CollectionUsage
{
    private bool _hasCompiledMany;

    public Statement BuildTopStatement(IMartenSession session, IQueryableMemberCollection collection,
        IDocumentStorage storage)
    {
        var statement = new SelectorStatement
        {
            SelectClause = storage, Limit = _limit, Offset = _offset, IsDistinct = IsDistinct
        };

        foreach (var ordering in OrderingExpressions)
            statement.Ordering.Expressions.Add(ordering.BuildExpression(collection));

        statement.ParseWhereClause(WhereExpressions, session, collection, storage);

        if (SelectExpression != null)
        {
            var visitor = new SelectorVisitor(statement, collection, _options.Serializer());
            visitor.Visit(SelectExpression);

            if (Inner != null)
            {
                IsAny = IsAny || Inner.IsAny;
                SingleValueMode ??= Inner.SingleValueMode;
                IsDistinct = Inner.IsDistinct;
                statement.Limit ??= Inner._limit;
                statement.Offset ??= Inner._offset;
            }
        }

        ProcessSingleValueModeIfAny(statement, session);

        compileNext(session, collection, statement);

        if (IsDistinct)
        {
            statement.ApplySqlOperator("DISTINCT");
        }

        return statement.Top();
    }


    public Statement BuildStatement(IMartenSession session, IQueryableMemberCollection collection,
        ISelectClause selectClause)
    {
        var statement = new SelectorStatement
        {
            SelectClause = selectClause ?? throw new ArgumentNullException(nameof(selectClause))
        };

        ConfigureStatement(session, collection, statement);

        if (IsDistinct)
        {
            statement.ApplySqlOperator("DISTINCT");
        }

        return statement;
    }

    internal Statement ConfigureStatement(IMartenSession session, IQueryableMemberCollection collection,
        SelectorStatement statement)
    {
        statement.Limit = _limit;
        statement.Offset = _offset;
        statement.IsDistinct = IsDistinct;

        foreach (var ordering in OrderingExpressions)
            statement.Ordering.Expressions.Add(ordering.BuildExpression(collection));

        statement.ParseWhereClause(WhereExpressions, session, collection);

        if (SelectExpression != null)
        {
            var visitor = new SelectorVisitor(statement, collection, _options.Serializer());
            visitor.Visit(SelectExpression);

            if (Inner != null)
            {
                IsAny = IsAny || Inner.IsAny;
                SingleValueMode ??= Inner.SingleValueMode;
                IsDistinct = Inner.IsDistinct;
                statement.Limit ??= Inner._limit;
                statement.Offset ??= Inner._offset;
            }
        }

        ProcessSingleValueModeIfAny(statement, session);

        compileNext(session, collection, statement);

        return statement.Top();
    }


    private void compileNext(IMartenSession session, IQueryableMemberCollection collection,
        SelectorStatement statement)
    {
        if (SelectMany != null)
        {
            var selection = statement.SelectorStatement();
            var collectionMember = (ICollectionMember)collection.MemberFor(SelectMany);

            // You might now already have another collection usage if the statement ends with
            // SelectMany()

            if (Inner == null)
            {
                if (SingleValueMode == Marten.Linq.Parsing.SingleValueMode.Any)
                {
                    var filter = new CollectionIsNotEmpty(collectionMember);
                    selection.Wheres.Add(filter);
                    selection.SelectClause = new AnySelectClause(selection.SelectClause.FromObject);
                }
                else if (SingleValueMode == Marten.Linq.Parsing.SingleValueMode.Count)
                {
                    selection.SelectClause = new NewScalarSelectClause<int>(
                        $"jsonb_array_length({collectionMember.JSONBLocator})", selection.SelectClause.FromObject);

                    selection.ApplyAggregateOperator("SUM");
                }
                else
                {
                    var next = new CollectionUsage(_options, collectionMember.MemberType);
                    next.CompileSelectMany(session, this, selection, collectionMember);
                }
            }
            else
            {
                Inner.CompileSelectMany(session, this, selection, collectionMember);
            }
        }
        else
        {
            Inner?.CompileAsChild(this, statement);
        }
    }

    public void CompileSelectMany(IMartenSession session, CollectionUsage parent,
        SelectorStatement parentStatement, ICollectionMember collectionMember)
    {
        if (_hasCompiledMany)
        {
            return;
        }

        _hasCompiledMany = true;

        parentStatement.Mode = StatementMode.CommonTableExpression;
        parentStatement.ExportName = session.NextTempTableName() + "CTE";

        parentStatement.SelectClause =
            collectionMember.BuildSelectClauseForExplosion(parentStatement.SelectClause.FromObject);


        // THINK THIS IS TOO SOON. MUCH OF THE LOGIC NEEDS TO GO IN THIS INSTEAD!!!
        var childStatement = collectionMember.BuildSelectManyStatement(this, session, parentStatement);

        if (IsDistinct)
        {
            if (childStatement.SelectClause is IScalarSelectClause c)
            {
                c.ApplyOperator("DISTINCT");
                parentStatement.AddToEnd(childStatement.Top());
            }
            else if (childStatement.SelectClause is ICountClause count)
            {
                if (collectionMember is IQueryableMemberCollection members)
                {
                    // It places itself at the back in this constructor function
                    var distinct = new DistinctSelectionStatement(parentStatement, count, session);
                    compileNext(session, members, distinct.SelectorStatement());
                }
                else
                {
                    throw new BadLinqExpressionException("See https://github.com/JasperFx/marten/issues/2704");
                }



                return;
            }
        }
        else
        {
            parentStatement.AddToEnd(childStatement.Top());
        }

        compileNext(session, collectionMember as IQueryableMemberCollection, childStatement);
    }

    public void CompileAsChild(CollectionUsage parent, SelectorStatement parentStatement)
    {
        if (ElementType.IsSimple() || ElementType == typeof(Guid) || ElementType == typeof(Guid?))
        {
            if (IsDistinct)
            {
                parent.IsDistinct = IsDistinct;
            }
        }
    }

    internal void ProcessSingleValueModeIfAny(SelectorStatement statement, IMartenSession session)
    {
        if (IsAny || SingleValueMode == Marten.Linq.Parsing.SingleValueMode.Any)
        {
            statement.SelectClause = new AnySelectClause(statement.SelectClause.FromObject);
            statement.Limit = 1;
            return;
        }

        if (SingleValueMode.HasValue)
        {
            switch (SingleValueMode)
            {
                case Marten.Linq.Parsing.SingleValueMode.First:
                    statement.SingleValue = true;
                    statement.CanBeMultiples = true;
                    statement.ReturnDefaultWhenEmpty = false;
                    statement.Limit ??= 1;
                    break;

                case Marten.Linq.Parsing.SingleValueMode.FirstOrDefault:
                    statement.SingleValue = true;
                    statement.CanBeMultiples = true;
                    statement.ReturnDefaultWhenEmpty = true;
                    statement.Limit ??= 1;
                    break;

                case Marten.Linq.Parsing.SingleValueMode.Single:
                    statement.SingleValue = true;
                    statement.CanBeMultiples = false;
                    statement.ReturnDefaultWhenEmpty = false;
                    statement.Limit ??= 2;
                    break;

                case Marten.Linq.Parsing.SingleValueMode.SingleOrDefault:
                    statement.SingleValue = true;
                    statement.CanBeMultiples = false;
                    statement.ReturnDefaultWhenEmpty = true;
                    statement.Limit ??= 2;
                    break;

                case Marten.Linq.Parsing.SingleValueMode.Count:
                    // Invalid to be using OrderBy() while also using Count() in
                    // PostgreSQL. Thank you Hot Chocolate.
                    statement.Ordering.Expressions.Clear();

                    if (statement.IsDistinct)
                    {
                        statement.ConvertToCommonTableExpression(session);
                        var count = new SelectorStatement
                        {
                            SelectClause = new CountClause<int>(statement.ExportName)
                        };

                        statement.AddToEnd(count);
                    }

                    statement.SelectClause = new CountClause<int>(statement.SelectClause.FromObject);

                    break;

                case Marten.Linq.Parsing.SingleValueMode.LongCount:
                    // Invalid to be using OrderBy() while also using Count() in
                    // PostgreSQL. Thank you Hot Chocolate.
                    statement.Ordering.Expressions.Clear();

                    if (statement.IsDistinct)
                    {
                        statement.ConvertToCommonTableExpression(session);
                        var count = new SelectorStatement
                        {
                            SelectClause = new CountClause<long>(statement.ExportName)
                        };

                        statement.AddToEnd(count);
                    }

                    statement.SelectClause = new CountClause<long>(statement.SelectClause.FromObject);
                    break;

                case Marten.Linq.Parsing.SingleValueMode.Average:
                    statement.ApplyAggregateOperator("AVG");
                    break;

                case Marten.Linq.Parsing.SingleValueMode.Max:
                    statement.ApplyAggregateOperator("MAX");
                    break;

                case Marten.Linq.Parsing.SingleValueMode.Min:
                    statement.ApplyAggregateOperator("MIN");
                    break;

                case Marten.Linq.Parsing.SingleValueMode.Sum:
                    statement.ApplyAggregateOperator("SUM");
                    break;

                default:
                    throw new NotImplementedException($"Whoa pardner, don't have this {SingleValueMode} yet!");
            }
        }
    }
}
