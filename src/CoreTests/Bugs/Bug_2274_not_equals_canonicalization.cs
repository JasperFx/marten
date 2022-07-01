using System;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Bugs
{
    public class Bug_2274_not_equals_canonicalization : BugIntegrationContext, IAsyncLifetime
    {
        public Bug_2274_not_equals_canonicalization()
        {
            StoreOptions(opts =>
            {
                opts.Schema.For<SomeData>().Index(s => s.SomeField,
                    y =>
                    {
                        y.IsUnique = true;
                        y.Predicate = $@"(data ->> 'SomeField' IS NOT NULL AND data ->> 'SomeField' != '')";
                    });
            });
        }


        [Fact]
        public async Task Marten_Correctly_Throws_Index_Exception_When_Inserting_Using_Bang_Equals()
        {
            await using var session = theStore.LightweightSession();

            session.Store(new SomeData() { Id = Guid.NewGuid(), SomeField = "1" });
            session.Store(new SomeData() { Id = Guid.NewGuid(), SomeField = "1" });

            await Should.ThrowAsync<DocumentAlreadyExistsException>(() => session.SaveChangesAsync());
        }

        [Fact]
        public async Task Marten_Should_Not_Throw_Exception_When_Asserting_Configuration_For_Bang_Equals()
        {
            await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
        }

        public Task InitializeAsync()
        {
            return theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        public Task DisposeAsync()
        {
            Dispose();
            return Task.CompletedTask;
        }
    }

    public class SomeData
    {
        public Guid Id { get; set; }

        public string SomeField { get; set; }
    }
}
