using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing.Identity;

public class using_int_identity : IntegrationContext
{
    [Fact]
    public void persist_and_load()
    {
        var IntDoc = new IntDoc { Id = 456 };

        theSession.Store(IntDoc);
        theSession.SaveChanges();

        using (var session = theStore.OpenSession())
        {
            SpecificationExtensions.ShouldNotBeNull(session.Load<IntDoc>(456));

            SpecificationExtensions.ShouldBeNull(session.Load<IntDoc>(222));
        }

    }

    [Fact]
    public void auto_assign_id_for_0_id()
    {
        var doc = new IntDoc {Id = 0};

        theSession.Store(doc);

        SpecificationExtensions.ShouldBeGreaterThan(doc.Id, 0);

        var doc2 = new IntDoc {Id = 0};
        theSession.Store(doc2);

        doc2.Id.ShouldNotBe(0);

        doc2.Id.ShouldNotBe(doc.Id);
    }

    [Fact]
    public void persist_and_delete()
    {
        var IntDoc = new IntDoc { Id = 567 };

        theSession.Store(IntDoc);
        theSession.SaveChanges();

        using (var session = theStore.OpenSession())
        {
            session.Delete<IntDoc>(IntDoc.Id);
            session.SaveChanges();
        }

        using (var session = theStore.OpenSession())
        {
            SpecificationExtensions.ShouldBeNull(session.Load<IntDoc>(IntDoc.Id));
        }
    }

    [Fact]
    public void load_by_array_of_ids()
    {
        theSession.Store(new IntDoc { Id = 3 });
        theSession.Store(new IntDoc { Id = 4 });
        theSession.Store(new IntDoc { Id = 5 });
        theSession.Store(new IntDoc { Id = 6 });
        theSession.Store(new IntDoc { Id = 7 });
        theSession.SaveChanges();

        using (var session = theStore.OpenSession())
        {
            session.LoadMany<IntDoc>(4, 5, 6).Count().ShouldBe(3);
        }
    }

    public using_int_identity(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}