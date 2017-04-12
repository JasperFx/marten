using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Generation;
using Marten.Schema;
using Marten.Util;
using Npgsql;

namespace Marten.Storage
{
    public class Table : ISchemaObject, IEnumerable<TableColumn>
    {
        public readonly IList<TableColumn> _columns = new List<TableColumn>();

        public DbObjectName Identifier { get; }

        public IList<IIndexDefinition> Indexes { get; } = new List<IIndexDefinition>();

        public Table(DbObjectName name)
        {
            Identifier = name;
        }

        public void AddPrimaryKey(TableColumn column)
        {
            PrimaryKey = column;
            column.Directive = $"CONSTRAINT pk_{Identifier.Name} PRIMARY KEY";
            _columns.Add(column);
        }

        public TableColumn PrimaryKey { get; private set; }

        public void AddColumn<T>() where T : TableColumn, new()
        {
            _columns.Add(new T());
        }

        public TableColumn AddColumn(string name, string type, string directive = null)
        {
            var column = new TableColumn(name, type)
            {
                Directive = directive
            };

            AddColumn(column);

            return column;
        }

        public void AddColumn(TableColumn column)
        {
            _columns.Add(column);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<TableColumn> GetEnumerator()
        {
            return _columns.GetEnumerator();
        }

        public virtual void Write(DdlRules rules, StringWriter writer)
        {
            if (rules.TableCreation == CreationStyle.DropThenCreate)
            {
                writer.WriteLine("DROP TABLE IF EXISTS {0} CASCADE;", Identifier);
                writer.WriteLine("CREATE TABLE {0} (", Identifier);
            }
            else
            {
                writer.WriteLine("CREATE TABLE IF NOT EXISTS {0} (", Identifier);
            }

            var length = _columns.Select(x => x.Name.Length).Max() + 4;

            foreach (var column in _columns)
            {
                writer.Write($"    {column.ToDeclaration(length)}");
                if (ReferenceEquals(column, _columns.Last()))
                {
                    writer.WriteLine();
                }
                else
                {
                    writer.WriteLine(",");
                }
            }

            writer.WriteLine(");");

            writer.WriteLine(OriginWriter.OriginStatement("TABLE", Identifier.QualifiedName));
        }


        public void WriteDropStatement(DdlRules rules, StringWriter writer)
        {
            writer.WriteLine($"DROP TABLE IF EXISTS {Identifier} CASCADE;");
        }

        public void ConfigureQueryCommand(CommandBuilder builder)
        {
            var schemaParam = builder.AddParameter(Identifier.Schema).ParameterName;
            var nameParam = builder.AddParameter(Identifier.Name).ParameterName;
            var qualifiedNameParam = builder.AddParameter(Identifier.ToString()).ParameterName;

            builder.Append($@"
select column_name, data_type, character_maximum_length 
from information_schema.columns where table_schema = :{schemaParam} and table_name = :{nameParam}
order by ordinal_position;

select a.attname, format_type(a.atttypid, a.atttypmod) as data_type
from pg_index i
join   pg_attribute a on a.attrelid = i.indrelid and a.attnum = ANY(i.indkey)
where attrelid = (select pg_class.oid 
                  from pg_class 
                  join pg_catalog.pg_namespace n ON n.oid = pg_class.relnamespace
                  where n.nspname = :{schemaParam} and relname = :{nameParam})
and i.indisprimary; 

-- Indexes
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
WHERE NOT nspname LIKE 'pg%' AND i.relname like 'mt_%' AND pg_catalog.textin(pg_catalog.regclassout(idx.indrelid :: REGCLASS)) = :{qualifiedNameParam}; -- Excluding system table

select constraint_name 
from information_schema.table_constraints as c
where 
  c.constraint_name LIKE 'mt_%' and 
  c.constraint_type = 'FOREIGN KEY' and 
  c.table_schema = :{schemaParam} and
  c.table_name = :{nameParam};

");
        }

        public Table FetchExisting(NpgsqlConnection conn)
        {
            var cmd = conn.CreateCommand();
            var builder = new CommandBuilder(cmd);

            ConfigureQueryCommand(builder);

            cmd.CommandText = builder.ToString();

            using (var reader = cmd.ExecuteReader())
            {
                return readExistingTable(reader);
            }
        }

        public TableDelta FetchDelta(NpgsqlConnection conn)
        {
            var actual = FetchExisting(conn);
            if (actual == null) return null;

            return new TableDelta(this, actual);
        }

        public SchemaPatchDifference CreatePatch(DbDataReader reader, SchemaPatch patch, AutoCreate autoCreate)
        {
            var existing = readExistingTable(reader);
            if (existing == null)
            {
                Write(patch.Rules, patch.UpWriter);
                patch.Rollbacks.Drop(this, Identifier);

                return SchemaPatchDifference.Create;
            }

            var delta = new TableDelta(this, existing);
            if (delta.Matches)
            {
                return SchemaPatchDifference.None;
            }

            if (delta.Extras.Any() || delta.Different.Any())
            {
                if (autoCreate == AutoCreate.All)
                {
                    WriteDropStatement(patch.Rules, patch.UpWriter);
                    Write(patch.Rules, patch.UpWriter);

                    return SchemaPatchDifference.Create;
                }

                return SchemaPatchDifference.Invalid;
            }

            if (!delta.Missing.All(x => x.CanAdd))
            {
                return SchemaPatchDifference.Invalid;
            }

            foreach (var missing in delta.Missing)
            {
                patch.Updates.Apply(this, missing.AddColumnSql(this));
                patch.Rollbacks.RemoveColumn(this, Identifier, missing.Name);
            }

            return SchemaPatchDifference.Update;
        }

        private Table readExistingTable(DbDataReader reader)
        {
            var columns = readColumns(reader);
            var pks = readPrimaryKeys(reader);
            readIndexes(reader);
            readConstraints(reader);


            if (!columns.Any()) return null;

            var existing = new Table(Identifier);
            foreach (var column in columns)
            {
                existing.AddColumn(column);
            }

            if (pks.Any())
            {
                existing.SetPrimaryKey(pks.First());
            }


            return existing;
        }

        private static void readConstraints(DbDataReader reader)
        {
            reader.NextResult();
            var constraints = new List<string>();
            while (reader.Read())
            {
                constraints.Add(reader.GetString(0));
            }
        }

        private void readIndexes(DbDataReader reader)
        {
            reader.NextResult();
            var indices = new List<ActualIndex>();
            reader.NextResult();
            while (reader.Read())
            {
                var index = new ActualIndex(Identifier, reader.GetString(3),
                    reader.GetString(4));

                indices.Add(index);
            }
        }

        private static List<string> readPrimaryKeys(DbDataReader reader)
        {
            var pks = new List<string>();
            reader.NextResult();
            while (reader.Read())
            {
                pks.Add(reader.GetString(0));
            }
            return pks;
        }

        private static List<TableColumn> readColumns(DbDataReader reader)
        {
            var columns = new List<TableColumn>();
            while (reader.Read())
            {
                var column = new TableColumn(reader.GetString(0), reader.GetString(1));

                if (!reader.IsDBNull(2))
                {
                    var length = reader.GetInt32(2);
                    column.Type = $"{column.Type}({length})";
                }

                columns.Add(column);
            }
            return columns;
        }

        public void SetPrimaryKey(string columnName)
        {
            var column = _columns.FirstOrDefault(x => x.Name == columnName);
            PrimaryKey = column;
        }

        public bool HasColumn(string columnName)
        {
            return _columns.Any(x => x.Name == columnName);
        }

        public TableColumn ColumnFor(string columnName)
        {
            return _columns.FirstOrDefault(x => x.Name == columnName);
        }

        public void RemoveColumn(string columnName)
        {
            _columns.RemoveAll(x => x.Name == columnName);
        }
    }
}