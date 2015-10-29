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
            ForSingletonOf<IDocumentSchema>().Use<Marten.Schema.DevelopmentDocumentSchema>();
            For<IDocumentSession>().Use<DocumentSession>();
            For<ISerializer>().Use<JsonNetSerializer>();

            For<IMartenQueryExecutor>().Use<MartenQueryExecutor>();

            ForSingletonOf<IQueryParser>().Use<MartenQueryParser>();
        }
    }
}