using System;
using System.Linq;
using JasperFx.Core;
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
        ISerializer serializer, out ICollectionAwareFilter filter)
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

        return new SubQueryFilter(collectionMember, _fragment);
    }
}
