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

internal class StringStartsWith: StringComparisonParser
{
    public StringStartsWith(): base(
        ReflectionHelper.GetMethod<string>(s => s.StartsWith(null!))!,
        ReflectionHelper.GetMethod<string>(s => s.StartsWith(null!, StringComparison.CurrentCulture))!
    )
    {
    }

    protected override ISqlFragment buildFilter(bool caseInsensitive, IQueryableMember member, CommandParameter value)
    {
        return new StringStartsWithFilter(caseInsensitive, member, value);
    }
}

internal class StringStartsWithFilter: ISqlFragment, ICompiledQueryAwareFilter, ICollectionAware,
    IInlinedJsonPathValueFilter, INegationGuardedJsonPathFilter
{
    private readonly bool _caseInsensitive;
    private readonly IQueryableMember _member;
    private readonly string _operator;
    private readonly string _rawValue;
    private MemberInfo _queryMember;

    public StringStartsWithFilter(bool caseInsensitive, IQueryableMember member, CommandParameter value)
    {
        _caseInsensitive = caseInsensitive;
        _member = member;
        _rawValue = value.Value as string;
        _operator = caseInsensitive
            ? StringComparisonParser.CaseInSensitiveLike
            : StringComparisonParser.CaseSensitiveLike;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_member.RawLocator);
        builder.Append(_operator);
        builder.AppendParameter($"{StringComparisonParser.EscapeValue(_rawValue)}%");

        ParameterName = builder.LastParameterName!;
    }

    public string ParameterName { get; private set; }

    public bool TryMatchValue(object value, MemberInfo member)
    {
        if (!_rawValue.Equals(value))
            return false;

        _queryMember = member;
        return true;
    }

    public Action<NpgsqlParameter, object> BuildSetter()
    {
        var member = _queryMember;
        return (parameter, query) =>
        {
            var raw = (string?)CompiledQueryMemberReader.Read(member, query) ?? string.Empty;
            parameter.NpgsqlDbType = NpgsqlDbType.Varchar;
            parameter.Value = $"{StringComparisonParser.EscapeValue(raw)}%";
        };
    }

    // The case-sensitive form rides the parameterized jsonpath `starts with $var`;
    // the case-insensitive form has to inline a like_regex pattern
    bool IInlinedJsonPathValueFilter.JsonPathValueIsInlined => _caseInsensitive;

    public bool CanReduceInChildCollection() => false;

    public ICollectionAwareFilter BuildFragment(ICollectionMember member,
        ISerializer serializer)
    {
        throw new NotSupportedException();
    }

    public bool SupportsContainment() => false;

    public void PlaceIntoContainmentFilter(ContainmentWhereFilter filter)
    {
        throw new NotSupportedException();
    }

    public bool CanBeJsonPathFilter() => _rawValue != null;

    public void BuildJsonPathFilter(ICommandBuilder builder,
        Dictionary<string, object> parameters)
    {
        StringComparisonParser.WriteJsonPathReference(_member, builder);

        if (_caseInsensitive)
        {
            builder.Append(" like_regex \"^");
            builder.Append(StringComparisonParser.EscapeForJsonPathRegex(_rawValue));
            builder.Append("\" flag \"i\"");
        }
        else
        {
            builder.Append(" starts with ");
            builder.Append(parameters.AddJsonPathParameter(_rawValue));
        }
    }

    public IEnumerable<DictionaryValueUsage> Values()
    {
        yield return new DictionaryValueUsage(_rawValue);
    }

    // "not starting with" must also match elements whose member is null or missing —
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
