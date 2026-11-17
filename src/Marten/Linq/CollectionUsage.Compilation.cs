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
using Marten.Linq.Members.ValueCollections;
using Marten.Linq.Parsing;
using Marten.Linq.Parsing.Operators;
using Marten.Linq.SqlGeneration;
using Marten.Linq.SqlGeneration.Filters;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Linq;

[UnconditionalSuppressMessage("Trimming", "IL2067",
    Justification = "Class-level: parameter receives a DAM-annotated Type from a reflective lookup whose source type is preserved at the StoreOptions / projection-registration boundary.")]
public partial class CollectionUsage
{
    private bool _hasCompiledMany;

    // #5928: set when ProcessSingleValueModeIfAny leaves a paged SelectMany(...).Any() for compileNext to answer
    // over the page. compileNext cannot tell from the statement alone: every other Any() has already been given
    // a LIMIT 1 by then, which looks just like a Take(1).
    private bool _anyOverPagedParents;

    public Statement BuildTopStatement(IStorageSession session, IQueryableMemberCollection collection,
        ILinqDocumentStorage storage, QueryStatistics? statistics)
    {
        Statement top;

        var statement = new SelectorStatement
        {
            SelectClause = storage, Limit = _limit, Offset = _offset, IsDistinct = IsDistinct
        };

        top = statement;

        foreach (var ordering in OrderingExpressions)
        {
            statement.Ordering.Expressions.Add(ordering.BuildFragment(collection));
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

        // #5955: an OrderBy() after a top-level Select() orders the projected values. It was dropped, because
        // only the paging, Distinct() and single-value operators after a projection were carried over.
        if (SelectExpression != null && SelectExpression is not ParameterExpression && Inner != null &&
            Inner.OrderingExpressions.Any() && !Includes.Any())
        {
            if (FlattenedRowProjection.TryTranslateScalar(SelectExpression, collection, _options.Serializer(),
                    out var projected, out var orderingRefusal))
            {
                return projectComputedSelect(session, statement, projected, statistics);
            }

            throw new BadLinqExpressionException(
                $"Marten cannot translate an OrderBy() applied after the projection '{SelectExpression}': " +
                $"{orderingRefusal}. Order the documents before the Select(), or project a scalar.");
        }

        var computedRefusal = string.Empty;
        if (SelectExpression != null && isComputedProjection(SelectExpression) && !Includes.Any())
        {
            // #5942: arithmetic, a cast, a conditional or a method call computed from the document. The select
            // visitor has no translation for these, and failed with an exception from inside the expression tree
            // API or with a reader for the wrong type.
            if (FlattenedRowProjection.TryTranslateScalar(SelectExpression, collection, _options.Serializer(),
                    out var computed, out computedRefusal))
            {
                return projectComputedSelect(session, statement, computed, statistics);
            }

            // A method call keeps its old translation, in case it had one
            if (SelectExpression is not MethodCallExpression)
            {
                throw cannotTranslateComputed(SelectExpression, computedRefusal, null);
            }
        }

        if (SelectExpression != null)
        {
            var visitor = new SelectorVisitor(statement, collection, _options.Serializer());
            try
            {
                visitor.Visit(SelectExpression);
            }
            catch (ArgumentException e) when (isComputedProjection(SelectExpression))
            {
                // #5942: the select visitor visits the members inside a method call and returns nothing for them,
                // so rebuilding the call around them failed with ArgumentNullException or "Static method requires
                // null instance". Say what could not be translated instead.
                throw cannotTranslateComputed(SelectExpression, computedRefusal, e);
            }

            if (Inner != null)
            {
                Inner.CompileAsChild(this);

                IsAny = IsAny || Inner.IsAny;
                SingleValueMode ??= Inner.SingleValueMode;
                statement.IsDistinct = IsDistinct = Inner.IsDistinct;
                statement.Limit ??= Inner._limit;
                statement.Offset ??= Inner._offset;
                DistinctByExpression ??= Inner.DistinctByExpression;
            }
        }

        // Deal with query statistics at the last minute
        if (statistics != null)
        {
            statement.SelectClause = statement.SelectClause.UseStatistics(statistics);
        }

        ProcessSingleValueModeIfAny(statement.SelectorStatement(), session, collection, statistics);

        statement = compileNext(session, collection, statement, statistics).SelectorStatement();

        ApplyDistinctByIfAny(statement, collection);

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

    private static BadLinqExpressionException cannotTranslateComputed(Expression projection, string reason,
        Exception? inner)
    {
        var message = $"Marten cannot translate the Select() projection '{projection}': {reason}. A computed " +
                      "projection can use member access, arithmetic, widening casts, string concatenation, " +
                      "ToUpper(), ToLower(), Length, and ToString() of an integer or a Guid. Materialize the " +
                      "documents with ToListAsync() and project them in memory instead.";

        return new BadLinqExpressionException(message, inner);
    }

    /// <summary>
    /// #5955: the ORDER BY for one ordering over flattened rows. Over a value collection a key computed from the
    /// element -- v => -v, v => v % 3 -- used to be taken as the element itself, so the rows came back ordered by
    /// the element. Such a key is now translated, or refused. The element itself, and a member of a flattened
    /// string (#5941), keep their existing translation.
    /// </summary>
    private ISqlFragment flattenedOrdering(Ordering ordering, IQueryableMemberCollection collection)
    {
        if (collection is not IValueCollectionMember || ordering.Literal != null || ordering.MemberName != null ||
            ordering.CasingRule != CasingRule.CaseSensitive)
        {
            return ordering.BuildFragment(collection);
        }

        var key = ordering.Expression;
        while (key is UnaryExpression { NodeType: ExpressionType.Quote } quote)
        {
            key = quote.Operand;
        }

        if (key is LambdaExpression lambda)
        {
            key = lambda.Body;
        }

        if (key is UnaryExpression { NodeType: ExpressionType.Convert } box && key.Type == typeof(object))
        {
            key = box.Operand;
        }

        var isStringMember = collection.ElementType == typeof(string) &&
                             key is MemberExpression or MethodCallExpression { Arguments.Count: 0 };

        if (key is ParameterExpression || isStringMember)
        {
            return ordering.BuildFragment(collection);
        }

        if (FlattenedRowProjection.TryTranslateOrderingKey(key, ordering.Direction == OrderingDirection.Desc,
                collection, _options.Serializer(), out var fragment, out var reason))
        {
            return fragment;
        }

        throw new BadLinqExpressionException(
            $"Marten cannot translate the OrderBy() key '{key}' over these rows: {reason}. Order by the element " +
            "itself, or by a key Marten can compute: member access, arithmetic, string concatenation, ToUpper(), " +
            "ToLower(), Length, or ToString() of an integer or a Guid.");
    }

    /// <summary>
    /// #5942: a top-level projection that is computed rather than read -- arithmetic, a conditional or a method
    /// call. Member access, object construction and ExplicitSql() keep the select visitor's translation.
    /// </summary>
    private static bool isComputedProjection(Expression projection) => projection switch
    {
        // #5949: an array index, x.Values[1] or x.Grid[1][0], reads an element; the select visitor translates it
        BinaryExpression { NodeType: ExpressionType.ArrayIndex } => false,
        BinaryExpression or ConditionalExpression => true,
        UnaryExpression { NodeType: ExpressionType.Negate or ExpressionType.NegateChecked } => true,

        // A cast that changes the value's type -- (long)x.Number, (int)x.Colour. The select visitor read the
        // member underneath and could not hand back the cast type. Wrapping into Nullable<T> and boxing are not
        // computations, and keep their existing translation.
        UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert
            => convert.Type != typeof(object) &&
               (Nullable.GetUnderlyingType(convert.Type) ?? convert.Type) !=
               (Nullable.GetUnderlyingType(convert.Operand.Type) ?? convert.Operand.Type),
        MethodCallExpression call => call.Method.Name != nameof(QueryableExtensions.ExplicitSql),
        _ => false
    };

    /// <summary>
    /// #5942: compute a translated top-level projection over the documents the query selects, then apply every
    /// operator after the <c>Select()</c> to the projected value -- the same shape a projection after
    /// <c>SelectMany()</c> compiles to (#5933).
    /// </summary>
    private Statement projectComputedSelect(IStorageSession session, SelectorStatement documents,
        ISqlFragment computed, QueryStatistics? statistics)
    {
        if (IsDistinct)
        {
            throw new BadLinqExpressionException(
                $"Marten cannot translate a Distinct() of whole documents before the projection '{SelectExpression}'. " +
                "Apply Distinct() after the Select() instead.");
        }

        var after = Inner ?? new CollectionUsage(_options, SelectExpression!.Type)
        {
            SingleValueMode = SingleValueMode, IsAny = IsAny
        };

        return projectScalarRows(session, documents, false, computed, after, statistics, "of the documents")
            .Top();
    }

    /// <summary>
    /// Translate a <c>DistinctBy(keySelector)</c> operator to PostgreSQL
    /// <c>SELECT DISTINCT ON (key) ...</c>. The key is resolved against the queried
    /// document collection (the same way an OrderBy member is) and prepended to the
    /// ORDER BY, because Postgres requires the DISTINCT ON expression to match the
    /// leftmost ORDER BY expression. See https://github.com/JasperFx/marten/issues/4565.
    /// </summary>
    private void ApplyDistinctByIfAny(SelectorStatement statement, IQueryableMemberCollection collection)
    {
        if (DistinctByExpression == null)
        {
            return;
        }

        var member = collection.MemberFor(DistinctByExpression, "Invalid DistinctBy() key selector");

        // Use the exact same expression for both DISTINCT ON and the leading ORDER BY.
        // Postgres requires them to match textually, and a typed member's ordering
        // locator (e.g. CAST(... as integer)) differs from its RawLocator. Asc never
        // appends a direction suffix, so the result is a bare column expression.
        var keySql = member.BuildOrderingExpression(
            new Ordering(DistinctByExpression, OrderingDirection.Asc), CasingRule.CaseSensitive);

        statement.Ordering.Expressions.Insert(0, new LiteralOrdering(keySql));

        if (statement.SelectClause is IDistinctOnSelectClause distinctOn)
        {
            distinctOn.DistinctOn = keySql;
        }
        else
        {
            throw new BadLinqExpressionException(
                "Marten can only translate DistinctBy() when it is preceded by a Select(...) projection "
                + "(for example Select(x => new { x.Foo, x.Bar }).DistinctBy(x => x.Foo)). For other query "
                + "shapes, materialize with ToListAsync() and call DistinctBy() in memory. "
                + "See https://github.com/JasperFx/marten/issues/4565.");
        }
    }


    public Statement BuildSelectManyStatement(IStorageSession session, IQueryableMemberCollection collection,
        ISelectClause selectClause, QueryStatistics? statistics, SelectorStatement parentStatement)
    {
        var statement = new SelectorStatement
        {
            SelectClause = selectClause ?? throw new ArgumentNullException(nameof(selectClause))
        };

        parentStatement.AddToEnd(statement);

        statement = ConfigureSelectManyStatement(session, collection, statement, statistics).SelectorStatement();

        // Count()/LongCount() over a Distinct() is already wrapped in a CTE and counted by
        // ProcessSingleValueModeIfAny; re-applying DISTINCT here would hit the count clause
        // and throw "DISTINCT cannot be used with non-simple types". #5933: with a Select() after the
        // SelectMany(), this usage's Distinct() came before the projection and projectFlattenedRows()
        // has already applied it to the flattened rows.
        if (IsDistinct && SelectExpression == null &&
            SingleValueMode is not (Marten.Linq.Parsing.SingleValueMode.Count
                or Marten.Linq.Parsing.SingleValueMode.LongCount))
        {
            statement.ApplySqlOperator("DISTINCT");
        }

        return statement;
    }

    internal Statement ConfigureSelectManyStatement(IStorageSession session, IQueryableMemberCollection collection,
        SelectorStatement statement, QueryStatistics? statistics)
    {
        Statement top = statement.Top();

        statement.Limit = _limit;
        statement.Offset = _offset;
        statement.IsDistinct = IsDistinct;

        foreach (var ordering in OrderingExpressions)
            statement.Ordering.Expressions.Add(flattenedOrdering(ordering, collection));

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
            // #5933: a Select() after the SelectMany() projects each flattened row
            return projectFlattenedRows(session, collection, statement, statistics);
        }

        // A SelectMany(...).Distinct().Count() carries IsDistinct on the usage itself; re-sync it onto the
        // statement so the Count branch of ProcessSingleValueModeIfAny wraps the DISTINCT projection in a CTE
        // and counts that. Only for the count modes -- the non-aggregate Distinct() is applied via the
        // ApplySqlOperator("DISTINCT") below, so leaving its flag set would double the DISTINCT.
        if (SingleValueMode is Marten.Linq.Parsing.SingleValueMode.Count
            or Marten.Linq.Parsing.SingleValueMode.LongCount)
        {
            statement.IsDistinct = IsDistinct;
        }

        ProcessSingleValueModeIfAny(statement, session, collection, statistics);

        compileNext(session, collection, statement, statistics);

        return top;
    }

    /// <summary>
    /// #5933: compile a <c>Select()</c> that follows a <c>SelectMany()</c>. <paramref name="flattened" /> already
    /// holds the flattened rows with every operator that came BEFORE the <c>Select()</c> -- its Where(),
    /// OrderBy(), Skip()/Take() and Distinct(). Every operator AFTER the <c>Select()</c> lives on
    /// <see cref="Inner" /> (or, for a terminal one such as Count() or First(), on this usage) and applies to
    /// the projected rows.
    /// <para>
    /// A scalar projection is computed in SQL from a CTE of the flattened rows, and the projected rows are then
    /// read back and filtered, ordered, paged and aggregated exactly like the elements of a flattened value
    /// collection. Marten used to run the projection through the top-level select visitor instead, which has
    /// no translation for arithmetic or method calls over a flattened element, and silently returned the
    /// raw rows; it also dropped an OrderBy() or Where() applied after the projection.
    /// </para>
    /// <para>
    /// An object projection over child documents keeps its existing translation (jsonb_build_object, or the
    /// client-side transform), and over a value collection it is built from translated scalars. Neither can
    /// be filtered, ordered or made distinct afterwards, so those are refused rather than ignored.
    /// </para>
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Uses CloseAndBuildAs for the scalar reader of an unregistered value type, the same fallback SelectClauseResolvers documents (#5635).")]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Consumes RUC-annotated members (ISerializer, CloseAndBuildAs / GenericFactoryCache fallbacks). Projected types flow in from MartenLinqQueryable<T> and are preserved per the AOT publishing guide.")]
    private Statement projectFlattenedRows(IStorageSession session, IQueryableMemberCollection collection,
        SelectorStatement flattened, QueryStatistics? statistics)
    {
        var serializer = _options.Serializer();
        var projection = SelectExpression!;
        var isValueCollection = collection is IValueCollectionMember;

        // The operators that follow the projection. A terminal Count()/First()/Any() with nothing in between
        // was recorded on this usage.
        var after = Inner ?? new CollectionUsage(_options, projection.Type)
        {
            SingleValueMode = SingleValueMode, IsAny = IsAny
        };

        // Distinct() before the projection belongs to the flattened rows. It is applied there now, and
        // BuildSelectManyStatement() leaves the projected rows alone. Nothing on this usage is changed, because a
        // compiled query builds its statements twice.
        var distinct = IsDistinct;
        if (distinct)
        {
            try
            {
                flattened.ApplySqlOperator("DISTINCT");
            }
            catch (NotSupportedException)
            {
                throw new BadLinqExpressionException(
                    "Marten cannot translate a Distinct() between SelectMany() and Select() for these rows. " +
                    "Apply Distinct() after the Select() instead.");
            }
        }

        if (FlattenedRowProjection.TryTranslateScalar(projection, collection, serializer, out var scalar,
                out var scalarRefusal))
        {
            return projectScalarRows(session, flattened, distinct, scalar, after, statistics, "of a SelectMany()");
        }

        if (isValueCollection)
        {
            if (!FlattenedRowProjection.TryTranslateObject(projection, collection, serializer, out var newObject,
                    out var objectRefusal))
            {
                throw cannotTranslate(projection,
                    projection is NewExpression or MemberInitExpression ? objectRefusal : scalarRefusal);
            }

            var objectStatement = projectionStatement(session, flattened, distinct);
            objectStatement.SelectClause = SelectClauseResolvers.SelectData(projection.Type,
                objectStatement.SelectClause.FromObject, newObject);

            return applyAfterObjectProjection(session, after, objectStatement);
        }

        if (projection is not (MemberExpression or NewExpression or MemberInitExpression))
        {
            throw cannotTranslate(projection, scalarRefusal);
        }

        // An object, or a member that is not a scalar, of a child document: the existing projection, either
        // jsonb_build_object() or the client-side transform
        var statement = projectionStatement(session, flattened, distinct);
        var visitor = new SelectorVisitor(statement, collection, serializer);
        visitor.Visit(projection);

        return applyAfterObjectProjection(session, after, statement);
    }

    private static BadLinqExpressionException cannotTranslate(Expression projection, string reason)
    {
        return new BadLinqExpressionException(
            $"Marten cannot translate the Select() projection '{projection}' after SelectMany(): {reason}. " +
            "A flattened row can be projected with member access, arithmetic, string concatenation, ToUpper(), " +
            "ToLower(), Length, ToString() of an integer or a Guid, and anonymous types or DTOs built from those. " +
            "Materialize the flattened rows with ToListAsync() and project them in memory instead.");
    }

    /// <summary>
    /// Compute a translated scalar projection into a CTE of the rows, and read the projected rows back the way a
    /// flattened value collection's elements are read, so every operator in <paramref name="after" /> applies to
    /// the projected value. Used after <c>SelectMany()</c> (#5933) and for a top-level <c>Select()</c> (#5942).
    /// </summary>
    private Statement projectScalarRows(IStorageSession session, SelectorStatement rows, bool distinct,
        ISqlFragment scalar, CollectionUsage after, QueryStatistics? statistics, string source)
    {
        var projection = SelectExpression!;
        var projectedType = Nullable.GetUnderlyingType(projection.Type) ?? projection.Type;
        var projected = projectionStatement(session, rows, distinct);

        projected.SelectClause =
            SelectClauseResolvers.SelectData(projectedType, projected.SelectClause.FromObject, scalar);
        projected.ConvertToCommonTableExpression(session);

        var reader = new SelectorStatement
        {
            SelectClause = projectedType == typeof(string)
                ? new NewScalarStringSelectClause("d.data", projected.ExportName)
                : SelectClauseResolvers.NewScalar(projectedType, "d.data", projected.ExportName)
        };
        projected.AddToEnd(reader);

        if (after.GroupByData != null || after.GroupJoinData != null || after.SelectMany != null ||
            after.DistinctByExpression != null)
        {
            throw new BadLinqExpressionException(
                $"Marten cannot translate GroupBy(), GroupJoin(), SelectMany() or DistinctBy() applied after the " +
                $"projection '{projection}' {source}. Materialize the projected rows with ToListAsync() " +
                "and apply them in memory instead.");
        }

        var projectedRows = new SelectManyValueCollection(projectedType, new RootMember(projectedType), _options);
        return after.ConfigureSelectManyStatement(session, projectedRows, reader, statistics);
    }

    /// <summary>
    /// The statement that projects the flattened rows. A projection over rows that are ordered, paged or made
    /// distinct before it has to read them from a CTE, or the paging and DISTINCT would apply to the projected
    /// rows.
    /// Otherwise the flattened statement projects its rows itself.
    /// </summary>
    private static SelectorStatement projectionStatement(IStorageSession session, SelectorStatement flattened,
        bool distinct)
    {
        // An ORDER BY on the flattened rows has to stay below the projection too: PostgreSQL resolves a bare
        // `data` in ORDER BY to the projected output column before the input column of the same name, so
        // SelectMany(x => x.Values).OrderBy(v => v).Select(v => v % 3) would sort by the projected value.
        if (!isPaged(flattened) && !distinct && !flattened.Ordering.Expressions.Any())
        {
            return flattened;
        }

        // #5942: whole documents keep every column in the CTE, because a projection can read one that is not in
        // the JSON body, such as d.id or a duplicated field
        if (flattened.SelectClause is IDocumentStorage)
        {
            flattened.SelectClause = new NewScalarStringSelectClause("d.*", flattened.SelectClause.FromObject);
        }

        flattened.ConvertToCommonTableExpression(session);

        var statement = new SelectorStatement
        {
            SelectClause = new NewScalarStringSelectClause("d.data", flattened.ExportName)
        };
        flattened.AddToEnd(statement);

        return statement;
    }

    /// <summary>
    /// #5933: an object projection of a flattened row is read back whole, so the operators after it can only
    /// page it, count it, test it with Any() or pick one row. Anything that has to look inside the projected
    /// object is refused rather than dropped, which is what used to happen.
    /// </summary>
    private Statement applyAfterObjectProjection(IStorageSession session, CollectionUsage after,
        SelectorStatement statement)
    {
        if (after.WhereExpressions.Any() || after.OrderingExpressions.Any() || after.IsDistinct ||
            after.DistinctByExpression != null || after.SelectExpression != null || after.SelectMany != null ||
            after.GroupByData != null || after.GroupJoinData != null || after.Inner != null ||
            after.Includes.Any() ||
            after.SingleValueMode is Marten.Linq.Parsing.SingleValueMode.Sum
                or Marten.Linq.Parsing.SingleValueMode.Min or Marten.Linq.Parsing.SingleValueMode.Max
                or Marten.Linq.Parsing.SingleValueMode.Average)
        {
            throw new BadLinqExpressionException(
                $"Marten cannot translate operators applied after the object projection '{SelectExpression}' that " +
                "follows SelectMany(). Only Take(), Skip(), Count(), LongCount(), Any(), First(), FirstOrDefault(), " +
                "Single() and SingleOrDefault() can follow it. Apply Where(), OrderBy() and Distinct() before the " +
                "Select(), or project a scalar.");
        }

        if (after._limit.HasValue || after._offset.HasValue)
        {
            // Rows paged before the projection were moved into a CTE by projectionStatement(), so this
            // statement carries no paging of its own yet
            statement.Limit = after._limit;
            statement.Offset = after._offset;
        }

        after.ProcessSingleValueModeIfAny(statement, session, null, null);

        return statement.Top();
    }


    private Statement compileNext(IStorageSession session, IQueryableMemberCollection collection,
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
                    if (_anyOverPagedParents)
                    {
                        _anyOverPagedParents = false;

                        // #5928: ask whether any parent ON THE PAGE has something to flatten, rather than
                        // paging the parents that pass the filter. ProcessSingleValueModeIfAny left the
                        // paging alone for this.
                        selection = pageParentsBeforeExploding(session, selection);
                        selection.Limit = 1;
                    }

                    var filter = collectionMember.NotEmpty;
                    selection.Wheres.Add(filter);
                    selection.SelectClause = new AnySelectClause(selection.SelectClause.FromObject);
                }
                else if (SingleValueMode == Marten.Linq.Parsing.SingleValueMode.Count)
                {
                    return sumCollectionLengths<int>(selection, session, collectionMember);
                }
                else if (SingleValueMode == Marten.Linq.Parsing.SingleValueMode.LongCount)
                {
                    return sumCollectionLengths<long>(selection, session, collectionMember);
                }
                else
                {
                    var next = new CollectionUsage(_options, collectionMember.MemberType);

                    // #5921: First()/Single() directly after SelectMany() pick from the flattened rows,
                    // so the mode travels to the usage that builds the flattened statement
                    if (SingleValueModeAppliesToFlattenedRows)
                    {
                        next.SingleValueMode = SingleValueMode;
                    }

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

    /// <summary>
    /// #5968: an ordering key computed from one side of a join -- r => -r.Amount -- translated over that side's
    /// CTE, or refused. The member lookup the join used walked past the computation to the member underneath it,
    /// so the rows were silently ordered by r.Amount.
    /// </summary>
    private ISqlFragment computedJoinOrdering(Ordering ordering, Expression key,
        IQueryableMemberCollection side, string sideAlias)
    {
        if (FlattenedRowProjection.TryTranslateOrderingKey(key, ordering.Direction == OrderingDirection.Desc,
                ordering.CasingRule == CasingRule.CaseInsensitive, side, _options.Serializer(), sideAlias,
                out var fragment, out var reason))
        {
            return fragment;
        }

        throw new BadLinqExpressionException(
            $"Marten cannot translate the OrderBy() key '{key}' over the joined rows into SQL that orders the way " +
            $".NET does: {reason}. Order by a member of one side, or by a key Marten can compute: member access, " +
            "arithmetic, widening casts, string concatenation, ToUpper(), ToLower(), Length, or ToString() of an " +
            "integer or a Guid. Or sort in memory after the query.");
    }

    private static Ordering CopyOrdering(Ordering source, LambdaExpression rewritten) =>
        new(rewritten, source.Direction) { CasingRule = source.CasingRule, IsTransformed = source.IsTransformed };

    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. The select clauses closed over a PROJECTED type now resolve through SelectClauseResolvers, which holds a compile-time instantiation for every type that reached MartenLinqQueryable<T>; the reflective path here remains the fallback for a JIT (#5624). Marten 9 retired the codegen-write step the earlier wording cited.")]
    public Statement CompileGroupJoin(IStorageSession session,
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
        var outerStorage = (ILinqDocumentStorage)session.StorageFor(ElementType);
        var outerCollection = outerStorage.QueryMembers;

        outerStatement.Mode = StatementMode.CommonTableExpression;
        outerStatement.ExportName = session.NextTempTableName() + "CTE";
        var outerCteAlias = outerStatement.ExportName;

        // Use SelectClauseWithDuplicatedFields so duplicated columns are available in the CTE
        outerStatement.SelectClause = outerStorage.SelectClauseWithDuplicatedFields;

        // 2. Create the inner CTE
        var innerStorage = (ILinqDocumentStorage)session.StorageFor(groupJoin.InnerElementType);
        var innerCollection = innerStorage.QueryMembers;

        var innerStatement = new SelectorStatement
        {
            // Use SelectClauseWithDuplicatedFields so duplicated columns are available in the CTE
            SelectClause = innerStorage.SelectClauseWithDuplicatedFields,
            Mode = StatementMode.CommonTableExpression,
            ExportName = session.NextTempTableName() + "CTE"
        };
        var innerCteAlias = innerStatement.ExportName;

        // 3b. #4677 follow-up — reduce a post-SelectMany Select(z => ...) / Where(z => ...) chain
        // back into the join's (x, c) projection space, so the join SQL collapses to the actually
        // used shape. Without this:
        //   * .SelectMany(.., (x,c) => new {..}).Sum(z=>z.X) (which lowers to .Select(z=>z.X).SumAsync())
        //     hits JoinSelectClause.ApplyOperator("SUM") on an object projection and throws.
        //   * .SelectMany(.., (x,c) => new {..}).Select(z=>z.X) silently returns the original anon rows.
        //   * .SelectMany(.., (x,c) => new {..}).Where(z=>...) is dropped entirely.
        //
        // Both Select() and inner-side Where() can be folded by walking the FlattenedResultSelector's
        // bindings -- z.MemberName -> the source expression in (x, c) terms. The Select rewrites the
        // effective result selector that JoinSelectParser then renders; the Where(s) get re-homed onto
        // the inner CTE's WhereExpressions just before ParseWhereClause runs below.
        //
        // The Select / Where can land on Inner (the SelectMany usage) OR on Inner.Inner (when the
        // post-SelectMany .Select(...) changed the element type and re-linq cut a new usage), so we
        // walk the chain and gather them — mirroring how SingleValueMode / IsDistinct already cope
        // with both levels just below.
        var effectiveResultSelector = groupJoin.FlattenedResultSelector;
        var expander = new AnonProjectionExpander(groupJoin.FlattenedResultSelector);

        Expression? postSmSelect = null;
        CollectionUsage? postSmSelectCarrier = null;
        var postSmWheres = new List<(CollectionUsage Carrier, Expression Where)>();

        // A GroupBy after the SelectMany is not a projection of the joined row but an aggregation over
        // it: its own Select() is the grouping projection and its Where()s are HAVING. Both are taken
        // out of the sweep below and rendered on the join statement itself further down.
        CollectionUsage? groupByUsage = null;
        for (var sweep = Inner; sweep != null; sweep = sweep.Inner)
        {
            if (sweep.GroupByData != null)
            {
                groupByUsage = sweep;
                break;
            }
        }

        var groupingUsage = groupByUsage?.Inner;

        for (var sweep = Inner; sweep != null; sweep = sweep.Inner)
        {
            if (sweep == groupByUsage || sweep == groupingUsage)
            {
                continue;
            }

            if (postSmSelect == null && sweep.SelectExpression != null)
            {
                postSmSelect = sweep.SelectExpression;
                postSmSelectCarrier = sweep;
            }

            foreach (var w in sweep.WhereExpressions)
            {
                postSmWheres.Add((sweep, w));
            }
        }

        if (expander.CanExpand && postSmSelect != null)
        {
            // Reduce: (x, c) => <expanded SelectExpression>. Null the carrier's SelectExpression so
            // the post-join compileNext / SVM processing doesn't try to re-apply it.
            var expanded = expander.Expand(postSmSelect);
            effectiveResultSelector = Expression.Lambda(expanded, groupJoin.FlattenedResultSelector.Parameters);
            postSmSelectCarrier!.SelectExpression = null;
        }

        // Route post-SelectMany Where filters that resolve cleanly to one side onto that CTE's
        // existing Where pipeline. Inner-side filters get pushed onto the InnerCollectionUsage's
        // WhereExpressions before ParseWhereClause runs below; outer-side filters get parsed
        // straight into the already-compiled outer CTE's Wheres list (the second ParseWhereClause
        // call uses storage=null so we don't double-apply tenant / soft-delete defaults). Filters
        // that reference both sides are pinned as a known limitation -- they'd need a join-level
        // WHERE clause in JoinSelectClause; left for a follow-up if anyone hits it.
        if (expander.CanExpand && postSmWheres.Count > 0)
        {
            var carriersTouched = new HashSet<CollectionUsage>();
            var outerSideFilters = new List<Expression>();
            foreach (var (carrier, where) in postSmWheres)
            {
                var expanded = expander.Expand(where);
                var sides = PostSelectManyFilterSideAnalyzer.Analyze(
                    expanded, groupJoin.FlattenedResultSelector.Parameters);
                if (sides == PostSelectManyFilterSideAnalyzer.Side.InnerOnly && groupJoin.IsLeftJoin)
                {
                    // #5910: an inner-side filter is applied to the inner CTE, before the join. On an inner
                    // join that is the same thing; on a left join it is not. Filtering first keeps every
                    // outer row with no surviving match, as a row with a null inner, which the filter
                    // applied after the join would have removed -- so x.Amount > 100 kept a customer with
                    // no orders, and x.Amount == null matched no order and so returned every customer.
                    throw new BadLinqExpressionException(
                        "Marten cannot translate a Where() on the inner side of a left join (DefaultIfEmpty(), "
                        + "LeftJoin() or RightJoin()) applied after the join. Marten would filter the inner rows "
                        + "before joining, which keeps the outer rows the filter should remove. If that is what you "
                        + "mean, filter the inner source before the join; for the rows that have a match, use an "
                        + "inner join.");
                }

                if (sides == PostSelectManyFilterSideAnalyzer.Side.InnerOnly)
                {
                    // Replace the SelectMany inner parameter c with a fresh parameter so MemberFor's
                    // standard pipeline binds against the inner document type cleanly.
                    var innerParam = Expression.Parameter(groupJoin.InnerElementType, "child");
                    var rewritten = new ParameterReplacingVisitor(
                            groupJoin.FlattenedResultSelector.Parameters[1], innerParam)
                        .Visit(expanded);
                    groupJoin.InnerCollectionUsage.WhereExpressions.Add(rewritten);
                    carriersTouched.Add(carrier);
                }
                else if (sides == PostSelectManyFilterSideAnalyzer.Side.OuterOnly)
                {
                    // The expanded filter still references the GroupJoin result selector's
                    // anon-type navigation (e.g. x.p.Name where x = new { p, children }). Strip
                    // that navigation so MemberFor binds against the outer document type directly.
                    var outerParam = Expression.Parameter(ElementType, "outer");
                    var rewritten = new GroupJoinOuterNavigationStripper(
                            groupJoin.FlattenedResultSelector.Parameters[0],
                            groupJoin.ResultSelector, outerParam)
                        .Visit(expanded);
                    outerSideFilters.Add(rewritten);
                    carriersTouched.Add(carrier);
                }
                else
                {
                    throw new BadLinqExpressionException(
                        "Marten cannot translate this post-SelectMany Where() filter -- it touches both "
                        + "the outer and inner sides of the GroupJoin (or neither). Single-sided filters "
                        + "are supported. Combine the predicate into either the outer Query<T>().Where(...) "
                        + "or the inner GroupJoin argument's Where(...).");
                }
            }

            if (outerSideFilters.Count > 0)
            {
                // Append to the already-compiled outer CTE. storage=null so we don't re-apply the
                // default tenant / soft-delete filter that the first ParseWhereClause call wrapped.
                outerStatement.ParseWhereClause(outerSideFilters, session, outerCollection);
            }

            foreach (var carrier in carriersTouched)
            {
                carrier.WhereExpressions.Clear();
            }
        }

        // Apply extracted Where clauses and default filters (soft delete, tenancy) to inner CTE.
        innerStatement.ParseWhereClause(
            groupJoin.InnerCollectionUsage.WhereExpressions,
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
            effectiveResultSelector);

        // 4b. A GroupBy over the joined rows. The keys and the aggregate arguments are written in the
        // post-SelectMany projection's terms, so they are expanded back into the join's (x, c) space
        // first and then resolved against whichever side's CTE they came from. The GROUP BY itself is
        // rendered by SelectorStatement, which writes it right after the join's own SELECT ... FROM.
        GroupBySelectParser? groupByParser = null;
        if (groupByUsage != null)
        {
            if (groupingUsage?.SelectExpression == null)
            {
                throw new BadLinqExpressionException(
                    "GroupBy must be followed by a Select() projection. Marten does not support returning IGrouping<K,T> directly.");
            }

            if (!expander.CanExpand)
            {
                throw new BadLinqExpressionException(
                    "Marten cannot translate a GroupBy() applied after GroupJoin(...).SelectMany(...) for this "
                    + "projection. Project the SelectMany into an anonymous type, a record, or one whole side.");
            }

            var groupBy = groupByUsage.GroupByData!;
            var keySelector = Expression.Lambda(
                expander.Expand(groupBy.KeySelector.Body), groupBy.KeySelector.Parameters);

            groupByParser = new GroupBySelectParser(
                _options,
                outerCollection,
                keySelector,
                expander.Expand(groupingUsage.SelectExpression),
                FindGroupingParameter(groupingUsage.SelectExpression),
                joinParser.ResolveAliasedMember);

            if (groupByParser.IsScalar)
            {
                throw new BadLinqExpressionException(
                    "Marten does not yet support a scalar GroupBy() projection over a GroupJoin. Project the "
                    + "grouping into an anonymous type or a record.");
            }

            if (groupingUsage.WhereExpressions.Any())
            {
                throw new BadLinqExpressionException(
                    "Marten does not yet support HAVING (a Where() on the IGrouping) over a GroupJoin.");
            }
        }

        // 5. Create the JoinSelectClause and final SelectorStatement
        // 9.0 (#4308): use GenericFactoryCache's object[] overload — the
        // 6-arg ctor doesn't fit the fixed-arity overloads, so we pay an
        // array allocation per call in exchange for skipping MakeGenericType.
        var resultType = groupByParser != null
            ? groupingUsage!.SelectExpression!.Type
            : effectiveResultSelector.ReturnType;

        // Sum()/Min()/Max()/Average() over a bare scalar projection cannot aggregate the
        // to_jsonb(...) form (Postgres has no sum/min/max/avg for jsonb). For those, render the
        // raw scalar into the join CTE and put a standard scalar select clause over it below.
        var pendingMode = Inner?.SingleValueMode ?? Inner?.Inner?.SingleValueMode;
        var scalarAggregate = joinParser.ScalarRawProjection != null
            && pendingMode is Marten.Linq.Parsing.SingleValueMode.Sum or Marten.Linq.Parsing.SingleValueMode.Min
                or Marten.Linq.Parsing.SingleValueMode.Max or Marten.Linq.Parsing.SingleValueMode.Average
            && (resultType == typeof(string) || resultType.IsValueType);

        var joinSelectClause = (ISelectClause)GenericFactoryCache.BuildAs<object>(
            typeof(JoinSelectClause<>),
            resultType,
            new object[]
            {
                groupByParser != null
                    ? groupByParser.NewObject
                    : scalarAggregate ? joinParser.ScalarRawProjection : joinParser.Projection,
                outerCteAlias,
                innerCteAlias,
                groupJoin.IsLeftJoin,
                outerKeyLocator,
                innerKeyLocator
            },
            static closed => args => Activator.CreateInstance(closed, args)!);

        var joinStatement = new SelectorStatement
        {
            SelectClause = joinSelectClause
        };

        if (groupByParser != null)
        {
            foreach (var column in groupByParser.GroupByColumns)
            {
                joinStatement.GroupByColumns.Add(column);
            }
        }

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

        // Post-SelectMany OrderBy(): render it on the join select, not inside a CTE. A join is free to
        // discard the order of its inputs, and Limit/Offset are already transferred above -- so leaving
        // the ordering out does not produce an unordered list, it produces an arbitrary page.
        var postSmOrderings = new List<Ordering>();
        for (var sweep = Inner; sweep != null; sweep = sweep.Inner)
        {
            postSmOrderings.AddRange(sweep.OrderingExpressions);
        }

        foreach (var ordering in postSmOrderings)
        {
            if (!string.IsNullOrEmpty(ordering.Literal))
            {
                joinStatement.Ordering.Expressions.Add(new LiteralOrdering(ordering.Literal!));
                continue;
            }

            if (!expander.CanExpand)
            {
                throw new BadLinqExpressionException(
                    "Marten cannot translate an OrderBy() applied after GroupJoin(...).SelectMany(...) for this "
                    + "projection. Order the outer Query<T>() before the GroupJoin instead.");
            }

            var body = ordering.Expression;
            while (body is UnaryExpression { NodeType: ExpressionType.Quote or ExpressionType.Convert } unary)
            {
                body = unary.Operand;
            }

            if (body is LambdaExpression orderingLambda)
            {
                body = orderingLambda.Body;
            }

            var expandedOrdering = expander.Expand(body);
            var orderingSide = PostSelectManyFilterSideAnalyzer.Analyze(
                expandedOrdering, groupJoin.FlattenedResultSelector.Parameters);

            string sql;
            if (orderingSide == PostSelectManyFilterSideAnalyzer.Side.InnerOnly)
            {
                var innerParam = Expression.Parameter(groupJoin.InnerElementType, "child");
                var rewritten = new ParameterReplacingVisitor(
                    groupJoin.FlattenedResultSelector.Parameters[1], innerParam).Visit(expandedOrdering);

                if (Ordering.ComputedKeyBody(Expression.Lambda(rewritten, innerParam)) is { } computedInner)
                {
                    joinStatement.Ordering.Expressions.Add(
                        computedJoinOrdering(ordering, computedInner, innerCollection, innerCteAlias));
                    continue;
                }

                sql = CopyOrdering(ordering, Expression.Lambda(rewritten, innerParam))
                    .BuildExpression(innerCollection)
                    .Replace("d.", innerCteAlias + ".");
            }
            else if (orderingSide == PostSelectManyFilterSideAnalyzer.Side.OuterOnly)
            {
                var outerParam = Expression.Parameter(ElementType, "outer");
                var rewritten = new GroupJoinOuterNavigationStripper(
                    groupJoin.FlattenedResultSelector.Parameters[0],
                    groupJoin.ResultSelector, outerParam).Visit(expandedOrdering);

                if (Ordering.ComputedKeyBody(Expression.Lambda(rewritten, outerParam)) is { } computedOuter)
                {
                    joinStatement.Ordering.Expressions.Add(
                        computedJoinOrdering(ordering, computedOuter, outerCollection, outerCteAlias));
                    continue;
                }

                sql = CopyOrdering(ordering, Expression.Lambda(rewritten, outerParam))
                    .BuildExpression(outerCollection)
                    .Replace("d.", outerCteAlias + ".");
            }
            else
            {
                throw new BadLinqExpressionException(
                    "Marten cannot translate this post-SelectMany OrderBy() -- it touches both the outer and "
                    + "inner sides of the GroupJoin (or neither). Order by a member of one side.");
            }

            joinStatement.Ordering.Expressions.Add(new LiteralOrdering(sql));
        }

        // Transfer Distinct() from the SelectMany usage chain. JoinSelectClause implements
        // IScalarSelectClause, so ProcessSingleValueModeIfAny renders DISTINCT(<projection>) for
        // a materialized Distinct() and wraps it in a count(*) CTE for Distinct().Count().
        // Without this the join would silently return non-distinct rows / the joined row count.
        if ((Inner?.IsDistinct ?? false) || (Inner?.Inner?.IsDistinct ?? false))
        {
            joinStatement.IsDistinct = true;
        }

        if (scalarAggregate)
        {
            // Wrap the raw-scalar join in a CTE and aggregate over its single 'data' column with a
            // standard scalar select clause, which already handles the aggregate result types
            // (SUM(int)->bigint, AVG->double via CloneToDouble, etc.).
            joinStatement.ConvertToCommonTableExpression(session);

            // For a nullable scalar (e.g. (int?)c.Amount) build the clause over the underlying type
            // -- NewScalarSelectClause<T> is `where T : struct` and also implements ISelector<T?>,
            // so the single-value handler reads the nullable result (null when all rows are null).
            var clauseType = Nullable.GetUnderlyingType(resultType) ?? resultType;
            var scalarClause = clauseType == typeof(string)
                ? new NewScalarStringSelectClause("d.data", joinStatement.ExportName)
                : SelectClauseResolvers.NewScalar(clauseType, "d.data", joinStatement.ExportName);

            var scalarStatement = new SelectorStatement { SelectClause = scalarClause };
            joinStatement.AddToEnd(scalarStatement);

            ProcessSingleValueModeIfAny(scalarStatement, session, null, statistics);

            return joinStatement;
        }

        // #5910: Any() must ask whether the join has a row, not whether the outer side does. Same trap
        // as Count() below: AnySelectClause selects from SelectClause.FromObject, which is the *outer*
        // CTE, so without the wrap Any() dropped the join and every filter on its inner side, and
        // answered true for any outer row at all.
        if (IsAny || SingleValueMode == Marten.Linq.Parsing.SingleValueMode.Any)
        {
            joinStatement.ConvertToCommonTableExpression(session);

            var anyStatement = new SelectorStatement
            {
                SelectClause = new NewScalarStringSelectClause("d.data", joinStatement.ExportName)
            };
            joinStatement.AddToEnd(anyStatement);

            ProcessSingleValueModeIfAny(anyStatement, session, null, statistics);

            return joinStatement;
        }

        // Count()/LongCount() must count the join, not the outer side. ProcessSingleValueModeIfAny
        // builds its count(*) over SelectClause.FromObject, and JoinSelectClause reports the *outer*
        // CTE there -- so counting without wrapping silently drops the join itself, along with every
        // filter on the inner side. Distinct().Count() is left alone: ProcessSingleValueModeIfAny
        // already builds its own count(*) CTE over the DISTINCT projection.
        if ((SingleValueMode is Marten.Linq.Parsing.SingleValueMode.Count
                or Marten.Linq.Parsing.SingleValueMode.LongCount)
            && !joinStatement.IsDistinct)
        {
            joinStatement.ConvertToCommonTableExpression(session);

            var countStatement = new SelectorStatement
            {
                SelectClause = new NewScalarStringSelectClause("d.data", joinStatement.ExportName)
            };
            joinStatement.AddToEnd(countStatement);

            ProcessSingleValueModeIfAny(countStatement, session, null, statistics);

            return joinStatement;
        }

        // Apply single value mode to the join statement
        ProcessSingleValueModeIfAny(joinStatement, session, null, statistics);

        return joinStatement;
    }

    public Statement CompileGroupBy(IStorageSession session,
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
            _options,
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
                    statement.SelectClause = SelectClauseResolvers.NewScalar(member.MemberType, member,
                        statement.SelectClause.FromObject);
                }
            }
            else if (fragment is LiteralSql or GroupBySqlFragment)
            {
                // JSON serialization uses the expression's actual result type (Count vs LongCount).
                statement.SelectClause = SelectClauseResolvers.SelectData(groupingUsage.SelectExpression.Type,
                    statement.SelectClause.FromObject, new GroupBySqlFragment("to_jsonb(", fragment, ")"));
            }
        }
        else
        {
            var resultType = groupingUsage.SelectExpression.Type;
            statement.SelectClause =
                SelectClauseResolvers.SelectData(resultType, statement.SelectClause.FromObject, parser.NewObject);
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

        // Transfer single-value operators applied directly on the grouping usage
        // itself (e.g., .GroupBy(...).Select(...).CountAsync() / .AnyAsync()).
        // See https://github.com/JasperFx/marten/issues/4278.
        if (groupingUsage.SingleValueMode.HasValue)
        {
            SingleValueMode ??= groupingUsage.SingleValueMode;
        }

        if (groupingUsage.IsAny)
        {
            IsAny = true;
        }

        // Transfer downstream operators from the grouping usage's Inner (if any)
        // e.g., OrderBy, Take, Skip after Select
        var downstream = groupingUsage.Inner;
        if (downstream != null)
        {
            statement.Limit ??= downstream._limit;
            statement.Offset ??= downstream._offset;

            foreach (var ordering in downstream.OrderingExpressions)
            {
                statement.Ordering.Expressions.Add(parser.BuildOrderingFragment(ordering));
            }

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

    public Statement CompileSelectMany(IStorageSession session,
        SelectorStatement parentStatement, ICollectionMember collectionMember, QueryStatistics? statistics)
    {
        if (_hasCompiledMany)
        {
            return parentStatement;
        }

        _hasCompiledMany = true;

        // #5928: a Take()/Skip() before the SelectMany() pages the parents. Explode from a CTE of that page,
        // or the LIMIT/OFFSET would land on the exploded rows and page the children instead.
        parentStatement = pageParentsBeforeExploding(session, parentStatement);

        parentStatement.Mode = StatementMode.CommonTableExpression;
        parentStatement.ExportName = session.NextTempTableName() + "CTE";

        parentStatement.SelectClause =
            collectionMember.BuildSelectClauseForExplosion(parentStatement.SelectClause.FromObject);


        // THINK THIS IS TOO SOON. MUCH OF THE LOGIC NEEDS TO GO IN THIS INSTEAD!!!
        var childStatement = collectionMember.AttachSelectManyStatement(this, session, parentStatement, statistics);
        var childSelector = childStatement.SelectorStatement();

        // #5933: the operators after a Select() were compiled onto the projected rows by projectFlattenedRows()
        if (SelectExpression != null && SelectMany == null)
        {
            return childSelector;
        }

        return compileNext(session, collectionMember as IQueryableMemberCollection, childSelector, statistics);
    }

    /// <summary>
    /// #5933: a compiled query builds its statements twice from the same usages -- once for the command and
    /// once for the handler. Forget that a SelectMany() was compiled by the first build, or the second build
    /// stops at the parent documents and a Select() after the SelectMany() reads the parent document type.
    /// </summary>
    internal void ResetCompilation()
    {
        _hasCompiledMany = false;
    }

    /// <summary>
    /// #5928: when the statement a SelectMany() flattens is paged -- a Take() and/or Skip() before the
    /// SelectMany() -- the paging belongs to the parent rows. The collection is exploded by a set-returning
    /// function in the select list, so a LIMIT/OFFSET left on that statement would page the exploded rows.
    /// Turn the paged statement into a CTE of the page, keeping its WHERE and its ORDER BY (which decides the
    /// page), and return a fresh statement over that CTE for the caller to explode. An unpaged statement is
    /// returned untouched, so its SQL does not change.
    /// <para>
    /// The flattened rows come out parent by parent in the page's order, and in collection order within a
    /// parent: the explosion is a plain scan of the ordered page, the same as an unpaged
    /// <c>OrderBy().SelectMany()</c> already relies on.
    /// </para>
    /// </summary>
    private static SelectorStatement pageParentsBeforeExploding(IStorageSession session,
        SelectorStatement parentStatement)
    {
        if (!isPaged(parentStatement))
        {
            return parentStatement;
        }

        // d.* rather than d.data: the explosion only reads d.data, but the not-empty filter Any() puts on the page
        // reads a duplicated array field from its own column (d.numbers), which has to survive into the CTE
        parentStatement.SelectClause =
            new NewScalarStringSelectClause("d.*", parentStatement.SelectClause.FromObject);
        parentStatement.ConvertToCommonTableExpression(session);

        var page = new SelectorStatement
        {
            SelectClause = new NewScalarStringSelectClause("d.data", parentStatement.ExportName)
        };
        parentStatement.AddToEnd(page);

        return page;
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

    /// <summary>
    /// #5921: a First / FirstOrDefault / Single / SingleOrDefault that lands on a usage ending in a plain
    /// SelectMany() -- <c>SelectMany(x => x.Children).FirstAsync()</c> -- with nothing downstream of it
    /// describes the flattened rows, not the documents being flattened. Applied to the parent statement it
    /// would put the LIMIT on the parents and leave the flattened statement a list, for which there is no
    /// single-result handler. Count / LongCount / Any keep their own SelectMany handling in compileNext,
    /// and a GroupJoin -- including the SelectMany() that flattens one -- carries the mode onto its join
    /// statement itself.
    /// </summary>
    internal bool SingleValueModeAppliesToFlattenedRows =>
        SelectMany != null && Inner == null && GroupJoinData == null && SelectManyCallExpression == null &&
        SingleValueMode is Marten.Linq.Parsing.SingleValueMode.First
            or Marten.Linq.Parsing.SingleValueMode.FirstOrDefault
            or Marten.Linq.Parsing.SingleValueMode.Single
            or Marten.Linq.Parsing.SingleValueMode.SingleOrDefault;

    /// <summary>
    /// #5928: <c>Take(n).SelectMany(x => x.Children).Any()</c> asks whether any of the first n parents has a
    /// child. compileNext answers that over a CTE of the page, so the paging must still be on the parent
    /// statement when it gets there instead of being replaced by the LIMIT 1 every other Any() uses.
    /// </summary>
    private bool anyAppliesToPagedParentsOfSelectMany(SelectorStatement statement) =>
        SelectMany != null && Inner == null && GroupJoinData == null && SelectManyCallExpression == null &&
        SingleValueMode == Marten.Linq.Parsing.SingleValueMode.Any && isPaged(statement);

    internal void ProcessSingleValueModeIfAny(SelectorStatement statement, IStorageSession session,
        IQueryableMemberCollection? members, QueryStatistics? statistics)
    {
        if (SingleValueModeAppliesToFlattenedRows)
        {
            // compileNext hands the mode to the flattened statement
            return;
        }

        if (IsAny || SingleValueMode == Marten.Linq.Parsing.SingleValueMode.Any)
        {
            if (anyAppliesToPagedParentsOfSelectMany(statement))
            {
                // compileNext pages the parents in a CTE and asks that page
                _anyOverPagedParents = true;
                return;
            }

            statement.SelectClause = new AnySelectClause(statement.SelectClause.FromObject);

            // #5920: Take(0).Any() is false whatever the table holds -- keep its LIMIT 0 rather than
            // widening it to the LIMIT 1 that every other Any() uses. An OFFSET is left alone: asking
            // for one row past it is exactly what Skip(k).Any() means.
            statement.Limit = statement.Limit == 0 ? 0 : 1;
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
                    if (tryCountPagedStatement<int>(statement, session))
                    {
                        return;
                    }

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

                    if (statement.GroupByColumns.Count > 0)
                    {
                        // .GroupBy(...).Select(...).CountAsync() should return the
                        // number of groups, not count(*) over the grouped rows.
                        // Wrap the GROUP BY query in a CTE and count its rows.
                        // See https://github.com/JasperFx/marten/issues/4278.
                        statement.ConvertToCommonTableExpression(session);
                        var groupCount = new SelectorStatement
                        {
                            SelectClause = new CountClause<int>(statement.ExportName)
                        };

                        statement.AddToEnd(groupCount);
                        return;
                    }

                    statement.SelectClause = new CountClause<int>(statement.SelectClause.FromObject);

                    break;

                case Marten.Linq.Parsing.SingleValueMode.LongCount:
                    if (tryCountPagedStatement<long>(statement, session))
                    {
                        return;
                    }

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

                    if (statement.GroupByColumns.Count > 0)
                    {
                        // .GroupBy(...).Select(...).LongCountAsync() should return
                        // the number of groups. See
                        // https://github.com/JasperFx/marten/issues/4278.
                        statement.ConvertToCommonTableExpression(session);
                        var groupLongCount = new SelectorStatement
                        {
                            SelectClause = new CountClause<long>(statement.ExportName)
                        };

                        statement.AddToEnd(groupLongCount);
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

    /// <summary>
    /// #5920: a Count()/LongCount() over a paged statement -- one carrying a LIMIT (Take()) or an OFFSET
    /// (Skip()) -- has to count the page, not attach the paging to the count(*) itself. A bare
    /// <c>select count(*) ... LIMIT n</c> returns the full count, and <c>OFFSET k</c> skips the single row the
    /// aggregate returns, so the count came back as 0. Wrap the paged statement in a CTE, keeping its
    /// ORDER BY because that decides which rows are on the page, and count the CTE instead.
    /// <para>
    /// Unpaged statements are left exactly as they were: their count(*) runs directly against the table.
    /// Distinct() and GroupBy() are left alone too, because those branches already count a CTE that
    /// carries the paging.
    /// </para>
    /// </summary>
    private bool tryCountPagedStatement<T>(SelectorStatement statement, IStorageSession session)
    {
        // A GroupJoin() counts its own joined CTE in CompileGroupJoin(), which already carries the paging.
        if (!isPaged(statement) || statement.IsDistinct || statement.GroupByColumns.Count > 0 || GroupJoinData != null)
        {
            return false;
        }

        // A SelectMany() with nothing after it is counted by compileNext() as SUM(jsonb_array_length(...))
        // over this very statement, which it builds on top of whatever is here. Leave the statement (and its
        // ordering and paging) intact for it; compileNext() wraps the paged rows itself.
        if (SelectMany != null && Inner == null)
        {
            statement.SelectClause = new CountClause<T>(statement.SelectClause.FromObject);
            return true;
        }

        statement.ConvertToCommonTableExpression(session);
        statement.AddToEnd(new SelectorStatement { SelectClause = new CountClause<T>(statement.ExportName) });

        return true;
    }

    private static bool isPaged(SelectorStatement statement) => statement.Limit.HasValue || statement.Offset.HasValue;

    /// <summary>
    /// Counts the elements of a SelectMany() target as SUM(jsonb_array_length(collection)) over the parent
    /// rows. When the parent rows are paged (Take()/Skip() before the SelectMany()), the sum has to run over
    /// the page rather than carry its LIMIT/OFFSET, which would sum every document and then page the single
    /// aggregate row (#5920).
    /// </summary>
    private static Statement sumCollectionLengths<T>(SelectorStatement selection, IStorageSession session,
        ICollectionMember collectionMember) where T : struct
    {
        // #5934: a duplicated array field is counted from its own column. Its JSONB locator is empty.
        var duplicated = collectionMember as DuplicatedArrayField;

        var target = selection;
        if (isPaged(selection))
        {
            // A duplicated array column has to survive into the page's CTE to be counted
            selection.SelectClause = new NewScalarStringSelectClause(duplicated == null ? "d.data" : "d.*",
                selection.SelectClause.FromObject);
            selection.ConvertToCommonTableExpression(session);

            target = new SelectorStatement
            {
                SelectClause = new NewScalarStringSelectClause("d.data", selection.ExportName)
            };
            selection.AddToEnd(target);
        }

        target.SelectClause = new NewScalarSelectClause<T>(
            duplicated?.LengthLocator ?? $"jsonb_array_length({collectionMember.JSONBLocator})",
            target.SelectClause.FromObject);

        target.ApplyAggregateOperator("SUM");

        return target;
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
