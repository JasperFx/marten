#nullable enable
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration.Filters;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods.Strings;

/// <summary>
///     Translates Regex.IsMatch(x.Member, pattern) to the PostgreSQL regex match
///     operator (~ / ~* for RegexOptions.IgnoreCase) on top-level members, and to a
///     jsonpath like_regex predicate inside collection filters. Note that PostgreSQL
///     regular expressions are POSIX-flavored (and like_regex is the SQL/JSON XQuery
///     subset) — most everyday patterns behave like .NET's, but the dialects are not
///     identical
/// </summary>
internal class RegexIsMatch: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method.DeclaringType == typeof(Regex)
               && expression.Method.Name == nameof(Regex.IsMatch)
               && expression.Method.IsStatic
               && expression.Arguments.Count is 2 or 3
               && expression.Arguments[0].Type == typeof(string);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var member = memberCollection.MemberFor(expression.Arguments[0]);
        var pattern = expression.Arguments[1].ReduceToConstant().Value() as string;

        if (pattern == null)
        {
            throw new BadLinqExpressionException(
                $"Marten needs a constant (or query-time resolvable) pattern for Regex.IsMatch(): '{expression}'");
        }

        var ignoreCase = false;
        if (expression.Arguments.Count == 3)
        {
            var regexOptions = (RegexOptions)expression.Arguments[2].ReduceToConstant().Value()!;
            ignoreCase = regexOptions.HasFlag(RegexOptions.IgnoreCase);

            var unsupported = regexOptions & ~RegexOptions.IgnoreCase;
            if (unsupported != RegexOptions.None)
            {
                throw new BadLinqExpressionException(
                    $"Marten can only translate RegexOptions.IgnoreCase to SQL, not '{unsupported}'");
            }
        }

        return new RegexIsMatchFilter(member, pattern, ignoreCase);
    }
}

internal class RegexIsMatchFilter: ISqlFragment, ICollectionAware, IInlinedJsonPathValueFilter,
    INegationGuardedJsonPathFilter
{
    private readonly bool _ignoreCase;
    private readonly IQueryableMember _member;
    private readonly string _pattern;

    public RegexIsMatchFilter(IQueryableMember member, string pattern, bool ignoreCase)
    {
        _member = member;
        _pattern = pattern;
        _ignoreCase = ignoreCase;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_member.RawLocator);
        builder.Append(_ignoreCase ? " ~* " : " ~ ");
        builder.AppendParameter(_pattern, NpgsqlDbType.Varchar);
    }

    // like_regex patterns cannot come from the vars parameter
    bool IInlinedJsonPathValueFilter.JsonPathValueIsInlined => true;

    public bool CanReduceInChildCollection() => false;

    public ICollectionAwareFilter BuildFragment(ICollectionMember member, ISerializer serializer)
    {
        throw new NotSupportedException();
    }

    public bool SupportsContainment() => false;

    public void PlaceIntoContainmentFilter(ContainmentWhereFilter filter)
    {
        throw new NotSupportedException();
    }

    public bool CanBeJsonPathFilter() => true;

    public void BuildJsonPathFilter(ICommandBuilder builder, Dictionary<string, object> parameters)
    {
        StringComparisonParser.WriteJsonPathReference(_member, builder);
        builder.Append(" like_regex \"");

        // the value IS a regex pattern — escape only for the jsonpath string literal
        // and the enclosing SQL string, never Regex.Escape
        builder.Append(_pattern
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("'", "''"));

        builder.Append(_ignoreCase ? "\" flag \"i\"" : "\"");
    }

    public IEnumerable<DictionaryValueUsage> Values()
    {
        yield return new DictionaryValueUsage(_pattern);
    }

    // a non-matching null/missing member must fail the predicate like it does in C#
    public void BuildNegatedJsonPathFilter(ICommandBuilder builder, Dictionary<string, object> parameters)
    {
        builder.Append("(!(");
        StringComparisonParser.WriteJsonPathReference(_member, builder);
        builder.Append(".type() == \"string\") || !(");
        BuildJsonPathFilter(builder, parameters);
        builder.Append("))");
    }
}
