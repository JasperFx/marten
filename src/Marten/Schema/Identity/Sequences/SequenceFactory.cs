using System;
using System.IO;
using Baseline;
using Marten.Services;
using Marten.Util;

namespace Marten.Schema.Identity.Sequences
{
    public class SequenceFactory : ISequences, ISchemaObjects
    {
        private readonly IConnectionFactory _factory;
        private readonly StoreOptions _options;
        private bool _checked = false;

        private TableName Table => new TableName(_options.DatabaseSchemaName, "mt_hilo");

        public SequenceFactory(IConnectionFactory factory, StoreOptions options)
        {
            _factory = factory;
            _options = options;
        }

        public ISequence Hilo(Type documentType, HiloSettings settings)
        {
            return new HiloSequence(_factory, _options, documentType.Name, settings);
        }

        public void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema, SchemaPatch patch)
        {
            if (_checked || autoCreateSchemaObjectsMode == AutoCreate.None) return;

            _checked = true;

            if (!schema.DbObjects.TableExists(Table))
            {
                if (autoCreateSchemaObjectsMode == AutoCreate.None)
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
            var patch = new SchemaPatch(schema.StoreOptions.DdlRules, writer);

            var sqlScript = SchemaBuilder.GetSqlScript(Table.Schema, "mt_hilo");

            writer.WriteLine(sqlScript);
            writer.WriteLine("");
            writer.WriteLine("");
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
            }
        }


        public string Name { get; } = "mt_hilo";
    }
}