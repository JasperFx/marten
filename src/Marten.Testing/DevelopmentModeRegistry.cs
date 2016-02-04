using Marten.Events;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Remotion.Linq.Parsing.Structure;
using StructureMap.Configuration.DSL;
using StructureMap.Pipeline;

namespace Marten.Testing
{
    public class DevelopmentModeRegistry : Registry
    {
        public DevelopmentModeRegistry()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = true;
                
            });

            store.Schema.UpsertType = UpsertType;

            For<IDocumentStore>().Use(store);
            For<IIdentityMap>().Use<NulloIdentityMap>();

            For<IDocumentSchema>().Use(store.Schema);
            For<IConnectionFactory>().Use<ConnectionSource>();

            For<IDocumentSession>().Use<DocumentSession>();
            For<ICommandRunner>().Use<CommandRunner>();
            For<IDocumentCleaner>().Use<DocumentCleaner>();
            For<ISerializer>().Use<JsonNetSerializer>();


            ForSingletonOf<IQueryParser>().Use<MartenQueryParser>();

            For<IQuerySession>().Use<QuerySession>().Ctor<IIdentityMap>().Is<NulloIdentityMap>();

            For<IEventStore>().Use<EventStore>();
            For<IMartenQueryExecutor>().Use<MartenQueryExecutor>();

            For<IDocumentSchemaCreation>().Use<DevelopmentSchemaCreation>();
        }

        public static PostgresUpsertType UpsertType { get; set; } = PostgresUpsertType.Legacy;
    }

    // SAMPLE: MartenServices
    public class MartenServices : Registry
    {
        public MartenServices()
        {
            ForSingletonOf<IDocumentStore>().Use("Build the DocumentStore", () =>
            {
                return DocumentStore.For(_ =>
                {
                    _.Connection("your connection string");
                    _.AutoCreateSchemaObjects = false;
                    
                    // other Marten configuration options
                });
            });

            For<IDocumentSession>()
                .LifecycleIs<ContainerLifecycle>() // not really necessary in some frameworks
                .Use(c => c.GetInstance<IDocumentStore>().LightweightSession());
        }
    }
    // ENDSAMPLE
}