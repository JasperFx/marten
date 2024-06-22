#nullable enable
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal class StringValueTypeMember<T>: StringMember
{
    private readonly Func<T, string> _valueSource;

    public StringValueTypeMember(IQueryableMember parent, Casing casing, MemberInfo member, StrongTypedIdInfo strongTypedIdInfo) : base(parent, casing, member)
    {
        _valueSource = strongTypedIdInfo.CreateConverter<string, T>();
    }

    public override void PlaceValueInDictionaryForContainment(Dictionary<string, object> dict, ConstantExpression constant)
    {
        var rawValue = constant.Value;
        if (rawValue is T value)
        {
            dict[MemberName] = _valueSource(value);
        }
    }

    public override ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        if (constant.Value == null)
        {
            return op == "=" ? new IsNullFilter(this) : new IsNotNullFilter(this);
        }

        var value = _valueSource(constant.Value.As<T>());
        var def = new CommandParameter(Expression.Constant(value));
        return new MemberComparisonFilter(this, def, op);
    }
}
