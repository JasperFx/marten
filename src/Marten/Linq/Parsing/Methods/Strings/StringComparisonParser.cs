#nullable enable
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core;
using Marten.Exceptions;
using Marten.Linq.Members;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods.Strings;

internal abstract class StringComparisonParser: IMethodCallParser
{
    public const string CaseSensitiveLike = " LIKE ";
    public const string CaseInSensitiveLike = " ILIKE ";

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

        value.DbType = NpgsqlDbType.Varchar;

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

    public static string EscapeValue(string raw)
    {
        return raw.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }

    /// <summary>
    ///     Writes the jsonpath reference for a member inside a collection filter:
    ///     "@.Member.Path" for element members, or the bare "@" when the collection
    ///     element itself is the value being compared (scalar string collections)
    /// </summary>
    internal static void WriteJsonPathReference(IQueryableMember member, ICommandBuilder builder)
    {
        // a scalar collection element IS the value under test — its JsonPathSegment
        // ("data") is only meaningful for the explode/unnest strategy
        if (member is Marten.Linq.Members.ValueCollections.SimpleElementMember)
        {
            builder.Append("@");
            return;
        }

        var path = member.WriteJsonPath();
        builder.Append("@");
        if (path.IsNotEmpty())
        {
            builder.Append(".");
            builder.Append(path);
        }
    }

    /// <summary>
    ///     Prepares a raw search value for embedding inside a like_regex "..." pattern
    ///     that itself lives inside a single-quoted SQL jsonpath literal. Regex-escapes
    ///     first, then escapes for the jsonpath string literal (backslash, double quote),
    ///     then doubles single quotes for the enclosing SQL string.
    /// </summary>
    public static string EscapeForJsonPathRegex(string raw)
    {
        return System.Text.RegularExpressions.Regex.Escape(raw)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("'", "''");
    }
}

