using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Internal.CodeGeneration;
using Marten.Schema.Identity;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Schema.Arguments;

// Public for code generation, just let it go.
public class UpsertArgument
{
    protected static readonly MethodInfo writeMethod =
        typeof(NpgsqlBinaryImporter).GetMethods().FirstOrDefault(x =>
            x.Name == "Write" && x.GetParameters().Length == 2 &&
            x.GetParameters()[0].ParameterType.IsGenericParameter &&
            x.GetParameters()[1].ParameterType == typeof(NpgsqlDbType));

    private MemberInfo[] _members;
    private string _postgresType;
    public string Arg { get; set; }

    public string PostgresType
    {
        get => _postgresType;
        set
        {
            if (value == null)
            {
                throw new ArgumentNullException();
            }

            _postgresType = value.Contains("(")
                ? value.Split('(')[0].Trim()
                : value;
        }
    }

    public string Column { get; set; }

    public MemberInfo[] Members
    {
        get => _members;
        set
        {
            _members = value;
            if (value != null)
            {
                var memberType = value.Last().GetMemberType();
                DbType = PostgresqlProvider.Instance.ToParameterType(memberType);

                if (_members.Length == 1)
                {
                    DotNetType = _members.Last().GetRawMemberType();
                }
                else
                {
                    var rawType = _members.LastOrDefault().GetRawMemberType();
                    if (!rawType.IsClass && !rawType.IsNullable())
                    {
                        DotNetType = typeof(Nullable<>).MakeGenericType(rawType);
                    }
                    else
                    {
                        DotNetType = rawType;
                    }
                }

                ParameterValue = _members.Select(x => x.Name).Join("?.");
            }
        }
    }

    public string ParameterValue { get; set; }

    public Type DotNetType { get; private set; }

    public NpgsqlDbType DbType { get; set; }

    public string ArgumentDeclaration()
    {
        return $"{Arg} {PostgresType}";
    }

    public virtual void GenerateCodeToModifyDocument(GeneratedMethod method, GeneratedType type, int i,
        Argument parameters,
        DocumentMapping mapping, StoreOptions options)
    {
        // Nothing
    }

