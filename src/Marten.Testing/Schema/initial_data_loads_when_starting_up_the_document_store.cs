using Marten.Schema;
using Marten.Testing.Harness;
using NSubstitute;
using Xunit;

namespace Marten.Testing.Schema
{
    public class initial_data_loads_when_starting_up_the_document_store : IntegrationContext
    {
        [Fact]
        public void runs_all_the_initial_data_sets_on_startup()
        {
            var data1 = Substitute.For<IInitialData>();
            var data2 = Substitute.For<IInitialData>();
            var data3 = Substitute.For<IInitialData>();

            StoreOptions(_ =>
            {
                _.InitialData.Add(data1);
                _.InitialData.Add(data2);
                _.InitialData.Add(data3);
            });

            theStore.ShouldNotBeNull();

            data1.Received().Populate(theStore);
            data2.Received().Populate(theStore);
            data3.Received().Populate(theStore);
        }

        public initial_data_loads_when_starting_up_the_document_store(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
