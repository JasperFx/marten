using System;
using System.IO;

namespace Marten.Schema
{
    public class DevelopmentSchemaCreation : IDocumentSchemaCreation
    {
        private readonly IConnectionFactory _factory;
        private readonly IMartenLogger _logger;
        private readonly object _lock = new object();

        public DevelopmentSchemaCreation(IConnectionFactory factory, IMartenLogger logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public void CreateSchema(IDocumentSchema schema, IDocumentMapping mapping, Func<bool> shouldRegenerate)
        {
            if (shouldRegenerate())
            {
                lock (_lock)
                {
                    if (shouldRegenerate())
                    {
                        writeSchemaObjects(schema, mapping);
                    }
                }
            }
        }

        private void writeSchemaObjects(IDocumentSchema schema, IDocumentMapping mapping)
        {
            var writer = new StringWriter();

            mapping.WriteSchemaObjects(schema, writer);

            var sql = writer.ToString();

            try
            {
                _factory.RunSql(sql);
                _logger.SchemaChange(sql);
            }
            catch (Exception e)
            {
                throw new MartenSchemaException(mapping.DocumentType, sql, e);
            }
        }


        public void RunScript(string script)
        {
            var sql = SchemaBuilder.GetText(script);

            try
            {
                _factory.RunSql(sql);
            }
            catch (Exception e)
            {
                throw new MartenSchemaException(sql, e);
            }
        }
    }
}