    public virtual void GenerateCodeToSetDbParameterValue(GeneratedMethod method, GeneratedType type, int i,
        Argument parameters,
        DocumentMapping mapping, StoreOptions options)
    {
        var memberPath = _members.Select(x => x.Name).Join("?.");

        if (DotNetType.IsEnum || (DotNetType.IsNullable() && DotNetType.GetGenericArguments()[0].IsEnum))
        {
            writeEnumerationValues(method, i, parameters, options, memberPath);
        }
        else
        {
            var rawMemberType = _members.Last().GetRawMemberType();

            var dbTypeString = rawMemberType.IsArray
                ? $"{Constant.ForEnum(NpgsqlDbType.Array).Usage} | {Constant.ForEnum(PostgresqlProvider.Instance.ToParameterType(rawMemberType.GetElementType())).Usage}"
                : $"({typeof(NpgsqlDbType).FullNameInCode()})({(int)DbType})";

            if (rawMemberType.IsClass || rawMemberType.IsNullable() || _members.Length > 1)
            {
                method.Frames.Code($@"
BLOCK:if (document.{memberPath} != null)
var parameter{i} = {{0}}.{nameof(IGroupedParameterBuilder.AppendParameter)}(document.{ParameterValue});
parameter{i}.{nameof(NpgsqlParameter.NpgsqlDbType)} = {dbTypeString};
END
BLOCK:else
var parameter{i} = {{0}}.{nameof(IGroupedParameterBuilder.AppendParameter)}<object>({typeof(DBNull).FullNameInCode()}.Value);
END
", Use.Type<IGroupedParameterBuilder>());
            }
            else
            {
                method.Frames.Code($"var parameter{i} = {{0}}.{nameof(IGroupedParameterBuilder.AppendParameter)}(document.{ParameterValue});",  Use.Type<IGroupedParameterBuilder>());
            }
        }
    }

    private void writeEnumerationValues(GeneratedMethod method, int i, Argument parameters, StoreOptions options,
        string memberPath)
    {
        if (options.Advanced.DuplicatedFieldEnumStorage == EnumStorage.AsInteger)
        {
            if (DotNetType.IsNullable())
            {
                method.Frames.Code(
                    $"var parameter{i} = document.{memberPath} == null ? {{0}}.{nameof(IGroupedParameterBuilder.AppendParameter)}<object>({typeof(DBNull).FullNameInCode()}.Value) : {{0}}.{nameof(CommandBuilder.AppendParameter)}((int)document.{memberPath});",
                    Use.Type<IGroupedParameterBuilder>());

                method.Frames.Code($"parameter{i}.{nameof(NpgsqlParameter.NpgsqlDbType)} = {{0}};", NpgsqlDbType.Integer);
            }
            else
            {
                method.Frames.Code(
                    $"var parameter{i} = {{0}}.{nameof(IGroupedParameterBuilder.AppendParameter)}((int)document.{memberPath});", Use.Type<IGroupedParameterBuilder>());
                method.Frames.Code($"parameter{i}.{nameof(NpgsqlParameter.NpgsqlDbType)} = {{0}};",
                    NpgsqlDbType.Integer);
            }
        }
        else if (DotNetType.IsNullable())
        {
            method.Frames.Code(
                $"var parameter{i} = {{0}}.{nameof(IGroupedParameterBuilder.AppendParameter)}((document.{memberPath}).ToString());", Use.Type<IGroupedParameterBuilder>());
            method.Frames.Code($"parameter{i}.{nameof(NpgsqlParameter.NpgsqlDbType)} = {{0}};",
                NpgsqlDbType.Varchar);
        }
        else
        {
            method.Frames.Code(
                $"var parameter{i} = {{0}}.{nameof(IGroupedParameterBuilder.AppendParameter)}(document.{memberPath}.ToString());", Use.Type<IGroupedParameterBuilder>());
            method.Frames.Code($"parameter{i}.{nameof(NpgsqlParameter.NpgsqlDbType)} = {{0}};",
                NpgsqlDbType.Varchar);
        }
    }
    public virtual void GenerateBulkWriterCodeAsync(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
    {
        var rawMemberType = _members.Last().GetRawMemberType();


        var dbTypeString = rawMemberType.IsArray
            ? $"{Constant.ForEnum(NpgsqlDbType.Array).Usage} | {Constant.ForEnum(PostgresqlProvider.Instance.ToParameterType(rawMemberType.GetElementType())).Usage}"
            : $"({typeof(NpgsqlDbType).FullNameInCode()})({(int)DbType})";


        var memberPath = _members.Select(x => x.Name).Join("?.");

        if (mapping.IdStrategy is ValueTypeIdGeneration st)
        {
            st.WriteBulkWriterCodeAsync(load, mapping);
        }
        else if (DotNetType.IsEnum || (DotNetType.IsNullable() && DotNetType.GetGenericArguments()[0].IsEnum))
        {
            var isDeep = _members.Length > 0;
            var memberType = _members.Last().GetMemberType();
            var isNullable = memberType.IsNullable();

            var enumType = isNullable ? memberType.GetGenericArguments()[0] : memberType;
            var accessor = memberPath;

            if (DbType == NpgsqlDbType.Integer)
            {
                if (isNullable || isDeep)
                {
                    accessor =
                        $"{nameof(BulkLoader<string, int>.GetEnumIntValue)}<{enumType.FullNameInCode()}>(document.{memberPath})";
                }

                load.Frames.CodeAsync($"await writer.WriteAsync({accessor}, {{0}}, {{1}});", NpgsqlDbType.Integer,
                    Use.Type<CancellationToken>());
            }
            else
            {
                if (isNullable || isDeep)
                {
                    accessor =
                        $"GetEnumStringValue<{enumType.FullNameInCode()}>(document.{memberPath})";
                }
                else
                {
                    accessor = $"document.{memberPath}.ToString()";
                }

                load.Frames.CodeAsync($"await writer.WriteAsync({accessor}, {{0}}, {{1}});", NpgsqlDbType.Varchar,
                    Use.Type<CancellationToken>());
            }
        }
        else if (DotNetType.IsNullable() && DotNetType.GetGenericArguments()[0].IsValueType)
        {
            var valueType = DotNetType.GetGenericArguments()[0];
            var accessor = $"GetNullable<{valueType}>(document.{memberPath})";
            var npgsqlType = DbType;
            load.Frames.CodeAsync($"await writer.WriteAsync({accessor}, {{0}}, {{1}});", npgsqlType,
                Use.Type<CancellationToken>());
        }
        else
        {
            load.Frames.CodeAsync($"await writer.WriteAsync(document.{ParameterValue}, {dbTypeString}, {{0}});",
                Use.Type<CancellationToken>());
        }
    }
}
