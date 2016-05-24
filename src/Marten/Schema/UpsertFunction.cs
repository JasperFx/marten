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

            if (mapping.UseOptimisticConcurrency)
            {
                Arguments.Add(new CurrentVersionArgument());
            }

        }

        public void WriteFunctionSql(PostgresUpsertType upsertType, StringWriter writer)
        {
            var ordered = OrderedArguments();

            var argList = ordered.Select(x => x.ArgumentDeclaration()).Join(", ");

            var systemUpdates = new string[] {$"{DocumentMapping.LastModifiedColumn} = transaction_timestamp()" };
            var updates = ordered.Where(x => x.Column != "id" && x.Column.IsNotEmpty())
                .Select(x => $"\"{x.Column}\" = {x.Arg}").Concat(systemUpdates).Join(", ");

            var inserts = ordered.Where(x => x.Column.IsNotEmpty()).Select(x => $"\"{x.Column}\"").Concat(new [] {DocumentMapping.LastModifiedColumn}).Join(", ");
            var valueList = ordered.Where(x => x.Column.IsNotEmpty()).Select(x => x.Arg).Concat(new [] { "transaction_timestamp()" }).Join(", ");

            var updateWhere = "";
            if (Arguments.Any(x => x is CurrentVersionArgument))
            {
                updateWhere = $" and {_tableName.QualifiedName}.{DocumentMapping.VersionColumn} = current_version or current_version is null";
                if (upsertType == PostgresUpsertType.Standard)
                {
                    updates += $" where {_tableName.QualifiedName}.{DocumentMapping.VersionColumn} = current_version or current_version is null";
                }
            }

            if (upsertType == PostgresUpsertType.Legacy)
            {
                if (Arguments.Any(x => x is CurrentVersionArgument))
                {
                    writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {_functionName.QualifiedName}({argList}) RETURNS UUID LANGUAGE plpgsql AS $function$
DECLARE
  final_version uuid;
  old_version uuid;
BEGIN
  SELECT {DocumentMapping.VersionColumn} into old_version FROM {_tableName.QualifiedName} WHERE id = docId;
  IF old_version IS NOT NULL AND old_version != current_version THEN
    RETURN old_version;
  END IF;

  LOCK TABLE {_tableName.QualifiedName} IN SHARE ROW EXCLUSIVE MODE;
  WITH upsert AS (UPDATE {_tableName.QualifiedName} SET {updates} WHERE id=docId {updateWhere} RETURNING *) 
  INSERT INTO {_tableName.QualifiedName} ({inserts})
  SELECT {valueList} WHERE NOT EXISTS (SELECT * FROM upsert);

  SELECT mt_version FROM {_tableName.QualifiedName} into final_version WHERE id = docId;
  RETURN final_version;
END;
$function$;
");
                }
                else
                {
                    writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {_functionName.QualifiedName}({argList}) RETURNS UUID LANGUAGE plpgsql AS $function$
DECLARE
  final_version uuid;
BEGIN
  LOCK TABLE {_tableName.QualifiedName} IN SHARE ROW EXCLUSIVE MODE;
  WITH upsert AS (UPDATE {_tableName.QualifiedName} SET {updates} WHERE id=docId RETURNING *) 
  INSERT INTO {_tableName.QualifiedName} ({inserts})
  SELECT {valueList} WHERE NOT EXISTS (SELECT * FROM upsert);

  SELECT mt_version FROM {_tableName.QualifiedName} into final_version WHERE id = docId;
  RETURN final_version;
END;
$function$;
");
                }




            }
            else
            {
                writer.WriteLine($"CREATE OR REPLACE FUNCTION {_functionName.QualifiedName}({argList}) RETURNS UUID LANGUAGE plpgsql AS $function$");
                writer.WriteLine("DECLARE");
                writer.WriteLine("  final_version uuid;");
                writer.WriteLine("BEGIN");
                writer.WriteLine($"INSERT INTO {_tableName.QualifiedName} ({inserts}) VALUES ({valueList})");
                writer.WriteLine($"  ON CONFLICT ON CONSTRAINT {_primaryKeyConstraintName}");
                writer.WriteLine($"  DO UPDATE SET {updates};");
                writer.WriteLine("");
                writer.WriteLine($"  SELECT mt_version FROM {_tableName.QualifiedName} into final_version WHERE id = docId;");
                writer.WriteLine("   RETURN final_version;");
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