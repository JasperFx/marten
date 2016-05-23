using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Schema.Arguments;
using Marten.Util;

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

            Arguments.Add(new DocJsonBodyArgument());

            Arguments.AddRange(mapping.DuplicatedFields.Select(x => x.UpsertArgument));

            Arguments.Add(new VersionArgument());

            Arguments.Add(new DotNetTypeArgument());

            if (mapping.IsHierarchy())
            {
                Arguments.Add(new DocTypeArgument());
            }

        }

        public void WriteFunctionSql(PostgresUpsertType upsertType, StringWriter writer)
        {
            var ordered = OrderedArguments();

            var argList = ordered.Select(x => x.ArgumentDeclaration()).Join(", ");

            var systemUpdates = new string[] {$"{DocumentMapping.LastModifiedColumn} = transaction_timestamp()" };
            var updates = ordered.Where(x => x.Column != "id")
                .Select(x => $"\"{x.Column}\" = {x.Arg}").Concat(systemUpdates).Join(", ");

            var inserts = ordered.Select(x => $"\"{x.Column}\"").Concat(new string[] {DocumentMapping.LastModifiedColumn}).Join(", ");
            var valueList = ordered.Select(x => x.Arg).Concat(new string[] { "transaction_timestamp()" }).Join(", ");


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