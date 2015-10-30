using System.IO;
using Marten.Generation;

namespace Marten.Schema
{
    public class DevelopmentSchemaCreation : IDocumentSchemaCreation
    {
        private readonly CommandRunner _runner;

        public DevelopmentSchemaCreation(IConnectionFactory factory)
        {
            _runner = new CommandRunner(factory);
        }

        public void CreateSchema(IDocumentSchema schema, DocumentMapping mapping)
        {
            var writer= new StringWriter();
            mapping.WriteSchemaObjects(schema, writer);

            _runner.Execute(writer.ToString());
        }
    }
}