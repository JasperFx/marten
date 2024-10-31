#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Core.Serialization;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

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
        if (Wheres.All(x => x is ICollectionAware aware && aware.CanBeJsonPathFilter()))
        {
            return new ChildCollectionJsonPathCountFilter(_collection, _serializer, Wheres.OfType<ICollectionAware>(),
                op, constant);
        }

        throw new BadLinqExpressionException(
            "Marten does not (yet) support this pattern for child collection.Count() queries");
    }

    void IWhereFragmentHolder.Register(ISqlFragment fragment)
    {
        Wheres.Add(fragment);
    }
}
