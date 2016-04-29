using System;
using System.Data.Common;
using System.Linq;

namespace Marten.Schema
{
    public class DbObjects : IDbObjects
    {
        private readonly IConnectionFactory _factory;
        private readonly DocumentSchema _schema;

        public DbObjects(IConnectionFactory factory, DocumentSchema schema)
        {
            _factory = factory;
            _schema = schema;
        }

        public TableName[] DocumentTables()
        {
            return SchemaTables().Where(x => x.Name.StartsWith(DocumentMapping.TablePrefix)).ToArray();
        }

        public FunctionName[] SchemaFunctionNames()
        {
            Func<DbDataReader, FunctionName> transform = r => new FunctionName(r.GetString(0), r.GetString(1));

            var sql =
                "SELECT specific_schema, routine_name FROM information_schema.routines WHERE type_udt_name != 'trigger' and routine_name like ? and specific_schema = ANY(?);";

            return
                _factory.Fetch(sql, transform, DocumentMapping.MartenPrefix + "%", _schema.AllSchemaNames()).ToArray();
        }

        public TableName[] SchemaTables()
        {
            Func<DbDataReader, TableName> transform = r => new TableName(r.GetString(0), r.GetString(1));

            var sql =
                "select table_schema, table_name from information_schema.tables where table_name like ? and table_schema = ANY(?);";

            var schemaNames = _schema.AllSchemaNames();

            var tablePattern = DocumentMapping.MartenPrefix + "%";
            var tables = _factory.Fetch(sql, transform, tablePattern, schemaNames).ToArray();


            return tables;
        }

        public bool TableExists(TableName table)
        {
            var schemaTables = SchemaTables();
            return schemaTables.Contains(table);
        }
    }
}