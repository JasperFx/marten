#nullable enable
using System;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

public class DateTimeOffsetMember: QueryableMember, IComparableMember
{
    public DateTimeOffsetMember(StoreOptions options, IQueryableMember parent, Casing casing, MemberInfo member): base(
        parent, casing, member)
    {
        TypedLocator = $"{options.DatabaseSchemaName}.mt_immutable_timestamptz({RawLocator})";
    }

    public override string SelectorForDuplication(string pgType)
    {
        return TypedLocator.Replace("d.data ->", "data ->");
    }

    public override ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        if (constant.Value == null)
        {
            return op == "=" ? new IsNullFilter(this) : new IsNotNullFilter(this);
        }

        var value = (DateTimeOffset)constant.Value;

        var def = new CommandParameter(value.ToUniversalTime());
        return new MemberComparisonFilter(this, def, op);
    }
}
