using System;
using System.Threading.Tasks;
using Marten.Metadata;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_2169_concurrency_error : BugIntegrationContext
{
    [Fact]
    public async Task there_should_be_no_error()
    {
        StoreOptions(options =>
        {
            options.Schema.For<TestObject>().Duplicate(x => x.SomeIntData);
            options.Schema.For<TestObject>().UseOptimisticConcurrency(true);
        });

        var newObject = new TestObject
        {
            Id = Guid.NewGuid().ToString(),
            SomeIntData = 0,
        };

        await using( var session = theStore.LightweightSession())
        {
            session.Store(newObject);
            await session.SaveChangesAsync();
        }

        var rehydratedObject = new TestObject();
        await using (var session = theStore.LightweightSession())
        {
            rehydratedObject = await session.LoadAsync<TestObject>(newObject.Id);
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Store(rehydratedObject);
            //throws ConcurrencyException
            await session.SaveChangesAsync();
        }
    }
}

public class TestObject : IVersioned
{
    public string Id { get; set; }
    public int SomeIntData { get; set; }
    public Guid Version { get; set; }
}
