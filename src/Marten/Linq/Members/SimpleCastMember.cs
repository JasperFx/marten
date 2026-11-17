#nullable enable
using System;
using System.Reflection;
using Marten.Linq.Members.ValueCollections;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Linq.Members;

public class SimpleCastMember: QueryableMember, IComparableMember
{
    internal static SimpleCastMember ForArrayIndex(ValueCollectionMember parent, ArrayIndexMember member)
    {
        // CAST(d.data -> 'NumberArray' ->> 1 as integer)
        return ForIndexedElement(parent, parent.SimpleLocator, member, parent.ElementType);
    }

    /// <summary>
    ///     #5949: the scalar element at <paramref name="member" /> of the jsonb array at
    ///     <paramref name="jsonbLocator" />, which may itself be an element of another array (<c>x.Grid[1][0]</c>).
    /// </summary>
    internal static SimpleCastMember ForIndexedElement(IQueryableMember parent, string jsonbLocator,
        ArrayIndexMember member, Type elementType)
    {
        var pgType = PostgresqlProvider.Instance.GetDatabaseType(elementType, EnumStorage.AsInteger);

        return new SimpleCastMember(parent, Casing.Default, member, pgType)
        {
            RawLocator = $"{jsonbLocator} ->> {member.Index}",
            TypedLocator = $"CAST({jsonbLocator} ->> {member.Index} as {pgType})",
            MemberType = elementType
        };
    }

    public SimpleCastMember(IQueryableMember parent, Casing casing, MemberInfo member, string pgType): base(parent,
        casing, member)
    {
        TypedLocator = $"CAST({RawLocator} as {pgType})";
    }
}
