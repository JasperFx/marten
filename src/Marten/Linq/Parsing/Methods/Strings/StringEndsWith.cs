#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Internal.CompiledQueries;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration.Filters;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods.Strings;

internal class StringEndsWith: StringComparisonParser
{
    private static readonly StringComparison[] CaseInsensitiveComparisons =
    {
        StringComparison.OrdinalIgnoreCase, StringComparison.CurrentCultureIgnoreCase
    };

    public StringEndsWith(): base(
        ReflectionHelper.GetMethod<string>(s => s.EndsWith(null))!,
        ReflectionHelper.GetMethod<string>(s => s.EndsWith(null, StringComparison.CurrentCulture))!
    )
    {
    }

    protected override ISqlFragment buildFilter(bool caseInsensitive, IQueryableMember member, CommandParameter value)
    {
        return new StringEndsWithFilter(caseInsensitive, member, value);
    }
}

internal class StringEndsWithFilter: ISqlFragment, ICompiledQueryAwareFilter, ICollectionAware,
    IInlinedJsonPathValueFilter, INegationGuardedJsonPathFilter
{
    private readonly bool _caseInsensitive;
    private readonly IQueryableMember _member;
    private readonly string _operator;
    private readonly object _rawValue;
    private MemberInfo _queryMember;

    public StringEndsWithFilter(bool caseInsensitive, IQueryableMember member, CommandParameter value)
    {
        _caseInsensitive = caseInsensitive;
        _member = member;
        _rawValue = value.Value;

        _operator = caseInsensitive
            ? StringComparisonParser.CaseInSensitiveLike
            : StringComparisonParser.CaseSensitiveLike;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_member.RawLocator);
        builder.Append(_operator);

        var mask = "%" + StringComparisonParser.EscapeValue(_rawValue.ToString()!);

        builder.AppendParameter(mask);

        ParameterName = builder.LastParameterName;
    }

    public bool TryMatchValue(object value, MemberInfo member)
    {
        if (_rawValue.Equals(value))
        {
            _queryMember = member;
            return true;
        }

        return false;
    }

    public Action<NpgsqlParameter, object> BuildSetter()
    {
        var member = _queryMember;
        return (parameter, query) =>
        {
            var raw = CompiledQueryMemberReader.Read(member, query)?.ToString() ?? string.Empty;
            parameter.NpgsqlDbType = NpgsqlDbType.Varchar;
            parameter.Value = $"%{StringComparisonParser.EscapeValue(raw)}";
        };
    }

    public string ParameterName { get; private set; }

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

    public bool CanBeJsonPathFilter() => _rawValue is string;

    public void BuildJsonPathFilter(ICommandBuilder builder, Dictionary<string, object> parameters)
    {
        StringComparisonParser.WriteJsonPathReference(_member, builder);
        builder.Append(" like_regex \"");
        builder.Append(StringComparisonParser.EscapeForJsonPathRegex((string)_rawValue));
        builder.Append(_caseInsensitive ? "$\" flag \"i\"" : "$\"");
    }

    public IEnumerable<DictionaryValueUsage> Values()
    {
        yield return new DictionaryValueUsage(_rawValue);
    }

    // "not ending with" must also match elements whose member is null or missing —
    // jsonpath string ops evaluate to UNKNOWN there, so guard on the value type first
    public void BuildNegatedJsonPathFilter(ICommandBuilder builder, Dictionary<string, object> parameters)
    {
        builder.Append("(!(");
        StringComparisonParser.WriteJsonPathReference(_member, builder);
        builder.Append(".type() == \"string\") || !(");
        BuildJsonPathFilter(builder, parameters);
        builder.Append("))");
    }
}


