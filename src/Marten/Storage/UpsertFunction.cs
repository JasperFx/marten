using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Util;
using Npgsql;

namespace Marten.Storage
{
    public class UpsertFunction : ISchemaObject
    {
        private readonly string _primaryKeyConstraintName;
        private readonly DbObjectName _tableName;

        public readonly IList<UpsertArgument> Arguments = new List<UpsertArgument>();

        public UpsertFunction(DocumentMapping mapping)
        {
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));

            Identifier = mapping.UpsertFunction;
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

        public void Write(DdlRules rules, StringWriter writer)
        {
            var ordered = OrderedArguments();

            var argList = ordered.Select(x => x.ArgumentDeclaration()).Join(", ");

            var systemUpdates = new string[] {$"{DocumentMapping.LastModifiedColumn} = transaction_timestamp()" };
            var updates = ordered.Where(x => x.Column != "id" && x.Column.IsNotEmpty())
                .Select(x => $"\"{x.Column}\" = {x.Arg}").Concat(systemUpdates).Join(", ");

            var inserts = ordered.Where(x => x.Column.IsNotEmpty()).Select(x => $"\"{x.Column}\"").Concat(new [] {DocumentMapping.LastModifiedColumn}).Join(", ");
            var valueList = ordered.Where(x => x.Column.IsNotEmpty()).Select(x => x.Arg).Concat(new [] { "transaction_timestamp()" }).Join(", ");

            if (Arguments.Any(x => x is CurrentVersionArgument))
            {
                updates += $" where {_tableName.QualifiedName}.{DocumentMapping.VersionColumn} = current_version";
            }

            var securityDeclaration = rules.UpsertRights == SecurityRights.Invoker
                ? "SECURITY INVOKER"
                : "SECURITY DEFINER";

            

            writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier.QualifiedName}({argList}) RETURNS UUID LANGUAGE plpgsql {securityDeclaration} AS $function$
DECLARE
  final_version uuid;
BEGIN
INSERT INTO {_tableName.QualifiedName} ({inserts}) VALUES ({valueList})
  ON CONFLICT ON CONSTRAINT {_primaryKeyConstraintName}
  DO UPDATE SET {updates};

  SELECT mt_version FROM {_tableName.QualifiedName} into final_version WHERE id = docId;
  RETURN final_version;
END;
$function$;
");


        }

        public UpsertArgument[] OrderedArguments()
        {
            return Arguments.OrderBy(x => x.Arg).ToArray();
        }


        public FunctionBody ToBody(DdlRules rules)
        {
            var dropSql = toDropSql();

            var writer = new StringWriter();
            Write(rules, writer);

            return new FunctionBody(Identifier, new string[] {dropSql}, writer.ToString());
        }

        private string toDropSql()
        {
            var argList = OrderedArguments().Select(x => x.PostgresType).Join(", ");
            var dropSql = $"drop function if exists {Identifier.QualifiedName}({argList});";
            return dropSql;
        }

        public void WriteDropStatement(DdlRules rules, StringWriter writer)
        {
            var dropSql = toDropSql();
            writer.WriteLine(dropSql);
        }

        public DbObjectName Identifier { get; }

        public void ConfigureQueryCommand(CommandBuilder builder)
        {
            var schemaParam = builder.AddParameter(Identifier.Schema).ParameterName;
            var nameParam = builder.AddParameter(Identifier.Name).ParameterName;

            builder.Append($@"
SELECT pg_get_functiondef(pg_proc.oid) 
FROM pg_proc JOIN pg_namespace as ns ON pg_proc.pronamespace = ns.oid WHERE ns.nspname = :{schemaParam} and proname = :{nameParam};

SELECT format('DROP FUNCTION %s.%s(%s);'
             ,n.nspname
             ,p.proname
             ,pg_get_function_identity_arguments(p.oid))
FROM   pg_proc p
LEFT JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace 
WHERE  p.proname = :{nameParam}
AND    n.nspname = :{schemaParam};
");
        }

        private FunctionDelta fetchDelta(DbDataReader reader, DdlRules rules)
        {
            if (!reader.Read()) return null;

            var upsertDefinition = reader.GetString(0);

            reader.NextResult();
            var drops = new List<string>();
            while (reader.Read())
            {
                drops.Add(reader.GetString(0));
            }

            if (upsertDefinition == null) return null;

            var actualBody = new FunctionBody(Identifier, drops.ToArray(), upsertDefinition);

            var expectedBody = ToBody(rules);

            return new FunctionDelta(expectedBody, actualBody);
        }

        public FunctionDelta FetchDelta(NpgsqlConnection conn, DdlRules rules)
        {
            var cmd = conn.CreateCommand();
            var builder = new CommandBuilder(cmd);

            ConfigureQueryCommand(builder);

            cmd.CommandText = builder.ToString();

            using (var reader = cmd.ExecuteReader())
            {
                return fetchDelta(reader, rules);
            }
        }

        public SchemaPatchDifference CreatePatch(DbDataReader reader, SchemaPatch patch, AutoCreate autoCreate)
        {
            var diff = fetchDelta(reader, patch.Rules);
            if (diff == null)
            {
                Write(patch.Rules, patch.UpWriter);
                WriteDropStatement(patch.Rules, patch.DownWriter);

                return SchemaPatchDifference.Create;
            }

            if (diff.AllNew)
            {
                Write(patch.Rules, patch.UpWriter);
                WriteDropStatement(patch.Rules, patch.DownWriter);

                return SchemaPatchDifference.Create;
            }

            if (diff.HasChanged)
            {
                diff.WritePatch(patch);

                return SchemaPatchDifference.Update;
            }

            return SchemaPatchDifference.None;
        }
    }
}