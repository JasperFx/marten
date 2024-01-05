using System;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten.Internal.CompiledQueries;
using Marten.Linq.Members;
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

internal class StringContainsFilter: ISqlFragment, ICompiledQueryAwareFilter
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

        builder.AppendParameter($"%{_rawValue}%");

        builder.Append(StringComparisonParser.EscapeSuffix);

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
        var maskedValue = $"ContainsString(_query.{_queryMember.Name})";

        method.Frames.Code($@"
{parametersVariableName}[{parameterIndex}].NpgsqlDbType = {{0}};
{parametersVariableName}[{parameterIndex}].Value = {maskedValue};
", NpgsqlDbType.Varchar);
    }

    public string ParameterName { get; private set; }
}
