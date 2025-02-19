using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing.Identity;

public class using_long_identity : IntegrationContext
{
    [Fact]
    public async Task persist_and_load()
    {
        var LongDoc = new LongDoc { Id = 456 };

        theSession.Store(LongDoc);
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        (await session.LoadAsync<LongDoc>(456)).ShouldNotBeNull();

        (await session.LoadAsync<LongDoc>(222)).ShouldBeNull();
    }

    [Fact]
    public void auto_assign_id_for_0_id()
    {
        var doc = new LongDoc { Id = 0 };

        theSession.Store(doc);

        doc.Id.ShouldBeGreaterThan(0L);

        var doc2 = new LongDoc { Id = 0 };
        theSession.Store(doc2);

        doc2.Id.ShouldNotBe(0L);

        doc2.Id.ShouldNotBe(doc.Id);
    }

    [Fact]
    public async Task persist_and_delete()
    {
        var LongDoc = new LongDoc { Id = 567 };

        theSession.Store(LongDoc);
        await theSession.SaveChangesAsync();

        using (var session = theStore.LightweightSession())
        {
            session.Delete<LongDoc>((int) LongDoc.Id);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            (await session.LoadAsync<LongDoc>(LongDoc.Id)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task load_by_array_of_ids()
    {
        theSession.Store(new LongDoc{Id = 3});
        theSession.Store(new LongDoc{Id = 4});
        theSession.Store(new LongDoc{Id = 5});
        theSession.Store(new LongDoc{Id = 6});
        theSession.Store(new LongDoc{Id = 7});
        await theSession.SaveChangesAsync();

        using var session = theStore.QuerySession();
        (await session.LoadManyAsync<LongDoc>(4, 5, 6)).Count().ShouldBe(3);
    }

    public using_long_identity(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
