using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing.Identity;

public class using_int_identity : IntegrationContext
{
    [Fact]
    public async Task persist_and_load()
    {
        var IntDoc = new IntDoc { Id = 456 };

        theSession.Store(IntDoc);
        await theSession.SaveChangesAsync();

        using var session = theStore.LightweightSession();
        (await session.LoadAsync<IntDoc>(456)).ShouldNotBeNull();

        (await session.LoadAsync<IntDoc>(222)).ShouldBeNull();
    }

    [Fact]
    public void auto_assign_id_for_0_id()
    {
        var doc = new IntDoc {Id = 0};

        theSession.Store(doc);

        doc.Id.ShouldBeGreaterThan(0);

        var doc2 = new IntDoc {Id = 0};
        theSession.Store(doc2);

        doc2.Id.ShouldNotBe(0);

        doc2.Id.ShouldNotBe(doc.Id);
    }

    [Fact]
    public async Task persist_and_delete()
    {
        var IntDoc = new IntDoc { Id = 567 };

        theSession.Store(IntDoc);
        await theSession.SaveChangesAsync();

        using (var session = theStore.LightweightSession())
        {
            session.Delete<IntDoc>(IntDoc.Id);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            (await session.LoadAsync<IntDoc>(IntDoc.Id)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task load_by_array_of_ids()
    {
        theSession.Store(new IntDoc { Id = 3 });
        theSession.Store(new IntDoc { Id = 4 });
        theSession.Store(new IntDoc { Id = 5 });
        theSession.Store(new IntDoc { Id = 6 });
        theSession.Store(new IntDoc { Id = 7 });
        await theSession.SaveChangesAsync();

        using var session = theStore.QuerySession();
        (await session.LoadManyAsync<IntDoc>(4, 5, 6)).Count.ShouldBe(3);
    }

    public using_int_identity(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
