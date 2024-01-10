using System;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core.Reflection;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Internal.CompiledQueries;

internal class ParameterUsage
{
    public bool IsTenant { get; set; }
    public int Index { get; }
    public NpgsqlParameter Parameter { get; }

    public ParameterUsage(int index, string name, object value, NpgsqlDbType? dbType = null)
    {
        Index = index;
        Parameter = new NpgsqlParameter { Value = value, ParameterName = name};
        if (dbType.HasValue) Parameter.NpgsqlDbType = dbType.Value;
        Name = name;
    }

    public string Name { get;}

    // If none, it's hard-coded
    public IQueryMember Member { get; set; }
    public ICompiledQueryAwareFilter? Filter { get; set; }

    public void GenerateCode(GeneratedMethod method, string parametersVariableName, StoreOptions storeOptions)
    {
        if (IsTenant)
        {
            method.Frames.Code($"{parametersVariableName}[{Index}].Value = {{0}}.{nameof(ICommandBuilder.TenantId)};", Use.Type<ICommandBuilder>());
        }
        else if (Member != null)
        {
            if (Filter == null)
            {
                var memberType = Member.Member.GetRawMemberType();
                if (memberType!.IsEnum)
                {
                    generateEnumCode(method, storeOptions, Member.Member, parametersVariableName);
                }
                else
                {
                    generateSimpleCode(method, Member.Member, memberType, parametersVariableName);
                }
            }
            else
            {
                // TODO -- gotta go into each and use the variable name
                Filter.GenerateCode(method, Index, parametersVariableName);
            }
        }
        else
        {
            method.Frames.Code($"{parametersVariableName}[{Index}].Value = {{0}};", Constant.For(Parameter.Value));
            method.Frames.Code($"{parametersVariableName}[{Index}].{nameof(NpgsqlParameter.NpgsqlDbType)} = {{0}};", Constant.ForEnum(Parameter.NpgsqlDbType));
        }
    }

    private void generateSimpleCode(GeneratedMethod method, MemberInfo member, Type memberType,
        string parametersVariableName)
    {
        method.Frames.Code($@"
{parametersVariableName}[{Index}].NpgsqlDbType = {{0}};
{parametersVariableName}[{Index}].Value = _query.{member.Name};
", PostgresqlProvider.Instance.ToParameterType(memberType));
    }

    private void generateEnumCode(GeneratedMethod method, StoreOptions storeOptions, MemberInfo member,
        string parametersVariableName)
    {
        if (storeOptions.Serializer().EnumStorage == EnumStorage.AsInteger)
        {
            method.Frames.Code($@"
{parametersVariableName}[{Index}].NpgsqlDbType = {{0}};
{parametersVariableName}[{Index}].Value = (int)_query.{member.Name};
", NpgsqlDbType.Integer);
        }
        else
        {
            method.Frames.Code($@"
{parametersVariableName}[{Index}].NpgsqlDbType = {{0}};
{parametersVariableName}[{Index}].Value = _query.{member.Name}.ToString();
", NpgsqlDbType.Varchar);
        }
    }
}