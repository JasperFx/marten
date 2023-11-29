using System;
using System.Collections.Generic;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

internal class CollectionIsNotEmpty: IReversibleWhereFragment, ICollectionAware, ICollectionAwareFilter
{
    private readonly string _text;

    public CollectionIsNotEmpty(ICollectionMember member)
    {
        CollectionMember = member;
        var jsonPath = member.WriteJsonPath();
        _text = $"d.data @? '$ ? (@.{jsonPath} != null && @.{jsonPath}.size() > 0)'";
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

    public void BuildJsonPathFilter(CommandBuilder builder, Dictionary<string, object> parameters)
    {
        // TODO -- come back to this later with
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
        return new DeepCollectionIsNotEmpty(path);
    }

    public void Apply(CommandBuilder builder)
    {
        builder.Append(_text);
    }

    public bool Contains(string sqlText)
    {
        return false;
    }

    public ISqlFragment Reverse()
    {
        return new CollectionIsEmpty(CollectionMember);
    }
}
