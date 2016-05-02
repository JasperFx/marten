using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Generation;
using Marten.Services;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class DbObjects : IDbObjects
    {
        private readonly IConnectionFactory _factory;
        private readonly DocumentSchema _schema;
        private static readonly string SchemaObjectsSQL;
        static DbObjects()
        {
            SchemaObjectsSQL =
                Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(DbObjects), "SchemaObjects.sql")
                    .ReadAllText();
        }

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

        public IEnumerable<ActualIndex> AllIndexes()
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

            Func<DbDataReader, ActualIndex> transform = r => new ActualIndex(TableName.Parse(r.GetString(2)), r.GetString(3), r.GetString(4));

            return _factory.Fetch(sql, transform);
        }

        public IEnumerable<ActualIndex> IndexesFor(TableName table)
        {
            return AllIndexes().Where(x => x.Table.Equals(table)).ToArray();
        }

        public string DefinitionForFunction(FunctionName function)
        {
            var sql = @"
SELECT pg_get_functiondef(pg_proc.oid) 
FROM pg_proc JOIN pg_namespace as ns ON pg_proc.pronamespace = ns.oid WHERE ns.nspname = ? and proname = ?;
";

            return _factory.Fetch(sql, r => r.GetString(0), function.Schema, function.Name).FirstOrDefault();
        }

        public TableDefinition TableSchema(IDocumentMapping documentMapping)
        {
            var columns = findTableColumns(documentMapping);
            if (!columns.Any()) return null;

            var pkName = primaryKeysFor(documentMapping).SingleOrDefault();

            return new TableDefinition(documentMapping.Table, pkName, columns);
        }

        private IEnumerable<TableColumn> findTableColumns(IDocumentMapping documentMapping)
        {
            Func<DbDataReader, TableColumn> transform = r => new TableColumn(r.GetString(0), r.GetString(1));

            var sql =
                "select column_name, data_type from information_schema.columns where table_schema = ? and table_name = ? order by ordinal_position";

            return _factory.Fetch(sql, transform, documentMapping.Table.Schema, documentMapping.Table.Name);
        }

        private string[] primaryKeysFor(IDocumentMapping documentMapping)
        {
            var sql = @"
select a.attname, format_type(a.atttypid, a.atttypmod) as data_type
from pg_index i
join   pg_attribute a on a.attrelid = i.indrelid and a.attnum = ANY(i.indkey)
where attrelid = (select pg_class.oid 
                  from pg_class 
                  join pg_catalog.pg_namespace n ON n.oid = pg_class.relnamespace
                  where n.nspname = ? and relname = ?)
and i.indisprimary; 
";

            return _factory.GetStringList(sql, documentMapping.Table.Schema, documentMapping.Table.Name).ToArray();
        }


        // TODO -- Really need to add some QueryHandlers for all this stuff to eliminate the duplication
        public SchemaObjects FindSchemaObjects(DocumentMapping mapping)
        {

            using (var connection = new ManagedConnection(_factory))
            {
                return connection.Execute(cmd =>
                {
                    cmd.CommandText = SchemaObjectsSQL;
                    cmd.AddParameter("schema", mapping.Table.Schema);
                    cmd.AddParameter("table_name", mapping.Table.Name);
                    cmd.AddParameter("function", mapping.UpsertFunction.Name);
                    cmd.AddParameter("qualified_name", mapping.Table.OwnerName);

                    var reader = cmd.ExecuteReader();

                    var columns = new List<TableColumn>();
                    while (reader.Read())
                    {
                        var column = new TableColumn(reader.GetString(0), reader.GetString(1));
                        columns.Add(column);
                    }

                    var pks = new List<string>();
                    reader.NextResult();
                    while (reader.Read())
                    {
                        pks.Add(reader.GetString(0));
                    }

                    reader.NextResult();
                    var upsertDefinition = reader.Read() ? reader.GetString(0) : null;

                    var indices = new List<ActualIndex>();
                    reader.NextResult();
                    while (reader.Read())
                    {
                        var index = new ActualIndex(mapping.Table, reader.GetString(3),
                            reader.GetString(4));

                        indices.Add(index);
                    }

                    var table = columns.Any() ? new TableDefinition(mapping.Table, pks.FirstOrDefault(), columns) : null;

                    reader.NextResult();
                    var drops = new List<string>();
                    while (reader.Read())
                    {
                        drops.Add(reader.GetString(0));
                    }

                    return new SchemaObjects(mapping.DocumentType, table, indices.ToArray(), upsertDefinition, drops);
                });
            }
        }
    }
}