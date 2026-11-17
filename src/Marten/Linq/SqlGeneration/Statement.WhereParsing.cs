#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Storage;
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

        // compiled-query parameter matching has to see the filters inside a
        // correlated EXISTS as well
        if (where is ExistsCollectionFilter exists)
        {
            return enumerateWheres(exists.Inner).Append(where).ToArray();
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

    public void ParseWhereClause(IReadOnlyList<Expression> wheres, IStorageSession session,
        IQueryableMemberCollection collection,
        IDocumentStorage? storage = null)
    {
        // QueryAcrossAllDatabasesAsync: every tenant, or the listed tenants, instead of the session's own.
        // The scope's filter is an ITenantFilter, so the storage adds no tenant filter of its own.
        var tenantScope = storage is { TenancyStyle: TenancyStyle.Conjoined } ? SessionTenantScope.For(session) : null;

        if (!wheres.Any())
        {
            var filter = tenantScope == null
                ? storage?.DefaultWhereFragment()
                : storage!.FilterDocuments(tenantScope.BuildFilter(), session);

            if (filter != null)
            {
                Wheres.Add((ISqlFragment)filter);
            }

            return;
        }

        var parser = new WhereClauseParser(((IMartenSession)session).Options, collection, this);
        foreach (var expression in wheres) parser.Visit(expression);

        // An AnyTenant() or TenantIsOneOf() in the query itself wins
        if (tenantScope != null && !Wheres.Any(x => x.SpecifiesTenant()))
        {
            Wheres.Add(tenantScope.BuildFilter());
        }

        if (storage != null)
        {
            if (Wheres.Count == 1)
            {
                var combinedWhere = (ISqlFragment)storage.FilterDocuments(Wheres.Single(), session);
                Wheres.Clear();
                Wheres.Add(combinedWhere);
            }
            else
            {
                var combined = CompoundWhereFragment.And(Wheres);
                var combinedWhere = (ISqlFragment)storage.FilterDocuments(combined, session);
                Wheres.Clear();
                Wheres.Add(combinedWhere);
            }
        }

        if (Wheres.Any())
        {
            compileAnySubQueries(session);
        }
    }

    protected virtual void compileAnySubQueries(IStorageSession session)
    {
        if (Wheres.OfType<ISubQueryFilter>().Any() ||
            Wheres.OfType<CompoundWhereFragment>().Any(x => x.Children.OfType<ISubQueryFilter>().Any()))
        {
            throw new BadLinqExpressionException("Sub Query filters are not supported for this operation");
        }
    }
}
