#nullable enable
namespace Marten.Linq.Members.ValueCollections;

internal interface IValueCollectionMember : IQueryableMemberCollection
{
    public IQueryableMember Element { get; }
}
