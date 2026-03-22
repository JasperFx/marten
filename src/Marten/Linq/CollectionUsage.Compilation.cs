#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Includes;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration;
using Marten.Linq.SqlGeneration.Filters;

namespace Marten.Linq;

public partial class CollectionUsage
{
    private bool _hasCompiledMany;

    public Statement BuildTopStatement(IMartenSession session, IQueryableMemberCollection collection,
        IDocumentStorage storage, QueryStatistics? statistics)
    {
        Statement top;

        var statement = new SelectorStatement
        {
            SelectClause = storage, Limit = _limit, Offset = _offset, IsDistinct = IsDistinct
        };

        top = statement;

        foreach (var ordering in OrderingExpressions)
        {
            statement.Ordering.Expressions.Add(ordering.BuildExpression(collection));
        }

        statement.ParseWhereClause(WhereExpressions, session, collection, storage);

        // If this is a GroupBy query, handle it separately
        if (GroupByData != null)
        {
            return CompileGroupBy(session, statement, collection, statistics);
        }

        ParseIncludes(collection, session);
        if (Includes.Any())
        {
            var inner = statement.Top();
            var selectionStatement = inner.SelectorStatement();

            ITenantFilter? tenantWhereFragment = null;
            selectionStatement.TryFindTenantAwareFilter(out tenantWhereFragment);


            if (inner is SelectorStatement { SelectClause: IDocumentStorage } select)
            {
                select.SelectClause = storage.SelectClauseWithDuplicatedFields;
            }

            // QueryStatistics has to be applied to the inner, selector statement
            if (statistics != null)
            {
                var innerSelect = inner.SelectorStatement();
                innerSelect.SelectClause = innerSelect.SelectClause.UseStatistics(statistics);
            }

            var temp = new TemporaryTableStatement(inner, session);
            foreach (var include in Includes) include.AppendStatement(temp, session, tenantWhereFragment);

            temp.AddToEnd(new PassthroughSelectStatement(temp.ExportName, selectionStatement.SelectClause));

            top = temp;
            statement = top.SelectorStatement();
        }

        if (SelectExpression != null)
        {
            var visitor = new SelectorVisitor(statement, collection, _options.Serializer());
            visitor.Visit(SelectExpression);

            if (Inner != null)
            {
                Inner.CompileAsChild(this);

                IsAny = IsAny || Inner.IsAny;
                SingleValueMode ??= Inner.SingleValueMode;
                statement.IsDistinct = IsDistinct = Inner.IsDistinct;
                statement.Limit ??= Inner._limit;
                statement.Offset ??= Inner._offset;
            }
        }

        // Deal with query statistics at the last minute
        if (statistics != null)
        {
            statement.SelectClause = statement.SelectClause.UseStatistics(statistics);
        }

        ProcessSingleValueModeIfAny(statement.SelectorStatement(), session, collection, statistics);

        statement = compileNext(session, collection, statement, statistics).SelectorStatement();

        // THIS CAN BE A PROBLEM IF IT'S DONE TOO SOON
        if (IsDistinct)
        {
            if (SelectExpression != null && OrderingExpressions.Any(x => x.IsTransformed))
            {
                throw new BadLinqExpressionException(
                    "Marten is unable to build a query with a Distinct() + Select() + a 'transformed' OrderBy(). You will have to resort to SQL for this query");
            }
        }

        return statement.Top();
    }


    public Statement BuildSelectManyStatement(IMartenSession session, IQueryableMemberCollection collection,
        ISelectClause selectClause, QueryStatistics? statistics, SelectorStatement parentStatement)
    {
        var statement = new SelectorStatement
        {
            SelectClause = selectClause ?? throw new ArgumentNullException(nameof(selectClause))
        };

        parentStatement.AddToEnd(statement);

        statement = ConfigureSelectManyStatement(session, collection, statement, statistics).SelectorStatement();

        if (IsDistinct)
        {
            statement.ApplySqlOperator("DISTINCT");
        }

        return statement;
    }

