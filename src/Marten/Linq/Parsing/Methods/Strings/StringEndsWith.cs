#nullable enable
using System;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten.Internal.CompiledQueries;
using Marten.Linq.Members;
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

    public void Apply(IPostgresqlCommandBuilder builder)
    {
        builder.Append(_member.RawLocator);
        builder.Append(_operator);

        var mask = "%" + StringComparisonParser.EscapeValue(_rawValue.ToString());

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

    public void GenerateCode(GeneratedMethod method, int parameterIndex, string parametersVariableName)
    {
        var maskedValue = $"EndsWith(_query.{_queryMember.Name})";

        method.Frames.Code($"{parametersVariableName}[{parameterIndex}].Value = {maskedValue};");
    }

    public string ParameterName { get; private set; }
}


