#nullable enable
using System;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Util;
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
        return TypedLocator.RemoveTableAlias("d");
    }

    public override ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        var unwrappedValue = constant.UnwrapValue();
        if (unwrappedValue == null)
        {
            return op == "=" ? new IsNullFilter(this) : new IsNotNullFilter(this);
        }

        var value = (DateTimeOffset)unwrappedValue;

        var def = new CommandParameter(value.ToUniversalTime());
        return new MemberComparisonFilter(this, def, op);
    }
}
