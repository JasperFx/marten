using Marten;
using Marten.Linq.CreatedTimestamp;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using System.Linq;
using Weasel.Postgresql.Tables;
using Xunit;

namespace DocumentDbTests.Metadata;

public class created_timestamp_queries: IntegrationContext
{
    [Fact]
    public void creates_btree_index_for_mt_created()
    {
        var mapping = DocumentMapping.For<Customer>();
        var indexDefinition = mapping.Indexes.Cast<DocumentIndex>().Single(x => x.Columns.First() == SchemaConstants.CreatedTimestampColumn);

        indexDefinition.Method.ShouldBe(IndexMethod.btree);
    }

    #region sample_index-created-timestamp-via-attribute
    [IndexedCreatedTimestamp]
    public class Customer
    {
    }
    #endregion


    [Fact]
    public void query_created_before_docs()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2);
        session.SaveChanges();

        session.Store(user3, user4);
        session.SaveChanges();

        var epoch = session.MetadataFor(user4).CreatedTimestamp;

        // no where clause
        session.Query<User>()
            .Where(x => x.CreatedBefore(epoch))
            .OrderBy(x => x.UserName)
            .Select(x => x.UserName)
            .ToList()
            .ShouldHaveTheSameElementsAs("bar", "foo");

        // with a where clause
        session.Query<User>().Where(x => x.UserName != "bar" && x.CreatedBefore(epoch))
            .OrderBy(x => x.UserName)
            .ToList()
            .Select(x => x.UserName)
            .Single().ShouldBe("foo");
    }

    [Fact]
    public void query_created_since_docs()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2);
        session.SaveChanges();

        var epoch = session.MetadataFor(user2).CreatedTimestamp;
        session.Store(user3, user4);
        session.SaveChanges();

        // no where clause
        session.Query<User>()
            .Where(x => x.CreatedSince(epoch))
            .OrderBy(x => x.UserName)
            .Select(x => x.UserName)
            .ToList()
            .ShouldHaveTheSameElementsAs("baz", "jack");

        // with a where clause
        session.Query<User>().Where(x => x.UserName != "baz" && x.CreatedSince(epoch))
            .OrderBy(x => x.UserName)
            .ToList()
            .Select(x => x.UserName)
            .Single()
            .ShouldBe("jack");
    }

    public created_timestamp_queries(DefaultStoreFixture fixture) : base(fixture)
    {
        fixture.Store.Options.Policies.ForAllDocuments(o => o.Metadata.CreatedTimestamp.Enabled = true);
    }
}
