using Marten.Events;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
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

            For<IIdentityMap>().Use<NulloIdentityMap>();
            For<IDocumentSession>().Use<DocumentSession>();

            For<ISerializer>().Use<JsonNetSerializer>();
            For<IDocumentSchemaCreation>().Use<DevelopmentSchemaCreation>();
            For<ICommandRunner>().Use<CommandRunner>();
            For<IDocumentCleaner>().Use<DocumentCleaner>();
            For<IMartenQueryExecutor>().Use<MartenQueryExecutor>();

            For<IDiagnostics>().Use<Diagnostics>();

            For<IDocumentStore>().Use<DocumentStore>();

            ForSingletonOf<IQueryParser>().Use<MartenQueryParser>();

            For<IQuerySession>().Use<QuerySession>().Ctor<IIdentityMap>().Is<NulloIdentityMap>();

            For<IEventStore>().Use<EventStore>();
        }

        public static PostgresUpsertType UpsertType { get; set; } = PostgresUpsertType.Legacy;
    }
}