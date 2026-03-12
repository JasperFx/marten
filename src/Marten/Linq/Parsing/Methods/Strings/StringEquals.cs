#nullable enable
using System;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten.Internal.CompiledQueries;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration.Filters;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods.Strings;

internal class StringEquals: StringComparisonParser
{
    public StringEquals(): base(
        ReflectionHelper.GetMethod<string>(s => s.Equals(string.Empty))!,
        ReflectionHelper.GetMethod<string>(s => s.Equals(string.Empty, StringComparison.CurrentCulture))!,
        ReflectionHelper.GetMethod(() => string.Equals(string.Empty, string.Empty))!,
        ReflectionHelper.GetMethod(() => string.Equals(string.Empty, string.Empty, StringComparison.CurrentCulture))!)
    {
    }

    protected override ISqlFragment buildFilter(bool caseInsensitive, IQueryableMember member, CommandParameter value)
    {
        return caseInsensitive
            ? new StringEqualsIgnoreCaseFilter(member, value)
            : new MemberComparisonFilter(member, value, "=");
    }
}

internal class StringEqualsIgnoreCaseFilter : ISqlFragment, ICompiledQueryAwareFilter
{
    public IQueryableMember Member { get; }
    public CommandParameter Value { get; }
    private readonly string _rawValue;
    private MemberInfo? _queryMember;

    public StringEqualsIgnoreCaseFilter(IQueryableMember member, CommandParameter value)
    {
        Member = member;
        _rawValue = value.Value as string ?? string.Empty;
        Value = new CommandParameter(StringComparisonParser.EscapeValue(_rawValue));
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(Member.RawLocator);
        builder.Append(StringComparisonParser.CaseInSensitiveLike);
        builder.AppendParameter(StringComparisonParser.EscapeValue(_rawValue));
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
        var maskedValue = $"EqualsIgnoreCaseValue(_query.{_queryMember!.Name})";

        method.Frames.Code($@"
{parametersVariableName}[{parameterIndex}].NpgsqlDbType = {{0}};
{parametersVariableName}[{parameterIndex}].Value = {maskedValue};
", NpgsqlDbType.Varchar);
    }

    public string? ParameterName { get; private set; }
}
