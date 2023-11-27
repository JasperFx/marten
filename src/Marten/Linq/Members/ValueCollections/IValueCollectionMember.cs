namespace Marten.Linq.Members.ValueCollections;

internal interface IValueCollectionMember : IQueryableMemberCollection
{
    IQueryableMember Element { get; }
}