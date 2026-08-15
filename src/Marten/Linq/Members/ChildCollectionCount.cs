#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core;
using Marten.Exceptions;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

/// <summary>
///     #5223: implemented by collection members that can translate <c>Count(predicate)</c> over their
///     elements into a scalar a <c>Select()</c> projection can place directly in its
///     <c>jsonb_build_object(...)</c> list, rather than only as the left side of a <c>Where()</c>
///     comparison.
/// </summary>
internal interface ICountableCollection
{
    /// <summary>
    ///     Returns false — rather than throwing — for any predicate that cannot be expressed, because
    ///     the caller's fallback is a correct client-side projection, not a failed query.
    /// </summary>
    bool TryBuildCountExpression(Expression predicate, [NotNullWhen(true)] out ISqlFragment? fragment);
}

internal class ChildCollectionCount: IComparableMember, IWhereFragmentHolder
{
    private readonly ICollectionMember _collection;
    private readonly ISerializer _serializer;

    public ChildCollectionCount(ICollectionMember collection, ISerializer serializer)
    {
        _collection = collection;
        _serializer = serializer;
    }

    public List<ISqlFragment> Wheres { get; } = new();

    public ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        var filters = asJsonPathFilters();
        if (filters != null)
        {
            return new ChildCollectionJsonPathCountFilter(_collection, _serializer, filters, op, constant);
        }

        throw new BadLinqExpressionException(
            "Marten does not (yet) support this pattern for child collection.Count() queries");
    }

    /// <summary>
    ///     #5223: the same count as a standalone scalar, for a <c>Select()</c> projection rather than a
    ///     comparison.
    /// </summary>
    public bool TryBuildCountExpression([NotNullWhen(true)] out ISqlFragment? fragment)
    {
        var filters = asJsonPathFilters();
        if (filters == null)
        {
            fragment = null;
            return false;
        }

        fragment = new ChildCollectionJsonPathCount(_collection, _serializer, filters);
        return true;
    }

    /// <summary>
    ///     Every accumulated predicate has to be expressible as a jsonpath filter, which is the only form
    ///     the count is translated through. Returns null when any of them is not.
    /// </summary>
    private IReadOnlyList<ICollectionAware>? asJsonPathFilters()
    {
        if (Wheres.Count == 0)
        {
            return null;
        }

        var filters = new List<ICollectionAware>(Wheres.Count);

        return Wheres.All(where => collect(where, filters)) ? filters : null;
    }

    private static bool collect(ISqlFragment where, List<ICollectionAware> filters)
    {
        switch (where)
        {
            case ICollectionAware aware when aware.CanBeJsonPathFilter():
                filters.Add(aware);
                return true;

            // #5223: a bare boolean predicate -- Count(x => x.IsActive), the shape the issue was
            // reported with -- registers a BooleanFieldIsTrue, which is not collection-aware, so the
            // whole count dropped out of the jsonpath tier. It means exactly `member = true`, which
            // is. Only the true case: BooleanFieldIsFalse deliberately also matches a MISSING
            // property ("raw is null or typed = False") and `@.member == false` in jsonpath does
            // not, so that one keeps falling back rather than quietly counting fewer elements.
            case BooleanFieldIsTrue isTrue:
                filters.Add(new MemberComparisonFilter(isTrue.Member, new CommandParameter(true), "="));
                return true;

            // #5223: `a && b` in the predicate arrives as ONE compound fragment. The jsonpath filter
            // already joins the filters it is given with &&, so an "and" compound flattens exactly.
            // An "or" compound does not -- that would need || , which this tier never emits -- so it
            // falls back rather than being silently narrowed to a conjunction.
            case CompoundWhereFragment compound when compound.Separator.Trim().EqualsIgnoreCase("and"):
                return compound.Children.All(child => collect(child, filters));

            default:
                return false;
        }
    }

    void IWhereFragmentHolder.Register(ISqlFragment fragment)
    {
        Wheres.Add(fragment);
    }
}
