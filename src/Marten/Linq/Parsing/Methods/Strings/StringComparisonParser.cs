using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Exceptions;
using Marten.Linq.Members;
using NpgsqlTypes;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods.Strings;

internal abstract class StringComparisonParser: IMethodCallParser
{
    public const string CaseSensitiveLike = " LIKE ";
    public const string CaseInSensitiveLike = " ILIKE ";

    // Do not use escape char when using case insensitivity
    // this way backslash does not have special meaning and works as string literal
    public const string EscapeSuffix = " ESCAPE '' ";

    public static readonly StringComparison[] CaseInSensitiveComparisons =
    {
        StringComparison.CurrentCultureIgnoreCase, StringComparison.InvariantCultureIgnoreCase,
        StringComparison.OrdinalIgnoreCase
    };

    private readonly MethodInfo[] _supportedMethods;

    public StringComparisonParser(params MethodInfo[] supportedMethods)
    {
        _supportedMethods = supportedMethods;
    }

    public bool Matches(MethodCallExpression expression)
    {
        return _supportedMethods.Any(m => AreMethodsEqual(m, expression.Method));
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        IQueryableMember member = null;
        CommandParameter value = null;
        var comparison = StringComparison.CurrentCulture;

        SimpleExpression left;
        SimpleExpression right;

        if (expression.Object != null)
        {
            left = new SimpleExpression(memberCollection, expression.Object);
            right = new SimpleExpression(memberCollection, expression.Arguments[0]);
        }
        else
        {
            left = new SimpleExpression(memberCollection, expression.Arguments[0]);
            right = new SimpleExpression(memberCollection, expression.Arguments[1]);
        }

        if (left.Member != null)
        {
            member = left.Member;
            value = right.FindValueFragment() as CommandParameter;
        }
        else
        {
            member = right.Member;
            value = left.FindValueFragment() as CommandParameter;
        }

        if (member == null || value == null)
        {
            throw new BadLinqExpressionException("Marten was not able to create a string comparison for " + expression);
        }

        if (expression.Arguments.Last().Type == typeof(StringComparison))
        {
            comparison = (StringComparison)expression.Arguments.Last().Value();
        }

        var caseInsensitive = CaseInSensitiveComparisons.Contains(comparison);

        // TODO -- watch the NULL values!
        return buildFilter(caseInsensitive, member, value);
    }

    protected abstract ISqlFragment buildFilter(bool caseInsensitive, IQueryableMember member, CommandParameter value);

    protected bool AreMethodsEqual(MethodInfo method1, MethodInfo method2)
    {
        return method1.DeclaringType == method2.DeclaringType && method1.Name == method2.Name
                                                              && method1.GetParameters().Select(p => p.ParameterType)
                                                                  .SequenceEqual(method2.GetParameters()
                                                                      .Select(p => p.ParameterType));
    }

}

