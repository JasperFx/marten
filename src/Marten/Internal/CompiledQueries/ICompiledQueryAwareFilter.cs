using System.Reflection;
using JasperFx.CodeGeneration;

namespace Marten.Internal.CompiledQueries;

/// <summary>
/// This marker interface is used for SQL fragment filters where
/// there needs to be some special handling within compiled queries
/// </summary>
public interface ICompiledQueryAwareFilter
{
    bool TryMatchValue(object value, MemberInfo member);
    void GenerateCode(GeneratedMethod method, int parameterIndex, string parametersVariableName);

    string ParameterName { get; }
}
