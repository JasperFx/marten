using System;
using System.Collections.Generic;
using System.IO;
using Marten.Services;

namespace Marten.Schema
{
    public class DevelopmentSchemaCreation : IDocumentSchemaCreation
    {
        private readonly ICommandRunner _runner;
        private readonly object _lock = new object();

        public DevelopmentSchemaCreation(ICommandRunner runner)
        {
            _runner = runner;
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
            SchemaBuilder.WriteSchemaObjects(mapping, schema, writer);

            var sql = writer.ToString();
            try
            {
                _runner.Execute(sql);
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
                _runner.Execute(sql);
            }
            catch (Exception e)
            {
                throw new MartenSchemaException(sql, e);
            }
        }
    }
}