using System;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Marten.Testing.OtherAssembly.Bug1984;
using Marten.Testing.ThirdAssembly.Bug1984;
using Xunit;

namespace DocumentDbTests.Bugs
{
    public class Bug_1984_need_to_recursively_reference_assemblies_in_lamar_code_generation : BugIntegrationContext
    {
        public Bug_1984_need_to_recursively_reference_assemblies_in_lamar_code_generation()
        {
            StoreOptions(_ => _.Schema.For<GenericEntity<Data>>());
        }

        [Fact]
        public async Task Should_be_able_execute_query()
        {
            theSession
                .Store(new GenericEntity<Data>
                {
                    Id = Guid.NewGuid(),
                    Data = new Data()
                    {
                        SomeField = "Hello",
                    }
                });
            await theSession.SaveChangesAsync().ConfigureAwait(false);
        }
    }

}
