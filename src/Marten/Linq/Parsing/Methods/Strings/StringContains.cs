#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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

internal class StringContains: StringComparisonParser
{
    public StringContains(): base(GetContainsMethods())
    {
    }

    private static MethodInfo[] GetContainsMethods()
    {
        return new[]
            {
                typeof(string).GetMethod("Contains", new[] { typeof(string), typeof(StringComparison) }),
                ReflectionHelper.GetMethod<string>(s => s.Contains(null)),
                ReflectionHelper.GetMethod<string>(s => s.Contains(null, StringComparison.CurrentCulture))
            }
            .Where(m => m != null)
            .Distinct()
            .ToArray();
    }

    protected override ISqlFragment buildFilter(bool caseInsensitive, IQueryableMember member, CommandParameter value)
    {
        return new StringContainsFilter(caseInsensitive, member, value);
    }
}

internal class StringContainsFilter: ISqlFragment, ICompiledQueryAwareFilter, ICollectionAware,
    IInlinedJsonPathValueFilter, INegationGuardedJsonPathFilter
{
    private readonly bool _caseInsensitive;
    private readonly IQueryableMember _member;
    private readonly string _rawValue;
    private MemberInfo _queryMember;

    public StringContainsFilter(bool caseInsensitive, IQueryableMember member, CommandParameter value)
    {
        _caseInsensitive = caseInsensitive;
        _member = member;
        _rawValue = value.Value as string;

    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_member.RawLocator);
        builder.Append(_caseInsensitive ? StringComparisonParser.CaseInSensitiveLike : StringComparisonParser.CaseSensitiveLike);

        builder.AppendParameter($"%{StringComparisonParser.EscapeValue(_rawValue)}%");

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
            var raw = (string?)CompiledQueryMemberReader.Read(member, query) ?? string.Empty;
            parameter.NpgsqlDbType = NpgsqlDbType.Varchar;
            parameter.Value = $"%{StringComparisonParser.EscapeValue(raw)}%";
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

    public bool CanBeJsonPathFilter() => _rawValue != null;

    public void BuildJsonPathFilter(ICommandBuilder builder, Dictionary<string, object> parameters)
    {
        StringComparisonParser.WriteJsonPathReference(_member, builder);
        builder.Append(" like_regex \"");
        builder.Append(StringComparisonParser.EscapeForJsonPathRegex(_rawValue));
        builder.Append(_caseInsensitive ? "\" flag \"i\"" : "\"");
    }

    public IEnumerable<DictionaryValueUsage> Values()
    {
        yield return new DictionaryValueUsage(_rawValue);
    }

    // "not containing" must also match elements whose member is null or missing —
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
