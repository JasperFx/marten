using System;
using System.Collections.Generic;
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

        public IEnumerable<IndexDef> AllIndexes()
        {
            var sql = @"
SELECT
  U.usename                AS user_name,
  ns.nspname               AS schema_name,
  pg_catalog.textin(pg_catalog.regclassout(idx.indrelid :: REGCLASS)) AS table_name,
  i.relname                AS index_name,
  pg_get_indexdef(i.oid) as ddl,
  idx.indisunique          AS is_unique,
  idx.indisprimary         AS is_primary,
  am.amname                AS index_type,
  idx.indkey,
       ARRAY(
           SELECT pg_get_indexdef(idx.indexrelid, k + 1, TRUE)
           FROM
             generate_subscripts(idx.indkey, 1) AS k
           ORDER BY k
       ) AS index_keys,
  (idx.indexprs IS NOT NULL) OR (idx.indkey::int[] @> array[0]) AS is_functional,
  idx.indpred IS NOT NULL AS is_partial
FROM pg_index AS idx
  JOIN pg_class AS i
    ON i.oid = idx.indexrelid
  JOIN pg_am AS am
    ON i.relam = am.oid
  JOIN pg_namespace AS NS ON i.relnamespace = NS.OID
  JOIN pg_user AS U ON i.relowner = U.usesysid
WHERE NOT nspname LIKE 'pg%' AND i.relname like 'mt_%'; -- Excluding system table

";

            Func<DbDataReader, IndexDef> transform = r => new IndexDef(TableName.Parse(r.GetString(2)), r.GetString(3), r.GetString(4));

            return _factory.Fetch(sql, transform);
        }
    }
}