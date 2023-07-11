using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

public abstract partial class Statement: IWhereFragmentHolder
{
    public List<ISqlFragment> Wheres { get; } = new();

    public ISqlFragment[] AllFilters()
    {
        return Wheres.SelectMany(enumerateWheres).ToArray();
    }

    private ISqlFragment[] enumerateWheres(ISqlFragment where)
    {
        if (where is CompoundWhereFragment compound)
        {
            return compound.Children.SelectMany(enumerateWheres).ToArray();
        }

        return new[] { where };
    }

    void IWhereFragmentHolder.Register(ISqlFragment filter)
    {
        if (filter != null)
        {
            Wheres.Add(filter);
        }
    }

    public void ParseWhereClause(IReadOnlyList<Expression> wheres, IMartenSession session,
        IQueryableMemberCollection collection,
        IDocumentStorage? storage = null)
    {
        if (!wheres.Any())
        {
            var filter = storage?.DefaultWhereFragment();
            if (filter != null)
            {
                Wheres.Add(filter);
            }

            return;
        }

        var parser = new WhereClauseParser(session.Options, collection, this);
        foreach (var expression in wheres) parser.Visit(expression);

        if (storage != null)
        {
            if (Wheres.Count == 1)
            {
                var combinedWhere = storage.FilterDocuments(Wheres.Single(), session);
                Wheres.Clear();
                Wheres.Add(combinedWhere);
            }
            else
            {
                var combined = CompoundWhereFragment.And(Wheres);
                var combinedWhere = storage.FilterDocuments(combined, session);
                Wheres.Clear();
                Wheres.Add(combinedWhere);
            }
        }

        if (Wheres.Any())
        {
            compileAnySubQueries(session);
        }
    }

    protected virtual void compileAnySubQueries(IMartenSession session)
    {
        if (Wheres.OfType<ISubQueryFilter>().Any() ||
            Wheres.OfType<CompoundWhereFragment>().Any(x => x.Children.OfType<ISubQueryFilter>().Any()))
        {
            throw new BadLinqExpressionException("Sub Query filters are not supported for this operation");
        }
    }
}
