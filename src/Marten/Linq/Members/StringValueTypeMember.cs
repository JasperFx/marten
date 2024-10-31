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
using Weasel.Core.Serialization;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

public class StringValueTypeMember<T>: StringMember, IValueTypeMember
{
    private readonly Func<T, string> _valueSource;
    private readonly IScalarSelectClause _selector;

    public StringValueTypeMember(IQueryableMember parent, Casing casing, MemberInfo member, ValueTypeInfo valueTypeInfo) : base(parent, casing, member)
    {
        _valueSource = valueTypeInfo.ValueAccessor<T, string>();
        var converter = valueTypeInfo.CreateConverter<T, string>();

        if (typeof(T).IsClass)
        {
            _selector = typeof(ClassValueTypeSelectClause<,>).CloseAndBuildAs<IScalarSelectClause>(
                TypedLocator, converter,
                valueTypeInfo.OuterType,
                typeof(string));
        }
        else
        {
            _selector = typeof(ValueTypeSelectClause<,>).CloseAndBuildAs<IScalarSelectClause>(
                TypedLocator, converter,
                valueTypeInfo.OuterType,
                typeof(string));
        }
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

    public object ConvertFromWrapperArray(object values)
    {
        if (values is IEnumerable e)
        {
            var list = new List<string>();
            foreach (var outer in e.OfType<T>()) list.Add(_valueSource(outer));

            return list.ToArray();
        }

        throw new BadLinqExpressionException("Marten can not (yet) perform this query");
    }

    public ISelectClause BuildSelectClause(string fromObject)
    {
        return _selector.CloneToOtherTable(fromObject);
    }
}
