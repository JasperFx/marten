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
    public async Task can_insert_all_new_documents()
    {
        using (var session = theStore.LightweightSession())
        {
            session.Insert(Target.GenerateRandomData(99).ToArray());
            await session.SaveChangesAsync();
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
    public async Task can_insert_records()
    {
        var docs = new RecordDocument(Guid.NewGuid(), Guid.NewGuid().ToString());

        using (var session = theStore.LightweightSession())
        {
            session.Store(docs);
            await session.SaveChangesAsync();
        }

        using (var query = theStore.QuerySession())
        {
            query.Query<RecordDocument>().ToList().Count().ShouldBe(1);
        }
    }

    public record RecordDocument(Guid Id, string Name);


    [Fact]
    public async Task insert_sad_path()
    {
        var target = Target.Random();

        #region sample_sample-document-insertonly

        using (var session = theStore.LightweightSession())
        {
            session.Insert(target);
            await session.SaveChangesAsync();
        }

        #endregion

        using (var session = theStore.LightweightSession())
        {
            await Should.ThrowAsync<DocumentAlreadyExistsException>(async () =>
            {
                session.Insert(target);
                await session.SaveChangesAsync();
            });
        }
    }

    public document_inserts(DefaultStoreFixture fixture): base(fixture)
    {
    }
}
