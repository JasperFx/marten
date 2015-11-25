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
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = true;
                
            });

            store.Schema.UpsertType = UpsertType;

            For<IDocumentStore>().Use(store);


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
}