using Marten.Linq;
using Marten.Schema;
using Remotion.Linq.Parsing.Structure;
using StructureMap.Configuration.DSL;

namespace Marten.Testing
{
    public class DevelopmentModeRegistry : Registry
    {
        public DevelopmentModeRegistry()
        {
            For<IConnectionFactory>().Use<ConnectionSource>();
            ForSingletonOf<IDocumentSchema>().Use<Marten.Schema.DocumentSchema>();
            For<IDocumentSession>().Use<DocumentSession>();
            For<ISerializer>().Use<JsonNetSerializer>();
            For<IDocumentSchemaCreation>().Use<DevelopmentSchemaCreation>();

            For<IMartenQueryExecutor>().Use<MartenQueryExecutor>();

            ForSingletonOf<IQueryParser>().Use<MartenQueryParser>();
        }
    }
}