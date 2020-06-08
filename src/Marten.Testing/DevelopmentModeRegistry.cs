
using Lamar;

namespace Marten.Testing
{


    // SAMPLE: MartenServices
    public class MartenServices : ServiceRegistry
    {
        public MartenServices()
        {
            ForSingletonOf<IDocumentStore>().Use(c =>
            {
                return DocumentStore.For(options =>
                {
                    options.Connection("your connection string");
                    options.AutoCreateSchemaObjects = AutoCreate.None;

                    // other Marten configuration options
                });
            });

            // Register IDocumentSession as Scoped
            For<IDocumentSession>()
                .Use(c => c.GetInstance<IDocumentStore>().LightweightSession())
                .Scoped();

            // Register IQuerySession as Scoped
            For<IQuerySession>()
                .Use(c => c.GetInstance<IDocumentStore>().QuerySession())
                .Scoped();
        }
    }
    // ENDSAMPLE
}
