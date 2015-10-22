using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Marten.Generation.Templates;

namespace Marten.Generation
{
    public class SchemaBuilder
    {
        private readonly StringWriter _writer = new StringWriter();

        public static string TableNameFor(Type documentType)
        {
            return "mt_doc_" + documentType.Name;
        }

        public static string UpsertNameFor(Type documentType)
        {
            return "mt_upsert_" + documentType.Name;
        }

        public void CreateTable(Type documentType, Type idType)
        {
            // TODO -- fancier later
            var table = new TableDefinition(TableNameFor(documentType), new TableColumn("id", TypeMappings.PgTypes[idType]));
            table.Columns.Add(new TableColumn("data", "jsonb NOT NULL"));

            table.Write(_writer);

            _writer.WriteLine();
        }

        public string ToSql()
        {
            return _writer.ToString();
        }

        public void DefineUpsert(Type documentType, Type idType)
        {
            var sql = TemplateSource.UpsertDocument()
                .Replace("%TABLE_NAME%", TableNameFor(documentType))
                .Replace("%SPROC_NAME%", UpsertNameFor(documentType))
                .Replace("%ID_TYPE%", TypeMappings.PgTypes[idType]);

            _writer.WriteLine(sql);
            _writer.WriteLine();
        }
    }

    public class TableDefinition
    {
        public readonly IList<TableColumn> Columns = new List<TableColumn>();

        public TableColumn PrimaryKey;

        public TableDefinition(string name, TableColumn primaryKey)
        {
            Name = name;
            PrimaryKey = primaryKey;
        }

        public string Name { get; set; }

        public void Write(StringWriter writer)
        {
            writer.WriteLine("DROP TABLE IF EXISTS {0} CASCADE;", Name);
            writer.WriteLine("CREATE TABLE {0} (", Name);

            var length = Columns.Select(x => x.Name.Length).Max() + 4;

            writer.WriteLine("    {0}{1} CONSTRAINT pk_{2} PRIMARY KEY,", PrimaryKey.Name.PadRight(length),
                PrimaryKey.Type, Name);

            Columns.Each(col =>
            {
                writer.Write("    {0}{1}", col.Name.PadRight(length), col.Type);
                if (col == Columns.Last())
                {
                    writer.WriteLine();
                }
                else
                {
                    writer.WriteLine(",");
                }
            });

            writer.WriteLine(");");
        }
    }

    public class TableColumn
    {
        public string Name;
        public string Type;

        public TableColumn(string name, string type)
        {
            Name = name;
            Type = type;
        }
    }
}