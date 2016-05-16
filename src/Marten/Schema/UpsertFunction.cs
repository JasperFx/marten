using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class UpsertFunction
    {
        private readonly string _primaryKeyConstraintName;
        private readonly FunctionName _functionName;
        private readonly TableName _tableName;

        public readonly IList<UpsertArgument> Arguments = new List<UpsertArgument>();

        public UpsertFunction(DocumentMapping mapping)
        {
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));

            _functionName = mapping.UpsertFunction;
            _tableName = mapping.Table;
            _primaryKeyConstraintName = "pk_" + mapping.Table.Name;
                
            var idType = mapping.IdMember.GetMemberType();
            var pgIdType = TypeMappings.GetPgType(idType);

            Arguments.Add(new UpsertArgument
            {
                Arg = "docId",
                PostgresType = pgIdType,
                Column = "id",
                Members = new[] {mapping.IdMember}
            });
            Arguments.Add(new UpsertArgument
            {
                Arg = "doc",
                PostgresType = "JSONB",
                DbType = NpgsqlDbType.Jsonb,
                Column = "data",
            });
        }

        public void WriteFunctionSql(PostgresUpsertType upsertType, StringWriter writer)
        {
            var ordered = OrderedArguments();

            var argList = ordered.Select(x => x.ArgumentDeclaration()).Join(", ");

            var updates = ordered.Where(x => x.Column != "id")
                .Select(x => $"\"{x.Column}\" = {x.Arg}").Join(", ");

            var inserts = ordered.Select(x => $"\"{x.Column}\"").Join(", ");
            var valueList = ordered.Select(x => x.Arg).Join(", ");

            // CREATE OR REPLACE FUNCTION public.mt_upsert_user(arg_internal boolean, arg_user_name varchar, doc jsonb, docid uuid) RETURNS void LANGUAGE plpgsql AS $function$ BEGIN LOCK TABLE public.mt_doc_user IN SHARE ROW EXCLUSIVE MODE;  WITH upsert AS (UPDATE public.mt_doc_user SET \"internal\" = arg_internal, \"user_name\" = arg_user_name, \"data\" = doc WHERE id=docId RETURNING *)   INSERT INTO public.mt_doc_user (\"internal\", \"user_name\", \"data\", \"id\")  SELECT arg_internal, arg_user_name, doc, docId WHERE NOT EXISTS (SELECT * FROM upsert); END; $function$

            if (upsertType == PostgresUpsertType.Legacy)
            {
                writer.WriteLine($"CREATE OR REPLACE FUNCTION {_functionName.QualifiedName}({argList}) RETURNS void LANGUAGE plpgsql AS $function$");
                writer.WriteLine("BEGIN");
                writer.WriteLine($"LOCK TABLE {_tableName.QualifiedName} IN SHARE ROW EXCLUSIVE MODE;");
                writer.WriteLine($"  WITH upsert AS (UPDATE {_tableName.QualifiedName} SET {updates} WHERE id=docId RETURNING *) ");
                writer.WriteLine($"  INSERT INTO {_tableName.QualifiedName} ({inserts})");
                writer.WriteLine($"  SELECT {valueList} WHERE NOT EXISTS (SELECT * FROM upsert);");
                writer.WriteLine("END;");
                writer.WriteLine("$function$;");
                
            }
            else
            {
                writer.WriteLine($"CREATE OR REPLACE FUNCTION {_functionName.QualifiedName}({argList}) RETURNS void LANGUAGE plpgsql AS $function$");
                writer.WriteLine("BEGIN");
                writer.WriteLine($"INSERT INTO {_tableName.QualifiedName} ({inserts}) VALUES ({valueList})");
                writer.WriteLine($"  ON CONFLICT ON CONSTRAINT {_primaryKeyConstraintName}");
                writer.WriteLine($"  DO UPDATE SET {updates};");
                writer.WriteLine("END;");
                writer.WriteLine("$function$;");
            }
        }

        public UpsertArgument[] OrderedArguments()
        {
            return Arguments.OrderBy(x => x.Arg).ToArray();
        }


    }
}