#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JasperFx.Core;
using Marten.Linq.Members.ValueCollections;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal class ChildCollectionWhereClause: IWhereFragmentHolder
{
    private ISqlFragment _fragment;

    public void Register(ISqlFragment fragment)
    {
        _fragment = fragment;
    }

    public ISqlFragment Fragment => _fragment;

    public static bool TryBuildInlineFragment(ISqlFragment fragment, ICollectionMember collectionMember,
        ISerializer serializer, [NotNullWhen(true)]out ICollectionAwareFilter? filter)
    {
        if (fragment is ICollectionAware collectionAware && collectionAware.CanReduceInChildCollection())
        {
            filter = collectionAware.BuildFragment(collectionMember, serializer);

            return true;
        }

        if (fragment is CompoundWhereFragment compound)
        {
            if (compound.Children.All(x => x is ICollectionAware aware && aware.CanReduceInChildCollection()))
            {
                var children = compound.Children.OfType<ICollectionAware>().ToArray();
                if (compound.Separator.ContainsIgnoreCase("and"))
                {
                    if (children.All(x => x.SupportsContainment()))
                    {
                        var containment = new ContainmentWhereFilter(collectionMember, serializer)
                        {
                            Usage = ContainmentUsage.Collection
                        };

                        foreach (var child in children) child.PlaceIntoContainmentFilter(containment);

                        filter = containment;
                        return true;
                    }
                }
            }
        }

        filter = default;
        return false;
    }

    public ISqlFragment CompileFragment(ICollectionMember collectionMember, ISerializer serializer)
    {
        if (_fragment is ICollectionAwareFilter awareFilter && awareFilter.CollectionMember != collectionMember)
        {
            return awareFilter.MoveUnder(collectionMember);
        }

        if (TryBuildInlineFragment(_fragment, collectionMember, serializer, out var filter))
        {
            return filter;
        }

        // The strategies below only apply to JSONB-backed collections. Duplicated
        // array fields and dictionary members have their own storage semantics and
        // keep the sub-query strategy
        if (collectionMember is ChildCollectionMember or ValueCollectionMember)
        {
            // An OR spray of containment-eligible predicates beats a jsonpath filter
            // because each branch stays eligible for a GIN index (BitmapOr)
            if (tryBuildOrOfContainment(_fragment, collectionMember, serializer, out var orContainment))
            {
                return orContainment;
            }

            if (JsonPathExistsFilter.TryBuild(_fragment, collectionMember, serializer, out var jsonPath))
            {
                // Mixed AND (equality + something else): the equality conjuncts also go
                // out as a logically redundant containment pre-filter so a GIN index can
                // prune before the jsonpath predicate runs. Only at the document root —
                // a nested filter must stay a single ICollectionAwareFilter so that an
                // outer Any() can flatten it via MoveUnder()
                if (collectionMember.Ancestors[0] is DocumentQueryableMemberCollection &&
                    tryBuildContainmentPrefilter(_fragment, collectionMember, serializer, out var prefilter))
                {
                    return CompoundWhereFragment.And(prefilter, jsonPath);
                }

                return jsonPath;
            }
        }

        // Correlated EXISTS over the exploded elements beats the legacy explode-CTE +
        // ctid correlation by ~8x and composes at any nesting depth
        if (collectionMember is IExistsElementSource { ExplodedElementSource: not null } source)
        {
            return new ExistsCollectionFilter(collectionMember, _fragment, source.ExplodedElementSource!);
        }

        return new SubQueryFilter(collectionMember, _fragment);
    }

    private static bool tryBuildContainmentPrefilter(ISqlFragment fragment, ICollectionMember collectionMember,
        ISerializer serializer, [NotNullWhen(true)]out ISqlFragment? prefilter)
    {
        prefilter = default;

        if (fragment is not CompoundWhereFragment compound || !compound.Separator.ContainsIgnoreCase("and"))
        {
            return false;
        }

        var eligible = compound.Children
            .OfType<ICollectionAware>()
            .Where(x => x.CanReduceInChildCollection() && x.SupportsContainment())
            .ToArray();

        if (!eligible.Any())
        {
            return false;
        }

        var containment = new ContainmentWhereFilter(collectionMember, serializer)
        {
            Usage = ContainmentUsage.Collection
        };
        foreach (var child in eligible)
        {
            child.PlaceIntoContainmentFilter(containment);
        }

        prefilter = containment;
        return true;
    }

    private static bool tryBuildOrOfContainment(ISqlFragment fragment, ICollectionMember collectionMember,
        ISerializer serializer, [NotNullWhen(true)]out ISqlFragment? filter)
    {
        filter = default;

        if (fragment is not CompoundWhereFragment compound || compound.Separator.ContainsIgnoreCase("and"))
        {
            return false;
        }

        var branches = new List<ISqlFragment>();
        foreach (var child in compound.Children)
        {
            switch (child)
            {
                // ElementComparisonFilter reduces to its own single-value containment for
                // = / != even though it reports SupportsContainment() == false (it cannot
                // be merged with siblings), so CanReduceInChildCollection() is the gate here
                case ICollectionAware aware when aware.CanReduceInChildCollection() &&
                                                 (aware.SupportsContainment() || aware is ElementComparisonFilter):
                    branches.Add(aware.BuildFragment(collectionMember, serializer));
                    break;

                case CompoundWhereFragment inner when inner.Separator.ContainsIgnoreCase("and") &&
                                                      inner.Children.Any() &&
                                                      inner.Children.All(x =>
                                                          x is ICollectionAware ia &&
                                                          ia.CanReduceInChildCollection() && ia.SupportsContainment()):
                    var containment = new ContainmentWhereFilter(collectionMember, serializer)
                    {
                        Usage = ContainmentUsage.Collection
                    };
                    foreach (var ia in inner.Children.OfType<ICollectionAware>())
                    {
                        ia.PlaceIntoContainmentFilter(containment);
                    }

                    branches.Add(containment);
                    break;

                default:
                    return false;
            }
        }

        filter = CompoundWhereFragment.Or(branches.ToArray());
        return true;
    }
}
