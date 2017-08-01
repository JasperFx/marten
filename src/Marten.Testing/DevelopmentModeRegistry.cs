using StructureMap;
using StructureMap.Pipeline;

namespace Marten.Testing
{


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