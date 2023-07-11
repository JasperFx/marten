using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal class ChildDocument: QueryableMember, IQueryableMemberCollection, IComparableMember
{
    private readonly StoreOptions _options;
    private ImHashMap<string, IQueryableMember> _members = ImHashMap<string, IQueryableMember>.Empty;


    public ChildDocument(StoreOptions options, IQueryableMember parent, Casing casing, MemberInfo member): base(
        parent, casing, member)
    {
        _options = options;

        RawLocator = TypedLocator = $"{parent.RawLocator} -> '{MemberName}'";

        NullTestLocator = $"{parent.RawLocator} ->> '{MemberName}'";

        ElementType = member.GetMemberType();
    }

    public override ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        if (constant == null || constant.Value == null)
        {
            switch (op)
            {
                case "=":
                    return new IsNullFilter(this);

                case "!=":
                    return new IsNotNullFilter(this);
            }
        }

        throw new BadLinqExpressionException(
            "Marten cannot support custom value types in Linq expression. Please query on either simple properties of the value type, or register a custom IMemberSource for this value type.");
    }

    public Type ElementType { get; }


    public override IQueryableMember FindMember(MemberInfo member)
    {
        if (_members.TryFind(member.Name, out var m))
        {
            return m;
        }

        m = _options.CreateQueryableMember(member, this);
        _members = _members.AddOrUpdate(member.Name, m);

        return m;
    }

    public override void ReplaceMember(MemberInfo member, IQueryableMember queryableMember)
    {
        _members = _members.AddOrUpdate(member.Name, queryableMember);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IEnumerator<IQueryableMember> GetEnumerator()
    {
        return _members.Enumerate().Select(x => x.Value).GetEnumerator();
    }
}
