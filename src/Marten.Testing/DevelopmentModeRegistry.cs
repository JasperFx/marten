using System;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Remotion.Linq.Parsing.Structure;
using StructureMap;
using StructureMap.Pipeline;

namespace Marten.Testing
{
    public class DevelopmentModeRegistry : Registry
    {
        public DevelopmentModeRegistry() : this(null)
        {
        }

        public DevelopmentModeRegistry(Action<StoreOptions> optionsHandler = null)
        {
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = AutoCreate.All;
                optionsHandler?.Invoke(_);
            });

            For<IMartenLogger>().Use<NulloMartenLogger>();

            For<IDocumentStore>().Use(store);
            For<IIdentityMap>().Use<NulloIdentityMap>();

            For<IDocumentSchema>().Use(store.Schema);
            For<IConnectionFactory>().Use<ConnectionSource>();

            For<IDocumentSession>().Use<DocumentSession>();
            For<IManagedConnection>().Use<ManagedConnection>().SelectConstructor(() => new ManagedConnection(null));
            For<IDocumentCleaner>().Use<DocumentCleaner>();
            For<ISerializer>().Use<JsonNetSerializer>();


            ForSingletonOf<IQueryParser>().Use<MartenQueryParser>();

            For<IQuerySession>().Use<QuerySession>().Ctor<IIdentityMap>().Is<NulloIdentityMap>();

            For<StoreOptions>().Use(c => c.GetInstance<IDocumentStore>().Advanced.Options);

        }

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
                    _.AutoCreateSchemaObjects = AutoCreate.None;
                    
                    // other Marten configuration options
                });
            });

            For<IDocumentSession>()
                .LifecycleIs<ContainerLifecycle>() // not really necessary in some frameworks
                .Use("Lightweight Session", c => c.GetInstance<IDocumentStore>().LightweightSession());
        }
    }
    // ENDSAMPLE
}