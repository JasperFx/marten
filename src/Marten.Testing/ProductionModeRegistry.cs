using Marten.Schema;

namespace Marten.Testing
{
    public class ProductionModeRegistry : DevelopmentModeRegistry
    {
        public ProductionModeRegistry()
        {
            For<IDocumentSchemaCreation>().Use<ProductionSchemaCreation>();
        }
    }
}