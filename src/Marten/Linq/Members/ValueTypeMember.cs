#nullable enable
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal class ValueTypeMember<TOuter, TInner>: SimpleCastMember
{
    private readonly Func<TOuter, TInner> _valueSource;

    public ValueTypeMember(IQueryableMember parent, Casing casing, MemberInfo member, StrongTypedIdInfo strongTypedIdInfo) : base(parent, casing, member, PostgresqlProvider.Instance.GetDatabaseType(strongTypedIdInfo.SimpleType, EnumStorage.AsInteger))
    {
        _valueSource = strongTypedIdInfo.ValueAccessor<TOuter, TInner>();
    }

    public override void PlaceValueInDictionaryForContainment(Dictionary<string, object> dict, ConstantExpression constant)
    {
        var rawValue = constant.Value;
        if (rawValue is TOuter value)
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

        var value = _valueSource(constant.Value.As<TOuter>());
        var def = new CommandParameter(Expression.Constant(value));
        return new MemberComparisonFilter(this, def, op);
    }
}
