using System;
using System.IO;
using Marten.Generation.Templates;

namespace Marten.Generation
{
    public class SchemaBuilder
    {
        private readonly StringWriter _writer = new StringWriter();

        public static string TableNameFor(Type documentType)
        {
            return "mt_doc_" + documentType.Name.ToLower();
        }

        public static string UpsertNameFor(Type documentType)
        {
            return "mt_upsert_" + documentType.Name.ToLower();
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
}