    internal Statement ConfigureSelectManyStatement(IMartenSession session, IQueryableMemberCollection collection,
        SelectorStatement statement, QueryStatistics? statistics)
    {
        Statement top = statement.Top();

        statement.Limit = _limit;
        statement.Offset = _offset;
        statement.IsDistinct = IsDistinct;

        foreach (var ordering in OrderingExpressions)
            statement.Ordering.Expressions.Add(ordering.BuildExpression(collection));

        statement.ParseWhereClause(WhereExpressions, session, collection);

        ParseIncludes(collection, session);
        if (Includes.Any())
        {
            var inner = statement.Top();
            var selectionStatement = inner.SelectorStatement();

            selectionStatement.TryFindTenantAwareFilter(out var tenantWhereFragment);

            // QueryStatistics has to be applied to the inner, selector statement
            if (statistics != null)
            {
                var innerSelect = inner.SelectorStatement();
                innerSelect.SelectClause = innerSelect.SelectClause.UseStatistics(statistics);
            }

            var temp = new TemporaryTableStatement(inner, session);
            foreach (var include in Includes) include.AppendStatement(temp, session, tenantWhereFragment);

            temp.AddToEnd(new PassthroughSelectStatement(temp.ExportName, selectionStatement.SelectClause));

            top = temp;
            statement = top.SelectorStatement();
        }

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

        ProcessSingleValueModeIfAny(statement, session, collection, statistics);

        compileNext(session, collection, statement, statistics);

        return top;
    }


