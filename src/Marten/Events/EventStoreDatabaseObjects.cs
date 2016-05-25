using System;
using System.IO;
using Marten.Generation;
using Marten.Schema;
using Marten.Services;

namespace Marten.Events
{
    public class EventStoreDatabaseObjects : IDocumentSchemaObjects
    {
        private readonly EventGraph _parent;
        private bool _checkedSchema;
        private readonly object _locker = new object();

        public EventStoreDatabaseObjects(EventGraph parent)
        {
            _parent = parent;
        }

        public void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema,
            Action<string> executeSql)
        {
            if (_checkedSchema) return;

            _checkedSchema = true;

            var schemaExists = schema.DbObjects.TableExists(_parent.Table);

            if (schemaExists) return;

            if (autoCreateSchemaObjectsMode == AutoCreate.None)
            {
                throw new InvalidOperationException(
                    "The EventStore schema objects do not exist and the AutoCreateSchemaObjects is configured as " +
                    autoCreateSchemaObjectsMode);
            }

            lock (_locker)
            {
                if (!schema.DbObjects.TableExists(_parent.Table))
                {
                    var writer = new StringWriter();

                    writeBasicTables(schema, writer);

                    executeSql(writer.ToString());


                    // This is going to have to be done separately
                    // TODO -- doesn't work anyway. Do this differently somehow.


                    //var js = SchemaBuilder.GetJavascript("mt_transforms").Replace("'", "\"").Replace("\n", "").Replace("\r", "");
                    //var sql = $"insert into mt_modules (name, definition) values ('mt_transforms', '{js}');";
                    //executeSql(sql);

                    //executeSql("select mt_initialize_projections();");
                }
            }
        }

        private void writeBasicTables(IDocumentSchema schema, StringWriter writer)
        {
            var schemaName = _parent.DatabaseSchemaName;

            writer.WriteSql(schemaName, "mt_stream");
            writer.WriteSql(schemaName, "mt_initialize_projections");
            writer.WriteSql(schemaName, "mt_apply_transform");
            writer.WriteSql(schemaName, "mt_apply_aggregation");
        }

        public void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer)
        {
            writeBasicTables(schema, writer);

            // TODO -- need to load the projection and initialize
        }

        public void RemoveSchemaObjects(IManagedConnection connection)
        {
            throw new NotImplementedException();
        }

        public void ResetSchemaExistenceChecks()
        {
            _checkedSchema = false;
        }

        public TableDefinition StorageTable()
        {
            throw new NotSupportedException();
        }
    }
}