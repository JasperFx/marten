#nullable enable
using System;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Internal.CompiledQueries;
using Marten.Linq.Members;
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

internal class StringEndsWithFilter: ISqlFragment, ICompiledQueryAwareFilter
{
    private readonly IQueryableMember _member;
    private readonly string _operator;
    private readonly object _rawValue;
    private MemberInfo _queryMember;

    public StringEndsWithFilter(bool caseInsensitive, IQueryableMember member, CommandParameter value)
    {
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
}


