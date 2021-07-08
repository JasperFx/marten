using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Services;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Schema.Arguments
{
    // Public for code generation, just let it go.
    public class UpsertArgument
    {
        protected static readonly MethodInfo writeMethod =
            typeof(NpgsqlBinaryImporter).GetMethods().FirstOrDefault(x => x.Name == "Write" && x.GetParameters().Length == 2 && x.GetParameters()[0].ParameterType.IsGenericParameter && x.GetParameters()[1].ParameterType == typeof(NpgsqlTypes.NpgsqlDbType));

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
                    DbType = PostgresqlProvider.Instance.ToParameterType(value.Last().GetMemberType());


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
                }


            }
        }

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

        public virtual void GenerateCodeToSetDbParameterValue(GeneratedMethod method, GeneratedType type, int i, Argument parameters,
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
                    : Constant.ForEnum(DbType).Usage;

                method.Frames.Code($"{parameters.Usage}[{i}].{nameof(NpgsqlParameter.NpgsqlDbType)} = {dbTypeString};");

                if (rawMemberType.IsClass || rawMemberType.IsNullable() || _members.Length > 1)
                {
                    method.Frames.Code($@"
BLOCK:if (document.{memberPath} != null)
{parameters.Usage}[{i}].{nameof(NpgsqlParameter.Value)} = document.{memberPath};
END
BLOCK:else
{parameters.Usage}[{i}].{nameof(NpgsqlParameter.Value)} = {typeof(DBNull).FullNameInCode()}.{nameof(DBNull.Value)};
END
");

                }
                else
                {
                    method.Frames.Code($"{parameters.Usage}[{i}].{nameof(NpgsqlParameter.Value)} = document.{memberPath};");
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
                    method.Frames.Code($"{parameters.Usage}[{i}].{nameof(NpgsqlParameter.NpgsqlDbType)} = {{0}};",
                        NpgsqlDbType.Integer);
                    method.Frames.Code(
                        $"{parameters.Usage}[{i}].{nameof(NpgsqlParameter.Value)} = document.{memberPath} == null ? (object){typeof(DBNull).FullNameInCode()}.Value : (object)((int)document.{memberPath});");
                }
                else
                {
                    method.Frames.Code($"{parameters.Usage}[{i}].{nameof(NpgsqlParameter.NpgsqlDbType)} = {{0}};",
                        NpgsqlDbType.Integer);
                    method.Frames.Code(
                        $"{parameters.Usage}[{i}].{nameof(NpgsqlParameter.Value)} = (int)document.{memberPath};");
                }
            }
            else if (DotNetType.IsNullable())
            {
                method.Frames.Code($"{parameters.Usage}[{i}].{nameof(NpgsqlParameter.NpgsqlDbType)} = {{0}};",
                    NpgsqlDbType.Varchar);
                method.Frames.Code(
                    $"{parameters.Usage}[{i}].{nameof(NpgsqlParameter.Value)} = (document.{memberPath} ).ToString();");
            }
            else
            {
                method.Frames.Code($"{parameters.Usage}[{i}].{nameof(NpgsqlParameter.NpgsqlDbType)} = {{0}};",
                    NpgsqlDbType.Varchar);
                method.Frames.Code(
                    $"{parameters.Usage}[{i}].{nameof(NpgsqlParameter.Value)} = document.{memberPath}.ToString();");
            }
        }

        public virtual void GenerateBulkWriterCode(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
        {
            var rawMemberType = _members.Last().GetRawMemberType();


            var dbTypeString = rawMemberType.IsArray
                ? $"{Constant.ForEnum(NpgsqlDbType.Array).Usage} | {Constant.ForEnum(PostgresqlProvider.Instance.ToParameterType(rawMemberType.GetElementType())).Usage}"
                : Constant.ForEnum(DbType).Usage;



            if (DotNetType.IsEnum)
            {
                if (mapping.EnumStorage == EnumStorage.AsInteger)
                {
                    load.Frames.Code($"writer.Write((int)document.{_members.Last().Name}, {{0}});", NpgsqlDbType.Integer);
                }
                else if (DotNetType.IsNullable())
                {

                    load.Frames.Code($"writer.Write(document.{_members.Last().Name}?.ToString(), {{0}});", NpgsqlDbType.Varchar);
                }
                else
                {
                    load.Frames.Code($"writer.Write(document.{_members.Last().Name}.ToString(), {{0}});", NpgsqlDbType.Varchar);
                }
            }
            else
            {
                load.Frames.Code($"writer.Write(document.{_members.Last().Name}, {dbTypeString});");
            }

        }

        public virtual void GenerateBulkWriterCodeAsync(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
        {
            var rawMemberType = _members.Last().GetRawMemberType();


            var dbTypeString = rawMemberType.IsArray
                ? $"{Constant.ForEnum(NpgsqlDbType.Array).Usage} | {Constant.ForEnum(PostgresqlProvider.Instance.ToParameterType(rawMemberType.GetElementType())).Usage}"
                : Constant.ForEnum(DbType).Usage;



            if (DotNetType.IsEnum)
            {
                if (mapping.EnumStorage == EnumStorage.AsInteger)
                {
                    load.Frames.CodeAsync($"await writer.WriteAsync((int)document.{_members.Last().Name}, {{0}}, {{1}});", NpgsqlDbType.Integer, Use.Type<CancellationToken>());
                }
                else if (DotNetType.IsNullable())
                {

                    load.Frames.CodeAsync($"await writer.WriteAsync(document.{_members.Last().Name}?.ToString(), {{0}}, {{1}});", NpgsqlDbType.Varchar, Use.Type<CancellationToken>());
                }
                else
                {
                    load.Frames.CodeAsync($"await writer.WriteAsync(document.{_members.Last().Name}.ToString(), {{0}}, {{1}});", NpgsqlDbType.Varchar, Use.Type<CancellationToken>());
                }
            }
            else
            {
                load.Frames.CodeAsync($"await writer.WriteAsync(document.{_members.Last().Name}, {dbTypeString}, {{0}});", Use.Type<CancellationToken>());
            }
        }
    }
}