    private Statement compileNext(IMartenSession session, IQueryableMemberCollection collection,
        SelectorStatement statement, QueryStatistics? statistics)
    {
        if (GroupJoinData != null)
        {
            return CompileGroupJoin(session, statement, statistics);
        }

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
                    var filter = collectionMember.NotEmpty;
                    selection.Wheres.Add(filter);
                    selection.SelectClause = new AnySelectClause(selection.SelectClause.FromObject);
                }
                else if (SingleValueMode == Marten.Linq.Parsing.SingleValueMode.Count)
                {
                    selection.SelectClause = new NewScalarSelectClause<int>(
                        $"jsonb_array_length({collectionMember.JSONBLocator})", selection.SelectClause.FromObject);

                    selection.ApplyAggregateOperator("SUM");
                }
                else if (SingleValueMode == Marten.Linq.Parsing.SingleValueMode.LongCount)
                {
                    selection.SelectClause = new NewScalarSelectClause<long>(
                        $"jsonb_array_length({collectionMember.JSONBLocator})", selection.SelectClause.FromObject);

                    selection.ApplyAggregateOperator("SUM");
                }
                else
                {
                    var next = new CollectionUsage(_options, collectionMember.MemberType);
                    return next.CompileSelectMany(session, selection, collectionMember, statistics);
                }
            }
            else
            {
                return Inner.CompileSelectMany(session, selection, collectionMember, statistics);
            }
        }
        else
        {
            Inner?.CompileAsChild(this);
        }

        return statement;
    }

    public Statement CompileGroupJoin(IMartenSession session,
        SelectorStatement outerStatement, QueryStatistics? statistics)
    {
        var groupJoin = GroupJoinData!;

        // If there's no flattened result selector, this is GroupJoin as a final operator (Pattern 3)
        if (groupJoin.FlattenedResultSelector == null)
        {
            throw new NotSupportedException(
                "Marten does not yet support GroupJoin as a final operator with collection materialization. " +
                "Use GroupJoin + SelectMany for INNER JOIN, or GroupJoin + SelectMany + DefaultIfEmpty for LEFT JOIN.");
        }

        // 1. Convert the outer statement to a CTE
        var outerStorage = session.StorageFor(ElementType);
        var outerCollection = outerStorage.QueryMembers;

        outerStatement.Mode = StatementMode.CommonTableExpression;
        outerStatement.ExportName = session.NextTempTableName() + "CTE";
        var outerCteAlias = outerStatement.ExportName;

        // Use SelectClauseWithDuplicatedFields so duplicated columns are available in the CTE
        outerStatement.SelectClause = outerStorage.SelectClauseWithDuplicatedFields;

        // 2. Create the inner CTE
        var innerStorage = session.StorageFor(groupJoin.InnerElementType);
        var innerCollection = innerStorage.QueryMembers;

        var innerStatement = new SelectorStatement
        {
            // Use SelectClauseWithDuplicatedFields so duplicated columns are available in the CTE
            SelectClause = innerStorage.SelectClauseWithDuplicatedFields,
            Mode = StatementMode.CommonTableExpression,
            ExportName = session.NextTempTableName() + "CTE"
        };
        var innerCteAlias = innerStatement.ExportName;

        // Apply default filters (soft delete, tenancy) to inner CTE
        innerStatement.ParseWhereClause(
            Array.Empty<System.Linq.Expressions.Expression>(),
            session, innerCollection, innerStorage);

        // Chain the inner CTE after the outer CTE
        outerStatement.InsertAfter(innerStatement);

        var outerKeyMember = outerCollection.MemberFor(groupJoin.OuterKeySelector.Body);
        var innerKeyMember = innerCollection.MemberFor(groupJoin.InnerKeySelector.Body);

        // Ensure join key columns are present in CTE SELECT lists (they may be
        // excluded when using QueryOnly storage, e.g. d.id is omitted by IdColumn)
        EnsureJoinKeyInCte(outerStatement, outerStorage, outerKeyMember.TypedLocator);
        EnsureJoinKeyInCte(innerStatement, innerStorage, innerKeyMember.TypedLocator);

        // Replace d. prefix with CTE aliases for the ON clause
        var outerKeyLocator = outerKeyMember.TypedLocator.Replace("d.", outerCteAlias + ".");
        var innerKeyLocator = innerKeyMember.TypedLocator.Replace("d.", innerCteAlias + ".");

        // 4. Build the result projection
        var joinParser = new JoinSelectParser(
            _options.Serializer(),
            outerCollection,
            innerCollection,
            outerCteAlias,
            innerCteAlias,
            groupJoin.ResultSelector,
            groupJoin.FlattenedResultSelector);

        // 5. Create the JoinSelectClause and final SelectorStatement
        var resultType = groupJoin.FlattenedResultSelector.ReturnType;
        var closedJoinSelectType = typeof(JoinSelectClause<>).MakeGenericType(resultType);
        var joinSelectClause = (ISelectClause)Activator.CreateInstance(
            closedJoinSelectType,
            joinParser.NewObject,
            outerCteAlias,
            innerCteAlias,
            groupJoin.IsLeftJoin,
            outerKeyLocator,
            innerKeyLocator)!;

        var joinStatement = new SelectorStatement
        {
            SelectClause = joinSelectClause
        };

        // Chain the join statement after the inner CTE
        innerStatement.InsertAfter(joinStatement);

        // 6. Apply downstream operators (Where, OrderBy, SingleValueMode, etc.)
        // from the Inner usage (which was the SelectMany usage)
        if (Inner != null)
        {
            // Transfer SingleValueMode
            if (Inner.SingleValueMode.HasValue)
            {
                SingleValueMode = Inner.SingleValueMode;
            }

            if (Inner.IsAny)
            {
                IsAny = true;
            }

            // Transfer Limit/Offset
            joinStatement.Limit = Inner._limit;
            joinStatement.Offset = Inner._offset;

            // Check if there's a deeper Inner with operators
            if (Inner.Inner != null)
            {
                var deepInner = Inner.Inner;
                if (deepInner.SingleValueMode.HasValue)
                {
                    SingleValueMode = deepInner.SingleValueMode;
                }

                if (deepInner.IsAny)
                {
                    IsAny = true;
                }

                joinStatement.Limit ??= deepInner._limit;
                joinStatement.Offset ??= deepInner._offset;
            }
        }

        // Apply single value mode to the join statement
        ProcessSingleValueModeIfAny(joinStatement, session, null, statistics);

        return joinStatement;
    }

    public Statement CompileGroupBy(IMartenSession session,
        SelectorStatement statement, IQueryableMemberCollection collection, QueryStatistics? statistics)
    {
        var groupBy = GroupByData!;
        var groupingUsage = Inner; // The IGrouping<K,T> usage with SelectExpression and WhereExpressions

        if (groupingUsage?.SelectExpression == null)
        {
            throw new BadLinqExpressionException(
                "GroupBy must be followed by a Select() projection. Marten does not support returning IGrouping<K,T> directly.");
        }

        // Find the grouping parameter from the select expression
        // The SelectExpression is the body of the lambda; we need the original lambda's parameter
        // The grouping parameter was on the Select's lambda: g => new { ... }
        // We need to find it from the expression tree
        ParameterExpression groupingParam = FindGroupingParameter(groupingUsage.SelectExpression);

        var parser = new GroupBySelectParser(
            _options.Serializer(),
            collection,
            groupBy.KeySelector,
            groupingUsage.SelectExpression,
            groupingParam);

        // Set GROUP BY columns
        foreach (var col in parser.GroupByColumns)
        {
            statement.GroupByColumns.Add(col);
        }

        // Set SELECT clause
        if (parser.IsScalar)
        {
            var fragment = parser.ScalarFragment;
            if (fragment is IQueryableMember member)
            {
                if (member.MemberType == typeof(string))
                {
                    statement.SelectClause =
                        new NewScalarStringSelectClause(member.RawLocator, statement.SelectClause.FromObject);
                }
                else if (member.MemberType.IsSimple() || member.MemberType == typeof(Guid) ||
                         member.MemberType == typeof(decimal) || member.MemberType == typeof(DateTimeOffset))
                {
                    statement.SelectClause =
                        typeof(NewScalarSelectClause<>).CloseAndBuildAs<ISelectClause>(member,
                            statement.SelectClause.FromObject,
                            member.MemberType);
                }
            }
            else if (fragment is LiteralSql literal)
            {
                // Aggregate scalar like count(*)
                statement.SelectClause =
                    new NewScalarSelectClause<int>(literal.Text, statement.SelectClause.FromObject);
            }
        }
        else
        {
            var resultType = groupingUsage.SelectExpression.Type;
            statement.SelectClause =
                typeof(SelectDataSelectClause<>).CloseAndBuildAs<ISelectClause>(
                    statement.SelectClause.FromObject,
                    parser.NewObject,
                    resultType);
        }

        // Process HAVING from the grouping usage's WhereExpressions
        if (groupingUsage.WhereExpressions.Any())
        {
            // Build key member dictionaries for the HAVING resolver
            var keyMembers = new Dictionary<string, IQueryableMember>();
            IQueryableMember simpleKeyMember = null;
            bool isCompositeKey;

            var keyBody = groupBy.KeySelector.Body;
            if (keyBody is NewExpression newExpr)
            {
                isCompositeKey = true;
                var parameters = newExpr.Constructor!.GetParameters();
                for (var i = 0; i < parameters.Length; i++)
                {
                    keyMembers[parameters[i].Name!] = collection.MemberFor(newExpr.Arguments[i]);
                }
            }
            else
            {
                isCompositeKey = false;
                simpleKeyMember = collection.MemberFor(keyBody);
            }

            foreach (var whereExpr in groupingUsage.WhereExpressions)
            {
                var havingFragment = GroupBySelectParser.ResolveHavingFragment(
                    whereExpr, collection, groupBy.KeySelector, keyMembers, simpleKeyMember, isCompositeKey);
                statement.HavingClauses.Add(havingFragment);
            }
        }

        // Transfer downstream operators from the grouping usage's Inner (if any)
        // e.g., OrderBy, Take, Skip after Select
        var downstream = groupingUsage.Inner;
        if (downstream != null)
        {
            statement.Limit ??= downstream._limit;
            statement.Offset ??= downstream._offset;

            if (downstream.SingleValueMode.HasValue)
            {
                SingleValueMode = downstream.SingleValueMode;
            }

            if (downstream.IsAny)
            {
                IsAny = true;
            }
        }

        // Apply single value mode (Count, First, etc. after the GroupBy+Select)
        ProcessSingleValueModeIfAny(statement, session, collection, statistics);

        return statement.Top();
    }

    private static ParameterExpression FindGroupingParameter(Expression expression)
    {
        var finder = new GroupingParameterFinder();
        finder.Visit(expression);
        return finder.Parameter ?? throw new BadLinqExpressionException(
            "Could not find the IGrouping parameter in the GroupBy Select expression");
    }

    private class GroupingParameterFinder: ExpressionVisitor
    {
        public ParameterExpression? Parameter { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (Parameter == null && node.Type.IsGenericType &&
                node.Type.GetGenericTypeDefinition() == typeof(IGrouping<,>))
            {
                Parameter = node;
            }

            return base.VisitParameter(node);
        }
    }

    public Statement CompileSelectMany(IMartenSession session,
        SelectorStatement parentStatement, ICollectionMember collectionMember, QueryStatistics? statistics)
    {
        if (_hasCompiledMany)
        {
            return parentStatement;
        }

        _hasCompiledMany = true;

        parentStatement.Mode = StatementMode.CommonTableExpression;
        parentStatement.ExportName = session.NextTempTableName() + "CTE";

        parentStatement.SelectClause =
            collectionMember.BuildSelectClauseForExplosion(parentStatement.SelectClause.FromObject);


        // THINK THIS IS TOO SOON. MUCH OF THE LOGIC NEEDS TO GO IN THIS INSTEAD!!!
        var childStatement = collectionMember.AttachSelectManyStatement(this, session, parentStatement, statistics);
        var childSelector = childStatement.SelectorStatement();

        return compileNext(session, collectionMember as IQueryableMemberCollection, childSelector, statistics);
    }

    public void CompileAsChild(CollectionUsage parent)
    {
        if (ElementType.IsSimple() || ElementType == typeof(Guid) || ElementType == typeof(Guid?))
        {
            if (IsDistinct)
            {
                parent.IsDistinct = IsDistinct;
            }
        }
    }

    internal void ProcessSingleValueModeIfAny(SelectorStatement statement, IMartenSession session,
        IQueryableMemberCollection? members, QueryStatistics? statistics)
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
                        statement.ApplyAggregateOperator("DISTINCT");
                        var count = new SelectorStatement
                        {
                            SelectClause = new CountClause<int>(statement.ExportName)
                        };

                        statement.AddToEnd(count);
                        return;
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
                        statement.ApplyAggregateOperator("DISTINCT");
                        var count = new SelectorStatement
                        {
                            SelectClause = new CountClause<long>(statement.ExportName)
                        };

                        statement.AddToEnd(count);
                        return;
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
        else if (statement.IsDistinct)
        {
            if (statement.SelectClause is IScalarSelectClause c)
            {
                c.ApplyOperator("DISTINCT");
            }
            else if (statement.SelectClause is ICountClause count)
            {
                if (members != null)
                {
                    // It places itself at the back in this constructor function
                    var distinct = new DistinctSelectionStatement(statement, count, session);
                    compileNext(session, members, distinct.SelectorStatement(), statistics);
                }
                else
                {
                    throw new BadLinqExpressionException("See https://github.com/JasperFx/marten/issues/2704");
                }
            }
        }
    }

    private static void EnsureJoinKeyInCte(SelectorStatement statement, IDocumentStorage storage, string keyLocator)
    {
        var selectClause = statement.SelectClause;
        var currentFields = selectClause.SelectFields();

        if (currentFields.Contains(keyLocator))
        {
            return;
        }

        if (selectClause is DuplicatedFieldSelectClause duplicatedClause)
        {
            duplicatedClause.EnsureColumn(keyLocator);
        }
        else
        {
            // When there are no duplicate fields, SelectClauseWithDuplicatedFields returns
            // the storage itself. Wrap it in a DuplicatedFieldSelectClause to add the column.
            var fields = currentFields.Append(keyLocator).ToArray();
            statement.SelectClause = new DuplicatedFieldSelectClause(
                selectClause.FromObject, string.Empty, fields, selectClause.SelectedType, storage);
        }
    }
}
