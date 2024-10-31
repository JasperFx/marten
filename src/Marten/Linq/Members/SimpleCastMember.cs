#nullable enable
using System.Reflection;
using Marten.Linq.Members.ValueCollections;
using Weasel.Core;
using Weasel.Core.Serialization;
using Weasel.Postgresql;

namespace Marten.Linq.Members;

public class SimpleCastMember: QueryableMember, IComparableMember
{
    internal static SimpleCastMember ForArrayIndex(ValueCollectionMember parent, ArrayIndexMember member)
    {
        var pgType = PostgresqlProvider.Instance.GetDatabaseType(parent.ElementType, EnumStorage.AsInteger);

        // CAST(d.data -> 'NumberArray' ->> 1 as integer)
        return new SimpleCastMember(parent, Casing.Default, member, pgType)
        {
            RawLocator = $"{parent.SimpleLocator} ->> {member.Index}",
            TypedLocator = $"CAST({parent.SimpleLocator} ->> {member.Index} as {pgType})"
        };
    }

    public SimpleCastMember(IQueryableMember parent, Casing casing, MemberInfo member, string pgType): base(parent,
        casing, member)
    {
        TypedLocator = $"CAST({RawLocator} as {pgType})";
    }
}
