using System;
using System.IO;
using System.Text;
using Marten.Generation.Templates;

namespace Marten.Generation
{
    public class SchemaBuilder
    {
        public static string TableNameFor(Type documentType)
        {
            return "mt_doc_" + documentType.Name;
        }

        private readonly StringWriter _writer = new StringWriter();

        public void CreateTable(Type documentType)
        {
            var sql = TemplateSource.DocumentTable().Replace("%TABLE_NAME%", TableNameFor(documentType));

            _writer.WriteLine(sql);
            _writer.WriteLine();
        }

        public string ToSql()
        {
            return _writer.ToString();
        }
    }
}