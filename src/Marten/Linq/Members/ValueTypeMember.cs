#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Linq.SqlGeneration;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

public interface IValueTypeMember<TOuter, TInner>: IQueryableMember
{
    IEnumerable<TInner> ConvertFromWrapperArray(IEnumerable<TOuter> values);
    ISelectClause BuildSelectClause(string fromObject);
}

public class ValueTypeMember<TOuter, TInner>: SimpleCastMember, IValueTypeMember<TOuter, TInner>
{
    private readonly Func<TOuter, TInner> _valueSource;
    private readonly IScalarSelectClause _selector;

    public ValueTypeMember(IQueryableMember parent, Casing casing, MemberInfo member, ValueTypeInfo valueTypeInfo):
        base(parent, casing, member,
            PostgresqlProvider.Instance.GetDatabaseType(valueTypeInfo.SimpleType, EnumStorage.AsInteger))
    {
        _valueSource = valueTypeInfo.UnWrapper<TOuter, TInner>();
        var converter = valueTypeInfo.CreateWrapper<TOuter, TInner>();

        if (typeof(TOuter).IsClass)
        {
            _selector = typeof(ClassValueTypeSelectClause<,>).CloseAndBuildAs<IScalarSelectClause>(
                TypedLocator, converter,
                valueTypeInfo.OuterType,
                valueTypeInfo.SimpleType);
        }
        else
        {
            _selector = typeof(ValueTypeSelectClause<,>).CloseAndBuildAs<IScalarSelectClause>(
                TypedLocator, converter,
                valueTypeInfo.OuterType,
                valueTypeInfo.SimpleType);
        }

    }

    public override void PlaceValueInDictionaryForContainment(Dictionary<string, object> dict,
        ConstantExpression constant)
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

        var value = constant.Value is TInner ? (TInner)constant.Value : _valueSource(constant.Value.As<TOuter>());

        var def = new CommandParameter(Expression.Constant(value));
        return new MemberComparisonFilter(this, def, op);
    }

    public IEnumerable<TInner> ConvertFromWrapperArray(IEnumerable<TOuter> values)
    {
        if (values is IEnumerable e)
        {
            var list = new List<TInner>();
            foreach (var outer in e.OfType<TOuter>()) list.Add(_valueSource(outer));

            return list.ToArray();
        }

        throw new BadLinqExpressionException("Marten can not (yet) perform this query");
    }

    public ISelectClause BuildSelectClause(string fromObject)
    {
        return _selector.CloneToOtherTable(fromObject);
    }
}
