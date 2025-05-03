#nullable enable
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.Members;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

/// <summary>
///     Implement Equals for <see cref="int" />, <see cref="long" />, <see cref="decimal" />, <see cref="Guid" />,
///     <see cref="bool" />, <see cref="DateTime" />, <see cref="DateTimeOffset" />.
/// </summary>
/// <remarks>
///     Equals(object) calls into <see cref="Convert.ChangeType(object, Type)" />. Equals(null) is converted to "is
///     null" query.
/// </remarks>
internal class SimpleEqualsParser: IMethodCallParser
{
    private static readonly List<Type> SupportedTypes = new()
    {
        typeof(int),
        typeof(long),
        typeof(decimal),
        typeof(Guid),
        typeof(bool)
    };

    private readonly string _equalsOperator;
    private readonly string _isOperator;
    private readonly bool _supportContainment;

    static SimpleEqualsParser()
    {
        SupportedTypes.AddRange(PostgresqlProvider.Instance.ResolveTypes(NpgsqlDbType.Timestamp));
        SupportedTypes.AddRange(PostgresqlProvider.Instance.ResolveTypes(NpgsqlDbType.TimestampTz));
        SupportedTypes.Add(typeof(double));
    }

    public SimpleEqualsParser(string equalsOperator = "=", string isOperator = "is", bool supportContainment = true)
    {
        _equalsOperator = equalsOperator;
        _isOperator = isOperator;
        _supportContainment = supportContainment;
    }

    public bool Matches(MethodCallExpression expression)
    {
        return SupportedTypes.Contains(expression.Method.DeclaringType!) &&
               expression.Method.Name.Equals("Equals", StringComparison.Ordinal);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var leftType = expression.Object?.Type;
        var rightType = expression.Arguments[0].Type;

        if (leftType != null)
        {
            if (!rightType.CanBeCastTo(leftType))
            {
                throw new BadLinqExpressionException(
                    $"Mismatched types in Equals() usage in expression '{expression}'");
            }
        }

        var left = new SimpleExpression(memberCollection, expression.Object);
        var right = new SimpleExpression(memberCollection, expression.Arguments[0]);

        return left.CompareTo(right, _equalsOperator);
    }
}
