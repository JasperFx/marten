using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq.LastModified;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.Tables;
using Xunit;

namespace DocumentDbTests.Metadata;

public class last_modified_queries: IntegrationContext
{
    [Fact]
    public void creates_btree_index_for_mt_last_modified()
    {
        var mapping = DocumentMapping.For<Customer>();
        var indexDefinition = mapping.Indexes.Cast<DocumentIndex>().Single(x => x.Columns.First() == SchemaConstants.LastModifiedColumn);

        indexDefinition.Method.ShouldBe(IndexMethod.btree);
    }

    #region sample_index-last-modified-via-attribute
    [IndexedLastModified]
    public class Customer
    {
    }
    #endregion


    #region sample_last_modified_queries

    public async Task sample_usage(IQuerySession session)
    {
        var fiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);
        var tenMinutesAgo = DateTime.UtcNow.AddMinutes(-10);

        // Query for documents modified between 5 and 10 minutes ago
        var recents = await session.Query<Target>()
            .Where(x => x.ModifiedSince(tenMinutesAgo))
            .Where(x => x.ModifiedBefore(fiveMinutesAgo))
            .ToListAsync();
    }

    #endregion


    [Fact]
    public void query_modified_since_docs()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        session.SaveChanges();

        var epoch = session.MetadataFor(user4).LastModified;
        session.Store(user3, user4);
        session.SaveChanges();

        // no where clause
        session.Query<User>().Where(x => x.ModifiedSince(epoch))
            .OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToList().ShouldHaveTheSameElementsAs("baz", "jack");

        // with a where clause
        session.Query<User>().Where(x => x.UserName != "baz" && x.ModifiedSince(epoch))
            .OrderBy(x => x.UserName)
            .ToList()
            .Select(x => x.UserName)
            .Single().ShouldBe("jack");
    }

    [Fact]
    public void query_modified_before_docs()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        session.SaveChanges();

        session.Store(user3, user4);
        session.SaveChanges();

        var epoch = session.MetadataFor(user4).LastModified;

        // no where clause
        session.Query<User>().Where(x => x.ModifiedBefore(epoch)).OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToList().ShouldHaveTheSameElementsAs("bar", "foo");

        // with a where clause
        session.Query<User>().Where(x => x.UserName != "bar" && x.ModifiedBefore(epoch))
            .OrderBy(x => x.UserName)
            .ToList()
            .Select(x => x.UserName)
            .Single().ShouldBe("foo");
    }

    public last_modified_queries(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
