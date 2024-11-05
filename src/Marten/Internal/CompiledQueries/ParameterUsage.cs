using System;
using System.Data;
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

    public ParameterUsage(int index, string name, object value, DbType? dbType = null)
    {
        Index = index;
        Parameter = new NpgsqlParameter { Value = value, ParameterName = name};
        if (dbType.HasValue) Parameter.DbType = dbType.Value;

        if (value is int) Parameter.NpgsqlDbType = NpgsqlDbType.Integer;

        Name = name;
    }

    [Obsolete("Try to eliminate this")]
    public ParameterUsage(int index, string name, object value, NpgsqlDbType? dbType = null)
    {
        Index = index;
        Parameter = new NpgsqlParameter { Value = value, ParameterName = name};
        if (dbType.HasValue) Parameter.NpgsqlDbType = dbType.Value;

        if (value is int) Parameter.NpgsqlDbType = NpgsqlDbType.Integer;

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
            method.Frames.Code($"{parametersVariableName}[{Index}].Value = {{0}}.{nameof(IPostgresqlCommandBuilder.TenantId)};", Use.Type<IPostgresqlCommandBuilder>());
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
            method.Frames.Code($"{parametersVariableName}[{Index}].{nameof(NpgsqlParameter.DbType)} = {typeof(DbType).FullNameInCode()}.{Parameter.DbType};");
        }
    }

    private void generateSimpleCode(GeneratedMethod method, MemberInfo member, Type memberType,
        string parametersVariableName)
    {
        method.Frames.Code($@"
{parametersVariableName}[{Index}].DbType = {{0}};
{parametersVariableName}[{Index}].Value = _query.{member.Name};
", DbTypeMapper.Lookup(memberType));
    }

    private void generateEnumCode(GeneratedMethod method, StoreOptions storeOptions, MemberInfo member,
        string parametersVariableName)
    {
        if (storeOptions.Serializer().EnumStorage == EnumStorage.AsInteger)
        {
            method.Frames.Code($@"
{parametersVariableName}[{Index}].DbType = {{0}};
{parametersVariableName}[{Index}].Value = (int)_query.{member.Name};
", DbType.Int32);
        }
        else
        {
            method.Frames.Code($@"
{parametersVariableName}[{Index}].DbType = {{0}};
{parametersVariableName}[{Index}].Value = _query.{member.Name}.ToString();
", DbType.String);
        }
    }
}
