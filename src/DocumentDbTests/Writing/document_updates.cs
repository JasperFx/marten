using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing;

public class document_updates: IntegrationContext
{


    [Fact]
    public async Task can_update_existing_documents()
    {
        var targets = Target.GenerateRandomData(99).ToArray();
        await theStore.BulkInsertAsync(targets);

        var theNewNumber = 54321;
        using (var session = theStore.LightweightSession())
        {
            targets[0].Double = theNewNumber;
            session.Update(targets[0]);
            await session.SaveChangesAsync();
        }

        using (var query = theStore.QuerySession())
        {
            (await query.LoadAsync<Target>(targets[0].Id))
                .Double.ShouldBe(theNewNumber);
        }
    }

    [Fact]
    public async Task update_sad_path()
    {
        var target = Target.Random();

        using var session = theStore.LightweightSession();

        await Should.ThrowAsync<NonExistentDocumentException>(async () =>
        {
            session.Update(target);
            await session.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task update_sad_path_async()
    {
        var target = Target.Random();

        await using var session = theStore.LightweightSession();
        await Should.ThrowAsync<NonExistentDocumentException>(async () =>
        {
            session.Update(target);
            await session.SaveChangesAsync();
        });
    }

    public document_updates(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
