using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Marten.Services;
using Marten.Storage;
using Marten.Util;

namespace Marten.Schema.Identity.Sequences
{
    public class SequenceFactory : ISequences, ISchemaObjects, IFeatureSchema
    {
        private readonly StoreOptions _options;
        private bool _checked = false;
        private readonly ConcurrentDictionary<Type, ISequence> _sequences = new ConcurrentDictionary<Type, ISequence>();

        private DbObjectName Table => new DbObjectName(_options.DatabaseSchemaName, "mt_hilo");

        public SequenceFactory(StoreOptions options)
        {
            _options = options;
        }

        public ISequence SequenceFor(Type documentType)
        {
            // Okay to let it blow up if it doesn't exist here IMO
            return _sequences[documentType];
        }

        public ISequence Hilo(Type documentType, HiloSettings settings)
        {
            return _sequences.GetOrAdd(documentType, type => new HiloSequence(_options.ConnectionFactory(), _options, documentType.Name, settings));
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
        public IEnumerable<Type> DependentTypes()
        {
            yield break;
        }

        public bool IsActive { get; set; }

        public ISchemaObject[] Objects
        {
            get
            {
                var table = new Table(new DbObjectName(_options.DatabaseSchemaName, "mt_hilo"));
                table.AddPrimaryKey(new TableColumn("entity_name", "varchar"));
                table.AddColumn("hi_value", "bigint", "default 0");

                var function = new SystemFunction(_options, "mt_get_next_hi", "varchar");

                return new ISchemaObject[]
                {
                    table,
                    function
                };
            }
        }

        public Type StorageType { get; } = typeof(SequenceFactory);
        public string Identifier { get; } = "hilo";
    }
}