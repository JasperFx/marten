#nullable enable
using System;
using System.Linq;
using JasperFx.Core;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

public class SelectorStatement: Statement, IWhereFragmentHolder
{
    public int? Limit { get; set; }
    public int? Offset { get; set; }

    public OrderByFragment Ordering { get; internal set; } = new();

    public ISelectClause SelectClause { get; internal set; }

    public virtual string FromObject => SelectClause?.FromObject;

    public bool IsDistinct { get; set; }

    public void Register(ISqlFragment fragment)
    {
        Wheres.Add(fragment);
    }

    protected override void configure(ICommandBuilder sql)
    {
        startCommonTableExpression(sql);

        SelectClause.Apply(sql);

        if (Wheres.Any())
        {
            sql.Append(" where ");
            Wheres[0].Apply(sql);
            for (var i = 1; i < Wheres.Count; i++)
            {
                sql.Append(" and ");
                Wheres[i].Apply(sql);
            }
        }

        Ordering.Apply(sql);

        if (Offset.HasValue)
        {
            sql.Append(" OFFSET ");
            sql.AppendParameter(Offset.Value);
        }

        if (Limit.HasValue)
        {
            sql.Append(" LIMIT ");
            sql.AppendParameter(Limit.Value);
        }

        endCommonTableExpression(sql);
    }

    public void ToAny()
    {
        SelectClause = new AnySelectClause(SelectClause.FromObject);
        Limit = 1;
    }

    public void ToCount<T>()
    {
        SelectClause = new CountClause<T>(SelectClause.FromObject);
    }

    public void ApplyAggregateOperator(string databaseOperator)
    {
        ApplySqlOperator(databaseOperator);
        SingleValue = true;
        ReturnDefaultWhenEmpty = true;
    }

    public void ApplySqlOperator(string databaseOperator)
    {
        if (SelectClause is IScalarSelectClause c)
        {
            c.ApplyOperator(databaseOperator);

            // Hack, but let it go
            if (databaseOperator == "AVG")
            {
                SelectClause = c.CloneToDouble();
            }
        }
        else
        {
            throw new NotSupportedException(
                $"The database operator '{databaseOperator}' cannot be used with non-simple types");
        }
    }

    public IQueryHandler<TResult> BuildSingleResultHandler<TResult>(IMartenSession session, Statement topStatement)
    {
        var selector = (ISelector<TResult>)SelectClause.BuildSelector(session);
        return new OneResultHandler<TResult>(topStatement, selector, ReturnDefaultWhenEmpty, CanBeMultiples);
    }

    public override string ToString()
    {
        return $"Selector statement: {SelectClause}";
    }

    protected override void compileAnySubQueries(IMartenSession session)
    {
        if (Wheres[0] is CompoundWhereFragment compound)
        {
            // See https://github.com/JasperFx/marten/issues/3025
            foreach (var deepCompound in compound.Children.OfType<CompoundWhereFragment>())
            {
                foreach (var subQueryFilter in deepCompound.Children.OfType<ISubQueryFilter>())
                {
                    subQueryFilter.PlaceUnnestAbove(session, this);
                }
            }

            if (compound.Children.OfType<ISubQueryFilter>().Any())
            {
                var subQueries = compound.Children.OfType<ISubQueryFilter>().ToArray();
                if (compound.Separator.ContainsIgnoreCase("and"))
                {
                    var others = compound.Children.Where(x => !subQueries.Contains(x)).ToArray();

                    ISqlFragment? topLevel = null;
                    switch (others.Length)
                    {
                        case 0:
                            break;

                        case 1:
                            topLevel = others.Single();
                            break;

                        default:
                            topLevel = CompoundWhereFragment.And(others);
                            break;
                    }

                    foreach (var subQuery in subQueries) subQuery.PlaceUnnestAbove(session, this, topLevel);

                    // We've moved all the non-sub query filters up to the various explode statements
                    Wheres.Clear();
                    Wheres.Add(CompoundWhereFragment.And(subQueries));
                }
                else
                {
                    foreach (var subQuery in subQueries) subQuery.PlaceUnnestAbove(session, this);
                }
            }
        }
        else if (Wheres[0] is ISubQueryFilter subQuery)
        {
            subQuery.PlaceUnnestAbove(session, this);
        }



        // The else is perfectly fine as is
    }
}
