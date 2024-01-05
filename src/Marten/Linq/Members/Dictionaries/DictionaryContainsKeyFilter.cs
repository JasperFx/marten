using System.Linq.Expressions;
using System.Reflection;
using JasperFx.CodeGeneration;
using Marten.Exceptions;
using Marten.Internal.CompiledQueries;
using Marten.Linq.Parsing;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members.Dictionaries;

internal class DictionaryContainsKeyFilter: ISqlFragment, ICompiledQueryAwareFilter
{
    private readonly object _value;
    private readonly string _keyText;
    private readonly IDictionaryMember _member;

    public DictionaryContainsKeyFilter(IDictionaryMember member, ISerializer serializer, ConstantExpression constant)
    {
        _value = constant.Value;
        _keyText = serializer.ToCleanJson(_value);

        _member = member;
    }

    public DictionaryContainsKeyFilter(IDictionaryMember member, ISerializer serializer, object value)
    {
        _value = value;
        _keyText = serializer.ToCleanJson(_value);

        _member = member;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("d.data #> '{");
        foreach (var segment in _member.JsonPathSegments())
        {
            builder.Append(segment);
            builder.Append(", ");
        }

        builder.Append(_keyText);
        builder.Append("}' is not null");
    }

    public bool TryMatchValue(object value, MemberInfo member)
    {
        throw new BadLinqExpressionException("Marten does not (yet) support Dictionary.ContainsKey() in compiled queries");
    }

    public void GenerateCode(GeneratedMethod method, int parameterIndex, string parametersVariableName)
    {
        throw new BadLinqExpressionException("Marten does not (yet) support Dictionary.ContainsKey() in compiled queries");
    }

    public string ParameterName { get; } = "NONE";
}
