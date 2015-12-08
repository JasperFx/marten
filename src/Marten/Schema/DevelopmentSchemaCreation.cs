using System.Collections.Generic;
using System.IO;
using Baseline;
using Marten.Services;

namespace Marten.Schema
{
    public class DevelopmentSchemaCreation : IDocumentSchemaCreation
    {
        private readonly ICommandRunner _runner;

        public DevelopmentSchemaCreation(ICommandRunner runner)
        {
            _runner = runner;
        }

        public void CreateSchema(IDocumentSchema schema, DocumentMapping mapping)
        {
            var writer= new StringWriter();
            SchemaBuilder.WriteSchemaObjects(mapping, schema, writer);

            _runner.Execute(writer.ToString());
        }


        public void RunScript(string script)
        {
            var sql = SchemaBuilder.GetText(script);

            _runner.Execute(sql);
        }
    }
}