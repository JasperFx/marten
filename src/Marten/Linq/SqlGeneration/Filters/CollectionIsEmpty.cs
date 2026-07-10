#nullable enable
using System;
using System.Collections.Generic;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

internal class CollectionIsEmpty: IReversibleWhereFragment, ICollectionAware, ICollectionAwareFilter
{
    private readonly string _text;

    public CollectionIsEmpty(ICollectionMember member)
    {
        CollectionMember = member;
        // deeply ashamed by the replace here, but it does work.
        var jsonPath = member.WriteJsonPath().Replace("[*]", "");
        _text = $"d.data @? '$ ? (@.{jsonPath} == null || @.{jsonPath}.size() == 0)'";
    }

    public ISqlFragment Reverse()
    {
        return CollectionMember.NotEmpty;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_text);
    }

    public bool CanReduceInChildCollection()
    {
        return true;
    }

    public ICollectionAwareFilter BuildFragment(ICollectionMember member, ISerializer serializer)
    {
        return this;
    }

    public bool SupportsContainment()
    {
        return false;
    }

    public void PlaceIntoContainmentFilter(ContainmentWhereFilter filter)
    {
        throw new NotSupportedException();
    }

    public bool CanBeJsonPathFilter()
    {
        return false;
    }

    public void BuildJsonPathFilter(ICommandBuilder builder, Dictionary<string, object> parameters)
    {
        throw new NotSupportedException();
    }

    public IEnumerable<DictionaryValueUsage> Values()
    {
        yield break;
    }

    public ICollectionMember CollectionMember { get; }

    public ISqlFragment MoveUnder(ICollectionMember ancestorCollection)
    {
        var path = new List<IQueryableMember>(ancestorCollection.Ancestors);
        path.Add(ancestorCollection);
        path.Add(CollectionMember);
        return new DeepCollectionIsEmpty(path);
    }
}
