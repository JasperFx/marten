using System;
using System.IO;
using Marten.Generation.Templates;
using Marten.Schema;

namespace Marten.Generation
{
    public class SchemaBuilder
    {
        private readonly StringWriter _writer = new StringWriter();

        public void CreateTable(TableDefinition table)
        {
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
                .Replace("%TABLE_NAME%", DocumentMapping.TableNameFor(documentType))
                .Replace("%SPROC_NAME%", DocumentMapping.UpsertNameFor(documentType))
                .Replace("%ID_TYPE%", TypeMappings.PgTypes[idType]);

            _writer.WriteLine(sql);
            _writer.WriteLine();
        }
    }
}