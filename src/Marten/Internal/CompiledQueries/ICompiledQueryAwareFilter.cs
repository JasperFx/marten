using System;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Internal.CompiledQueries;

/// <summary>
/// This marker interface is used for SQL fragment filters where
/// there needs to be some special handling within compiled queries
/// </summary>
public interface ICompiledQueryAwareFilter
{
    bool TryMatchValue(object value, MemberInfo member);
    void GenerateCode(GeneratedMethod method, int parameterIndex);

    string ParameterName { get; }
}


internal record CompiledParameterApplication(int Index, ICompiledQueryAwareFilter? Filter)
{
    public void GenerateCode(GeneratedMethod method, StoreOptions storeOptions, MemberInfo member)
    {
        if (Filter != null)
        {
            Filter.GenerateCode(method, Index);
        }
        else
        {
            var memberType = member.GetRawMemberType();

            if (memberType!.IsEnum)
            {
                generateEnumCode(method, storeOptions, member);
            }
            else
            {
                generateSimpleCode(method, member, memberType);
            }
        }
    }

    private void generateSimpleCode(GeneratedMethod method, MemberInfo member, Type memberType)
    {
        method.Frames.Code($@"
parameters[{Index}].NpgsqlDbType = {{0}};
parameters[{Index}].Value = _query.{member.Name};
", PostgresqlProvider.Instance.ToParameterType(memberType));
    }

    private void generateEnumCode(GeneratedMethod method, StoreOptions storeOptions, MemberInfo member)
    {
        if (storeOptions.Serializer().EnumStorage == EnumStorage.AsInteger)
        {
            method.Frames.Code($@"
parameters[{Index}].NpgsqlDbType = {{0}};
parameters[{Index}].Value = (int)_query.{member.Name};
", NpgsqlDbType.Integer);
        }
        else
        {
            method.Frames.Code($@"
parameters[{Index}].NpgsqlDbType = {{0}};
parameters[{Index}].Value = _query.{member.Name}.ToString();
", NpgsqlDbType.Varchar);
        }
    }
}
