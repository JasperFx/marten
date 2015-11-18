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
            ForSingletonOf<IDocumentSchema>()
                .Use<DocumentSchema>()
                .OnCreation("", x => x.UpsertType = UpsertType);
            Forward<DocumentSchema, IDocumentSchema>();
            For<IDocumentSession>().Use<DocumentSession>();
            For<ISerializer>().Use<JsonNetSerializer>();
            For<IDocumentSchemaCreation>().Use<DevelopmentSchemaCreation>();
            For<ICommandRunner>().Use<CommandRunner>();
            For<IDocumentCleaner>().Use<DocumentCleaner>();
            For<IMartenQueryExecutor>().Use<MartenQueryExecutor>();

            ForSingletonOf<IQueryParser>().Use<MartenQueryParser>();
        }

        public static PostgresUpsertType UpsertType { get; set; }
    }
}