using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FubuCore.Util.TextWriting;
using Marten.Generation.Templates;

namespace Marten.Generation
{
    public class SchemaBuilder
    {
        public static string TableNameFor(Type documentType)
        {
            return "mt_doc_" + documentType.Name;
        }

        public static string UpsertNameFor(Type documentType)
        {
            return "mt_upsert_" + documentType.Name;
        }

        private readonly StringWriter _writer = new StringWriter();

        public void CreateTable(Type documentType)
        {
            // TODO -- fancier later
            var table = new TableDefinition(TableNameFor(documentType), new TableColumn("id", "uuid"));
            table.Columns.Add(new TableColumn("data", "jsonb NOT NULL"));

            table.Write(_writer);

            //var sql = TemplateSource.DocumentTable().Replace("%TABLE_NAME%", TableNameFor(documentType));

            //_writer.WriteLine(sql);
            _writer.WriteLine();
        }

        public string ToSql()
        {
            return _writer.ToString();
        }

        public void DefineUpsert(Type documentType)
        {
            var sql = TemplateSource.UpsertDocument()
                .Replace("%TABLE_NAME%", TableNameFor(documentType))
                .Replace("%SPROC_NAME%", UpsertNameFor(documentType));

            _writer.WriteLine(sql);
            _writer.WriteLine();
        }
    }

    public class TableDefinition
    {
        public TableDefinition(string name, TableColumn primaryKey)
        {
            Name = name;
            PrimaryKey = primaryKey;
        }

        public string Name { get; set; }

        public TableColumn PrimaryKey;

        public readonly IList<TableColumn> Columns = new List<TableColumn>();

        public void Write(StringWriter writer)
        {
            writer.WriteLine("DROP TABLE IF EXISTS {0} CASCADE;", Name);
            writer.WriteLine("CREATE TABLE {0} (", Name);

            var length = Columns.Select(x => x.Name.Length).Max() + 4;

            writer.WriteLine("    {0}{1} CONSTRAINT pk_{2} PRIMARY KEY,", PrimaryKey.Name.PadRight(length), PrimaryKey.Type, Name);

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
        public TableColumn(string name, string type)
        {
            Name = name;
            Type = type;
        }

        public string Name;
        public string Type;
    }


}