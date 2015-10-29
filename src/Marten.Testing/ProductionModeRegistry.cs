using Marten.Linq;
using Marten.Schema;
using Remotion.Linq.Parsing.Structure;
using StructureMap.Configuration.DSL;

namespace Marten.Testing
{
    public class ProductionModeRegistry : Registry
    {
        public ProductionModeRegistry()
        {
            For<IConnectionFactory>().Use<ConnectionSource>();
            ForSingletonOf<IDocumentSchema>().Use<Marten.Schema.DocumentSchema>();
            For<IDocumentSession>().Use<DocumentSession>();
            For<ISerializer>().Use<JsonNetSerializer>();
            For<IDocumentSchemaCreation>().Use<ProductionSchemaCreation>();

            For<IMartenQueryExecutor>().Use<MartenQueryExecutor>();

            ForSingletonOf<IQueryParser>().Use<MartenQueryParser>();
        }
    }
}