using System;
using System.IO;
using Baseline;
using Marten.Services;
using Marten.Util;

namespace Marten.Schema.Identity.Sequences
{
    public class SequenceFactory : ISequences, ISchemaObjects
    {
        private readonly IDocumentSchema _schema;
        private readonly IConnectionFactory _factory;
        private readonly StoreOptions _options;
        private readonly IMartenLogger _logger;
        private bool _checked = false;

        private TableName Table => new TableName(_options.DatabaseSchemaName, "mt_hilo");

        public SequenceFactory(IDocumentSchema schema, IConnectionFactory factory, StoreOptions options, IMartenLogger logger)
        {
            _schema = schema;
            _factory = factory;
            _options = options;
            _logger = logger;
        }

        public ISequence Hilo(Type documentType, HiloSettings settings)
        {
            return new HiloSequence(_factory, _options, documentType.Name, settings);
        }

        public void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema, SchemaPatch patch)
        {
            if (_checked) return;

            _checked = true;

            if (!schema.DbObjects.TableExists(Table))
            {
                if (_options.AutoCreateSchemaObjects == AutoCreate.None)
                {
                    throw new InvalidOperationException($"Hilo table is missing, but {nameof(StoreOptions.AutoCreateSchemaObjects)} is {_options.AutoCreateSchemaObjects}");
                }

                WritePatch(schema, patch);
            }
        }

        public override string ToString()
        {
            return "Hilo Sequence Factory";
        }

        public void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer)
        {
            var sqlScript = SchemaBuilder.GetSqlScript(Table.Schema, "mt_hilo");
            writer.WriteLine(sqlScript);
            writer.WriteLine("");
            writer.WriteLine("");

            if (schema.StoreOptions.OwnerName.IsNotEmpty())
            {
                writer.WriteLine($"ALTER TABLE {schema.StoreOptions.DatabaseSchemaName}.mt_hilo OWNER TO \"{schema.StoreOptions.OwnerName}\";");
                writer.WriteLine($"ALTER FUNCTION {schema.StoreOptions.DatabaseSchemaName}.mt_get_next_hi(varchar) OWNER TO \"{schema.StoreOptions.OwnerName}\";");
            }
        }

        public void RemoveSchemaObjects(IManagedConnection connection)
        {
            var sql = "drop table if exists " + Table.QualifiedName;
            connection.Execute(cmd => cmd.Sql(sql).ExecuteNonQuery());
        }

        public void ResetSchemaExistenceChecks()
        {
            _checked = false;
        }

        public void WritePatch(IDocumentSchema schema, SchemaPatch patch)
        {
            if (!schema.DbObjects.TableExists(Table))
            {
                var sqlScript = SchemaBuilder.GetSqlScript(Table.Schema, "mt_hilo");
                patch.Updates.Apply(this, sqlScript);

                patch.Rollbacks.Drop(this, Table);

                writeOwnership(schema, patch);

                writeGrants(schema, patch);
            }
        }

        private void writeGrants(IDocumentSchema schema, SchemaPatch patch)
        {
            foreach (var role in schema.StoreOptions.DdlRules.Grants)
            {
                patch.Updates.Apply(this, $"GRANT SELECT ON TABLE {Table.QualifiedName} TO \"{role}\";");
                patch.Updates.Apply(this, $"GRANT UPDATE ON TABLE {Table.QualifiedName} TO \"{role}\";");
                patch.Updates.Apply(this, $"GRANT INSERT ON TABLE {Table.QualifiedName} TO \"{role}\";");

                patch.Updates.Apply(this, $"GRANT EXECUTE ON {schema.StoreOptions.DatabaseSchemaName}.mt_get_next_hi(varchar) TO \"{role}\";");
            }
        }

        private void writeOwnership(IDocumentSchema schema, SchemaPatch patch)
        {
            if (schema.StoreOptions.OwnerName.IsNotEmpty())
            {
                patch.Updates.Apply(this,
                    $"ALTER TABLE {schema.StoreOptions.DatabaseSchemaName}.mt_hilo OWNER TO \"{schema.StoreOptions.OwnerName}\";");
                patch.Updates.Apply(this,
                    $"ALTER FUNCTION {schema.StoreOptions.DatabaseSchemaName}.mt_get_next_hi(varchar) OWNER TO \"{schema.StoreOptions.OwnerName}\";");
            }
        }

        public string Name { get; } = "mt_hilo";
    }
}