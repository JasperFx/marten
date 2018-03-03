using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using Marten.Schema;
using Marten.Util;
using Npgsql;

namespace Marten.Storage
{
    /// <summary>
    /// Base class for an ISchemaObject manager for a Postgresql function
    /// </summary>
    public abstract class Function : ISchemaObject
    {
        public DbObjectName Identifier { get; }

        protected Function(DbObjectName identifier)
        {
            Identifier = identifier;
        }

        /// <summary>
        /// Override to write the actual DDL code
        /// </summary>
        /// <param name="rules"></param>
        /// <param name="writer"></param>
        public abstract void Write(DdlRules rules, StringWriter writer);

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

        public IEnumerable<DbObjectName> AllNames()
        {
            yield return Identifier;
        }

        protected FunctionDelta fetchDelta(DbDataReader reader, DdlRules rules)
        {
            if (!reader.Read())
            {
                reader.NextResult();
                return null;
            }

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

        public FunctionBody ToBody(DdlRules rules)
        {
            var dropSql = toDropSql();

            var writer = new StringWriter();
            Write(rules, writer);

            return new FunctionBody(Identifier, new string[] {dropSql}, writer.ToString());
        }

        /// <summary>
        /// Override to customize the DROP statements for this function
        /// </summary>
        /// <returns></returns>
        protected abstract string toDropSql();

        public void WriteDropStatement(DdlRules rules, StringWriter writer)
        {
            var dropSql = toDropSql();
            writer.WriteLine(dropSql);
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
    }
}