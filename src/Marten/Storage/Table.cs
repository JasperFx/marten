using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using Marten.Generation;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Storage
{
    public class Table : ISchemaObject, IEnumerable<TableColumn>
    {
        public readonly IList<TableColumn> _columns = new List<TableColumn>();

        public DbObjectName Identifier { get; }

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

        public TableColumn AddColumn(string name, string type)
        {
            var column = new TableColumn(name, type);
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

        public SchemaPatchDifference CreatePatch(DbDataReader reader, SchemaPatch patch)
        {
            var existing = readExistingTable(reader);

            throw new NotImplementedException();
        }

        private Table readExistingTable(DbDataReader reader)
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

            var pks = new List<string>();
            reader.NextResult();
            while (reader.Read())
            {
                pks.Add(reader.GetString(0));
            }

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

        public void SetPrimaryKey(string columnName)
        {
            var column = _columns.FirstOrDefault(x => x.Name == columnName);
            PrimaryKey = column;
        }
    }


}