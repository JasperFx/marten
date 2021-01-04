using Marten.Testing.Harness;

namespace Marten.Testing.Events.Projections
{
    public class self_aggregates : IntegrationContext
    {
        public self_aggregates(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }

    public class PrivateConstructor
    {
        private PrivateConstructor()
        {

        }




    }
}
