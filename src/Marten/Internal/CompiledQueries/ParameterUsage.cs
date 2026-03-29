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

            method.Frames.Code($"{parametersVariableName}[{Index}].{nameof(NpgsqlParameter.NpgsqlDbType)} = {npgsqlDataTypeInCodeFor(Parameter)};");
        }
    }

    // Hack for part of GH-3610
    private static string npgsqlDataTypeInCodeFor(NpgsqlParameter parameter)
    {
        if (parameter.Value is string[])
        {
            return $"{typeof(NpgsqlDbType).FullNameInCode()}.{NpgsqlDbType.Array} | {typeof(NpgsqlDbType).FullNameInCode()}.{NpgsqlDbType.Varchar}";
        }

        if (parameter.Value is int[])
        {
            return $"{typeof(NpgsqlDbType).FullNameInCode()}.{NpgsqlDbType.Array} | {typeof(NpgsqlDbType).FullNameInCode()}.{NpgsqlDbType.Integer}";
        }

        return $"{typeof(NpgsqlDbType).FullNameInCode()}.{parameter.NpgsqlDbType}";
    }

    private void generateSimpleCode(GeneratedMethod method, MemberInfo member, Type memberType,
        string parametersVariableName)
    {
        // Array types like string[], Guid[], int[] need composite NpgsqlDbType (Array | ElementType)
        // which can't be passed as a single enum value to the code generation template
        if (memberType.IsArray)
        {
            var dbTypeCode = npgsqlArrayDbTypeCodeFor(memberType);
            method.Frames.Code(
                $"{parametersVariableName}[{Index}].NpgsqlDbType = {dbTypeCode};\n" +
                $"{parametersVariableName}[{Index}].Value = _query.{member.Name};");
        }
        else
        {
            method.Frames.Code($@"
{parametersVariableName}[{Index}].NpgsqlDbType = {{0}};
{parametersVariableName}[{Index}].Value = _query.{member.Name};
", PostgresqlProvider.Instance.ToParameterType(memberType));
        }
    }

    private static string npgsqlArrayDbTypeCodeFor(Type arrayType)
    {
        var elementType = arrayType.GetElementType()!;
        var npgsqlTypeName = typeof(NpgsqlDbType).FullNameInCode();

        if (elementType == typeof(string))
            return $"{npgsqlTypeName}.{NpgsqlDbType.Array} | {npgsqlTypeName}.{NpgsqlDbType.Varchar}";
        if (elementType == typeof(Guid))
            return $"{npgsqlTypeName}.{NpgsqlDbType.Array} | {npgsqlTypeName}.{NpgsqlDbType.Uuid}";
        if (elementType == typeof(int))
            return $"{npgsqlTypeName}.{NpgsqlDbType.Array} | {npgsqlTypeName}.{NpgsqlDbType.Integer}";
        if (elementType == typeof(long))
            return $"{npgsqlTypeName}.{NpgsqlDbType.Array} | {npgsqlTypeName}.{NpgsqlDbType.Bigint}";
        if (elementType == typeof(float))
            return $"{npgsqlTypeName}.{NpgsqlDbType.Array} | {npgsqlTypeName}.{NpgsqlDbType.Real}";
        if (elementType == typeof(decimal))
            return $"{npgsqlTypeName}.{NpgsqlDbType.Array} | {npgsqlTypeName}.{NpgsqlDbType.Numeric}";
        if (elementType == typeof(DateTime))
            return $"{npgsqlTypeName}.{NpgsqlDbType.Array} | {npgsqlTypeName}.{NpgsqlDbType.Timestamp}";
        if (elementType == typeof(DateTimeOffset))
            return $"{npgsqlTypeName}.{NpgsqlDbType.Array} | {npgsqlTypeName}.{NpgsqlDbType.TimestampTz}";

        throw new NotSupportedException($"Array type {arrayType.FullNameInCode()} is not supported for compiled query parameters");
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
