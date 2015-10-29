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

        public void CreateSchema(IDocumentStorage storage)
        {
            var builder = new SchemaBuilder();
            storage.InitializeSchema(builder);

            _runner.Execute(builder.ToSql());
        }
    }
}