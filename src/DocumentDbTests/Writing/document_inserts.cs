using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing;

public class document_inserts: IntegrationContext
{
    [Fact]
    public void can_insert_all_new_documents()
    {
        using (var session = theStore.LightweightSession())
        {
            session.Insert(Target.GenerateRandomData(99).ToArray());
            session.SaveChanges();
        }

        using (var query = theStore.QuerySession())
        {
            query.Query<Target>().Count().ShouldBe(99);
        }
    }

    [Fact]
    public async Task can_insert_a_mixed_bag_of_documents()
    {
        var docs = new object[]
        {
            Target.Random(), Target.Random(), Target.Random(), new User(), new User(), new User(), new User()
        };

        await using (var session = theStore.LightweightSession())
        {
            session.InsertObjects(docs);
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            query.Query<Target>().Count().ShouldBe(3);
            query.Query<User>().Count().ShouldBe(4);
        }
    }

    [Fact]
    public void can_insert_records()
    {
        var docs = new RecordDocument(Guid.NewGuid(), Guid.NewGuid().ToString());

        using (var session = theStore.LightweightSession())
        {
            session.Store(docs);
            session.SaveChanges();
        }

        using (var query = theStore.QuerySession())
        {
            query.Query<RecordDocument>().ToList().Count().ShouldBe(1);
        }
    }

    public record RecordDocument(Guid Id, string Name);


    [Fact]
    public void insert_sad_path()
    {
        var target = Target.Random();

        #region sample_sample-document-insertonly

        using (var session = theStore.LightweightSession())
        {
            session.Insert(target);
            session.SaveChanges();
        }

        #endregion

        using (var session = theStore.LightweightSession())
        {
            Exception<DocumentAlreadyExistsException>.ShouldBeThrownBy(() =>
            {
                session.Insert(target);
                session.SaveChanges();
            });
        }
    }

    public document_inserts(DefaultStoreFixture fixture): base(fixture)
    {
    }
}
