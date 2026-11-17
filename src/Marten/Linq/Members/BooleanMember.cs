#nullable enable
using System;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Linq.Members.ValueCollections;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal class BooleanMember: QueryableMember, IComparableMember, IBooleanMember
{
    private readonly bool _isNullable;

    public BooleanMember(IQueryableMember parent, Casing casing, MemberInfo member, string pgType): base(parent,
        casing, member)
    {
        TypedLocator = $"CAST({RawLocator} as {pgType})";

        // #5949: the element of a bool[] or List<bool> is an ElementMember, which has no member type of its own.
        // Reading it threw, so no bool collection on a document could be queried at all.
        _isNullable = (member is ElementMember element ? element.ReflectedType : member.GetRawMemberType())!
            .IsNullable();
    }

    public ISqlFragment BuildIsTrueFragment()
    {
        return new BooleanFieldIsTrue(this);
    }

    public override ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        if (constant.Value == null)
        {
            return op == "=" ? new IsNullFilter(this) : new IsNotNullFilter(this);
        }

        if (_isNullable && op == "!=")
        {
            if (constant.Value.Equals(true))
            {
                return CompoundWhereFragment.Or(new IsNullFilter(this), base.CreateComparison(op, constant));
            }
            else
            {
                return CompoundWhereFragment.Or(new IsNullFilter(this), base.CreateComparison(op, constant));
            }
        }

        return base.CreateComparison(op, constant);
    }
}
