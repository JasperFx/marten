#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Exceptions;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Schema.Identity;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal class StrongTypedIdMember<TOuter, TInner>: IdMember, IValueTypeMember
{
    private readonly Func<object, TInner> _innerValue;

    public StrongTypedIdMember(MemberInfo member, StrongTypedIdGeneration idGeneration) : base(member)
    {
        _innerValue = idGeneration.BuildInnerValueSource<TInner>();
    }

    public object ConvertFromWrapperArray(object values)
    {
        if (values is IEnumerable e)
        {
            var list = new List<TInner>();
            foreach (var outer in e.OfType<TOuter>())
            {
                list.Add(_innerValue(outer));
            }

            return list.ToArray();
        }

        throw new BadLinqExpressionException("Marten can not (yet) perform this query");

    }

    public override ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        if (constant.Value == null)
        {
            return op == "=" ? new IsNullFilter(this) : new IsNotNullFilter(this);
        }

        var def = new CommandParameter(Expression.Constant(_innerValue(constant.Value)));
        return new ComparisonFilter(this, def, op);
    }
}
