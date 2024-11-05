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

internal class StringStartsWithFilter: ISqlFragment, ICompiledQueryAwareFilter
{
    private readonly IQueryableMember _member;
    private readonly string _operator;
    private readonly string _rawValue;
    private MemberInfo _queryMember;

    public StringStartsWithFilter(bool caseInsensitive, IQueryableMember member, CommandParameter value)
    {
        _member = member;
        _rawValue = value.Value as string;
        _operator = caseInsensitive
            ? StringComparisonParser.CaseInSensitiveLike
            : StringComparisonParser.CaseSensitiveLike;
    }

    public void Apply(IPostgresqlCommandBuilder builder)
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

    public void GenerateCode(GeneratedMethod method, int parameterIndex, string parametersVariableName)
    {
        var maskedValue = $"StartsWith(_query.{_queryMember.Name})";

        method.Frames.Code($"{parametersVariableName}[{parameterIndex}].Value = {maskedValue};");
    }
}
