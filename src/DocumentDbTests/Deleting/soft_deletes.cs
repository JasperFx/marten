using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Linq.SoftDeletes;
using Marten.Metadata;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables.Partitioning;
using Xunit;

namespace DocumentDbTests.Deleting;

public class SoftDeletedFixture: StoreFixture
{
    public SoftDeletedFixture() : base("softdelete")
    {
        Options.Schema.For<User>().SoftDeletedWithIndex();
        Options.Schema.For<File>().SoftDeleted()
            .Metadata(m => m.IsSoftDeleted.MapTo(x => x.Deleted));


        Options.Schema.For<User>()
            .SoftDeleted()
            .AddSubClass<AdminUser>()
            .AddSubClass<SuperUser>();

        Options.Schema.For<IntDoc>().SoftDeleted().MultiTenanted();
        Options.Schema.For<LongDoc>().SoftDeleted().MultiTenanted();
        Options.Schema.For<StringDoc>().SoftDeleted().MultiTenanted();
        Options.Schema.For<GuidDoc>().SoftDeleted().MultiTenanted();

    }
}

public class SoftDeletedDocument: ISoftDeleted
{
    public string Id { get; set; }
    public bool Deleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public class soft_deletes: StoreContext<SoftDeletedFixture>, IClassFixture<SoftDeletedFixture>, IAsyncLifetime
{
    public soft_deletes(SoftDeletedFixture fixture): base(fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
    }

    public override ValueTask DisposeAsync()
    {
        return base.DisposeAsync();
    }


    [Fact]
    public async Task can_query_by_the_deleted_column_if_it_exists()
    {
        var doc1 = new SoftDeletedDocument{Id = "1"};
        var doc2 = new SoftDeletedDocument{Id = "2"};
        var doc3 = new SoftDeletedDocument{Id = "3"};
        var doc4 = new SoftDeletedDocument{Id = "4"};
        var doc5 = new SoftDeletedDocument{Id = "5"};

        theSession.Store(doc1, doc2, doc3, doc4, doc5);
        await theSession.SaveChangesAsync();

        await using var session2 = theStore.LightweightSession();

        session2.Delete(doc1);
        session2.Delete(doc3);
        await session2.SaveChangesAsync();

        await using var query = theStore.QuerySession();


        var deleted = await query.Query<SoftDeletedDocument>().Where(x => x.Deleted)
            .CountAsync();

        var notDeleted = await query.Query<SoftDeletedDocument>().Where(x => !x.Deleted)
            .CountAsync();

        deleted.ShouldBe(2);
        notDeleted.ShouldBe(3);
    }

    [Fact]
    public async Task initial_state_of_deleted_columns()
    {
        using var session = theStore.LightweightSession();
        var user = new User();
        session.Store(user);
        await session.SaveChangesAsync();

        userIsNotMarkedAsDeleted(session, user.Id);
    }

    private static void userIsNotMarkedAsDeleted(IDocumentSession session, Guid userId)
    {
        var cmd = session.Connection.CreateCommand("select mt_deleted, mt_deleted_at from softdelete.mt_doc_user where id = :id")
            .With("id", userId);

        using var reader = cmd.ExecuteReader();
        reader.Read();

        reader.GetFieldValue<bool>(0).ShouldBeFalse();
        reader.IsDBNull(1).ShouldBeTrue();
    }

    private void assertDocumentIsHardDeleted<T>(IDocumentSession session, object id)
    {
        var mapping = theStore.Options.Storage.MappingFor(typeof(T));


        var cmd = session.Connection.CreateCommand($"select count(*) from {mapping.TableName} where id = :id")
            .With("id", id);

        var count = (long)cmd.ExecuteScalar();
        count.ShouldBe(0);
    }

    private void plantDeletedAt<T>(IDocumentSession session, object id, DateTimeOffset instant)
    {
        var mapping = theStore.Options.Storage.MappingFor(typeof(T));

        var cmd = session.Connection
            .CreateCommand($"update {mapping.TableName} set mt_deleted_at = :at where id = :id")
            .With("at", instant.UtcDateTime)
            .With("id", id);

        cmd.ExecuteNonQuery();
    }

    private DateTimeOffset? readDeletedAt<T>(IDocumentSession session, object id)
    {
        var mapping = theStore.Options.Storage.MappingFor(typeof(T));

        var cmd = session.Connection
            .CreateCommand($"select mt_deleted_at from {mapping.TableName} where id = :id")
            .With("id", id);

        var raw = cmd.ExecuteScalar();
        if (raw == null || raw == DBNull.Value)
        {
            return null;
        }

        // Npgsql surfaces timestamptz as a UTC DateTime, not a DateTimeOffset.
        return raw is DateTimeOffset offset ? offset : new DateTimeOffset((DateTime)raw, TimeSpan.Zero);
    }

    /// <summary>
    ///     Ports polecat#419. A soft <c>DeleteWhere</c> must not re-stamp <c>mt_deleted_at</c> on rows
    ///     that were already deleted, or every question about *when* a document was deleted
    ///     (DeletedSince, DeletedBefore, an audit, a retention sweep) silently reads the later bulk
    ///     call instead of the deletion, and the real value is unrecoverable.
    ///     <para>
    ///     Two deletes in the same millisecond agree either way, so calling DeleteWhere twice proves
    ///     nothing on its own. This plants a known instant far in the past on the already-deleted row
    ///     and asserts the second call leaves it alone.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task delete_where_does_not_restamp_deleted_at_on_already_deleted_rows()
    {
        var planted = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var alreadyDeleted = new StringDoc { Id = "restamp-old", Size = "big" };
        var stillLive = new StringDoc { Id = "restamp-new", Size = "big" };

        theSession.Store(alreadyDeleted, stillLive);
        await theSession.SaveChangesAsync();

        theSession.Delete(alreadyDeleted);
        await theSession.SaveChangesAsync();

        // Plant a deletion instant well in the past, so a re-stamp is unmistakable.
        plantDeletedAt<StringDoc>(theSession, alreadyDeleted.Id, planted);

        // A later bulk delete whose predicate still matches the already-deleted row.
        theSession.DeleteWhere<StringDoc>(x => x.Size == "big");
        await theSession.SaveChangesAsync();

        // The already-deleted row keeps the instant it was actually deleted.
        var untouched = readDeletedAt<StringDoc>(theSession, alreadyDeleted.Id);
        untouched.ShouldNotBeNull();
        untouched.Value.ToUniversalTime().ShouldBe(planted.ToUniversalTime());

        // ... and the row that was still live is deleted now, with a real (recent) timestamp.
        var freshlyDeleted = readDeletedAt<StringDoc>(theSession, stillLive.Id);
        freshlyDeleted.ShouldNotBeNull();
        freshlyDeleted.Value.ShouldBeGreaterThan(planted);
    }

    /// <summary>
    ///     Ports polecat#419, the other half. <c>HardDeleteWhere</c> must NOT inherit the soft-delete
    ///     exclusion filter: a hard delete of an already-soft-deleted row is a real delete and has to
    ///     proceed, or soft-deleted rows are stranded permanently beyond the reach of the purge that
    ///     is supposed to remove them.
    /// </summary>
    [Fact]
    public async Task hard_delete_where_still_removes_already_soft_deleted_rows()
    {
        var doc = new StringDoc { Id = "purge-me", Size = "big" };

        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        theSession.Delete(doc);
        await theSession.SaveChangesAsync();

        theSession.HardDeleteWhere<StringDoc>(x => x.Size == "big");
        await theSession.SaveChangesAsync();

        assertDocumentIsHardDeleted<StringDoc>(theSession, doc.Id);
    }

    [Fact]
    public async Task soft_delete_a_document_row_state()
    {
        using var session = theStore.LightweightSession();
        var user = new User();
        session.Store(user);
        await session.SaveChangesAsync();

        session.Delete(user);
        await session.SaveChangesAsync();

        userIsMarkedAsDeleted(session, user.Id);
    }

    [Fact]
    public async Task hard_delete_a_document_row_state()
    {
        using var session = theStore.LightweightSession();
        var user = new User();
        session.Store(user);
        await session.SaveChangesAsync();

        session.HardDelete(user);
        await session.SaveChangesAsync();

        assertDocumentIsHardDeleted<User>(session, user.Id);
    }

    [Fact]
    public async Task hard_delete_by_linq()
    {
        var doc1 = new StringDoc {Id = "red", Size = "big"};
        var doc2 = new StringDoc {Id = "blue", Size = "small"};
        var doc3 = new StringDoc {Id = "green", Size = "big"};
        var doc4 = new StringDoc {Id = "purple", Size = "medium"};

        theSession.Store(doc1, doc2, doc3, doc4);
        await theSession.SaveChangesAsync();

        theSession.HardDeleteWhere<StringDoc>(x => x.Size == "big");
        await theSession.SaveChangesAsync();

        assertDocumentIsHardDeleted<StringDoc>(theSession,"red");
        assertDocumentIsHardDeleted<StringDoc>(theSession, "green");

        var count = await theSession.Query<StringDoc>().Where(x => x.MaybeDeleted()).CountAsync();
        count.ShouldBe(2);
    }

    [Fact]
    public async Task hard_delete_a_document_row_state_int()
    {
        using var session = theStore.LightweightSession();
        var doc = new IntDoc();
        session.Store(doc);
        await session.SaveChangesAsync();

        session.HardDelete<IntDoc>(doc.Id);
        await session.SaveChangesAsync();

        assertDocumentIsHardDeleted<IntDoc>(session, doc.Id);
    }

    [Fact]
    public async Task hard_delete_a_document_row_state_long()
    {
        using var session = theStore.LightweightSession();
        var doc = new LongDoc();
        session.Store(doc);
        await session.SaveChangesAsync();

        session.HardDelete<LongDoc>(doc.Id);
        await session.SaveChangesAsync();

        assertDocumentIsHardDeleted<LongDoc>(session, doc.Id);
    }

    [Fact]
    public async Task hard_delete_a_document_row_state_string()
    {
        using var session = theStore.LightweightSession();
        var doc = new StringDoc{Id = Guid.NewGuid().ToString()};
        session.Store(doc);
        await session.SaveChangesAsync();

        session.HardDelete<StringDoc>(doc.Id);
        await session.SaveChangesAsync();

        assertDocumentIsHardDeleted<StringDoc>(session, doc.Id);
    }

    private static void userIsMarkedAsDeleted(IDocumentSession session, Guid userId)
    {
        var cmd = session.Connection.CreateCommand()
            .Sql("select mt_deleted, mt_deleted_at from softdelete.mt_doc_user where id = :id")
            .With("id", userId);

        using var reader = cmd.ExecuteReader();
        reader.Read();

        reader.GetFieldValue<bool>(0).ShouldBeTrue();
        reader.IsDBNull(1).ShouldBeFalse();
    }

    [Fact]
    public async Task soft_delete_a_document_by_where_row_state()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3);
        await session.SaveChangesAsync();

        session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        userIsNotMarkedAsDeleted(session, user1.Id);
        userIsMarkedAsDeleted(session, user2.Id);
        userIsMarkedAsDeleted(session, user3.Id);
    }

    [Fact]
    public async Task un_delete_a_document_by_where_row_state()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3);
        await session.SaveChangesAsync();

        session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        userIsNotMarkedAsDeleted(session, user1.Id);
        userIsMarkedAsDeleted(session, user2.Id);
        userIsMarkedAsDeleted(session, user3.Id);

        session.UndoDeleteWhere<User>(x => x.UserName == "bar");
        await session.SaveChangesAsync();

        userIsNotMarkedAsDeleted(session, user1.Id);
        userIsNotMarkedAsDeleted(session, user2.Id);
        userIsMarkedAsDeleted(session, user3.Id);
    }

    #region sample_query_soft_deleted_docs
    [Fact]
    public async Task query_soft_deleted_docs()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        await session.SaveChangesAsync();

        // Deleting 'bar' and 'baz'
        session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        // no where clause, deleted docs should be filtered out
        (await session.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("foo", "jack");

        // with a where clause
        (await session.Query<User>().Where(x => x.UserName != "jack")
            .ToListAsync()).Single().UserName.ShouldBe("foo");
    }

    #endregion

    #region sample_query_maybe_soft_deleted_docs
    [Fact]
    public async Task query_maybe_soft_deleted_docs()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        await session.SaveChangesAsync();

        session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        // no where clause, all documents are returned
        (await session.Query<User>().Where(x => x.MaybeDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("bar", "baz", "foo", "jack");

        // with a where clause, all documents are returned
        (await session.Query<User>().Where(x => x.UserName != "jack" && x.MaybeDeleted())
            .OrderBy(x => x.UserName)
            .ToListAsync())
            .Select(x => x.UserName)
            .ShouldHaveTheSameElementsAs("bar", "baz", "foo");
    }

    #endregion

    #region sample_query_is_soft_deleted_docs
    [Fact]
    public async Task query_is_soft_deleted_docs()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        await session.SaveChangesAsync();

        session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        // no where clause
        (await session.Query<User>().Where(x => x.IsDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("bar", "baz");

        // with a where clause
        (await session.Query<User>().Where(x => x.UserName != "baz" && x.IsDeleted())
            .OrderBy(x => x.UserName)
            .ToListAsync())
            .Select(x => x.UserName)
            .Single().ShouldBe("bar");
    }

    #endregion

    #region sample_query_soft_deleted_since
    [Fact]
    public async Task query_is_soft_deleted_since_docs()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        await session.SaveChangesAsync();

        session.Delete(user3);
        await session.SaveChangesAsync();

        var epoch = (await session.MetadataForAsync(user3)).DeletedAt;
        session.Delete(user4);
        await session.SaveChangesAsync();

        (await session.Query<User>().Where(x => x.DeletedSince(epoch.Value)).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("jack");
    }

    #endregion

    [Fact]
    public async Task query_is_soft_deleted_before_docs()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        await session.SaveChangesAsync();

        session.Delete(user3);
        await session.SaveChangesAsync();

        session.Delete(user4);
        await session.SaveChangesAsync();

        var epoch = (await session.MetadataForAsync(user4)).DeletedAt;

        (await session.Query<User>().Where(x => x.DeletedBefore(epoch.Value)).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("baz");
    }

    [Fact]
    public async Task top_level_of_hierarchy()
    {


        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        await session.SaveChangesAsync();

        session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        // no where clause
        (await session.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("foo", "jack");

        // with a where clause
        (await session.Query<User>().Where(x => x.UserName != "jack")
            .ToListAsync()).Single().UserName.ShouldBe("foo");
    }

    [Fact]
    public async Task sub_level_of_hierarchy()
    {

        var user1 = new SuperUser { UserName = "foo" };
        var user2 = new SuperUser { UserName = "bar" };
        var user3 = new SuperUser { UserName = "baz" };
        var user4 = new SuperUser { UserName = "jack" };
        var user5 = new AdminUser { UserName = "admin" };

        using var session = theStore.LightweightSession();
        session.StoreObjects(new User[] { user1, user2, user3, user4, user5 });
        await session.SaveChangesAsync();

        session.DeleteWhere<SuperUser>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        // no where clause
        (await session.Query<SuperUser>().OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("foo", "jack");

        // with a where clause
        (await session.Query<SuperUser>().Where(x => x.UserName != "jack")
            .ToListAsync()).Single().UserName.ShouldBe("foo");
    }

    [Fact]
    public async Task sub_level_of_hierarchy_maybe_deleted()
    {

        var user1 = new SuperUser { UserName = "foo" };
        var user2 = new SuperUser { UserName = "bar" };
        var user3 = new SuperUser { UserName = "baz" };
        var user4 = new SuperUser { UserName = "jack" };
        var user5 = new AdminUser { UserName = "admin" };

        using var session = theStore.LightweightSession();
        session.StoreObjects(new User[] { user1, user2, user3, user4, user5 });
        await session.SaveChangesAsync();

        session.DeleteWhere<SuperUser>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        // no where clause
        (await session.Query<SuperUser>().Where(x => x.MaybeDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("bar", "baz", "foo", "jack");

        // with a where clause
        (await session.Query<SuperUser>().Where(x => x.UserName != "jack" && x.MaybeDeleted())
            .OrderBy(x => x.UserName)
            .Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("bar", "baz", "foo");
    }

    [Fact]
    public async Task sub_level_of_hierarchy_is_deleted()
    {

        var user1 = new SuperUser { UserName = "foo" };
        var user2 = new SuperUser { UserName = "bar" };
        var user3 = new SuperUser { UserName = "baz" };
        var user4 = new SuperUser { UserName = "jack" };
        var user5 = new AdminUser { UserName = "admin" };

        using var session = theStore.LightweightSession();
        session.StoreObjects(new User[] { user1, user2, user3, user4, user5 });
        await session.SaveChangesAsync();

        session.DeleteWhere<SuperUser>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        // no where clause
        (await session.Query<SuperUser>().Where(x => x.IsDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("bar", "baz");

        // with a where clause
        (await session.Query<SuperUser>().Where(x => x.UserName != "bar" && x.IsDeleted())
            .OrderBy(x => x.UserName)
            .Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("baz");
    }

    [Fact]
    public async Task soft_deleted_documents_work_with_linq_include()
    {
        using var session = theStore.LightweightSession();

        var user = new User();
        session.Store(user);
        var file1 = new File() { UserId = user.Id };
        session.Store(file1);
        var file2 = new File() { UserId = user.Id };
        session.Store(file2);
        await session.SaveChangesAsync();
        session.Delete(file2);
        await session.SaveChangesAsync();

        var users = new List<User>();
        var files = (await session.Query<File>().Include(u => u.UserId, users).ToListAsync());
        files.Count.ShouldBe(1);
        users.Count.ShouldBe(1);
        files = (await session.Query<File>().Where(f => f.MaybeDeleted()).Include(u => u.UserId, users).ToListAsync());
        files.Count.ShouldBe(2);
        files = (await session.Query<File>().Where(f => f.IsDeleted()).Include(u => u.UserId, users).ToListAsync());
        files.Count.ShouldBe(1);
    }

    [Fact]
    public async Task hard_delete_by_document_and_tenant_by_string()
    {
        var doc1 = new StringDoc{Id = "big"};

        theSession.ForTenant("read").Store(doc1);
        theSession.ForTenant("blue").Store(doc1);

        await theSession.SaveChangesAsync();

        theSession.ForTenant("red").HardDelete(doc1);

        await using var query = theStore.QuerySession();

        var redCount = await query.Query<StringDoc>().Where(x => x.TenantIsOneOf("red"))
            .CountAsync();

        redCount.ShouldBe(0);

        var blueCount = await query.Query<StringDoc>().Where(x => x.TenantIsOneOf("blue"))
            .CountAsync();

        blueCount.ShouldBe(1);
    }

    [Fact]
    public async Task hard_delete_by_document_and_tenant_by_int()
    {
        var doc1 = new IntDoc{Id = 5000};

        theSession.ForTenant("red").Store(doc1);
        theSession.ForTenant("blue").Store(doc1);

        await theSession.SaveChangesAsync();

        theSession.ForTenant("red").HardDelete(doc1);

        await using var query = theStore.QuerySession();

        var redCount = await query.Query<IntDoc>().Where(x => x.TenantIsOneOf("red"))
            .CountAsync();

        redCount.ShouldBe(0);

        var blueCount = await query.Query<IntDoc>().Where(x => x.TenantIsOneOf("blue"))
            .CountAsync();

        blueCount.ShouldBe(1);
    }

    [Fact]
    public async Task hard_delete_by_document_and_tenant_by_long()
    {
        var doc1 = new LongDoc{Id = 5000};

        theSession.ForTenant("red").Store(doc1);
        theSession.ForTenant("blue").Store(doc1);

        await theSession.SaveChangesAsync();

        theSession.ForTenant("red").HardDelete(doc1);

        await using var query = theStore.QuerySession();

        var redCount = await query.Query<LongDoc>().Where(x => x.TenantIsOneOf("red"))
            .CountAsync();

        redCount.ShouldBe(0);

        var blueCount = await query.Query<LongDoc>().Where(x => x.TenantIsOneOf("blue"))
            .CountAsync();

        blueCount.ShouldBe(1);
    }

    [Fact]
    public async Task hard_delete_by_document_and_tenant_by_Guid()
    {
        var doc1 = new GuidDoc{Id = Guid.NewGuid()};


        theSession.ForTenant("red").Store(doc1);
        theSession.ForTenant("blue").Store(doc1);

        await theSession.SaveChangesAsync();

        theSession.ForTenant("red").HardDelete(doc1);

        await using var query = theStore.QuerySession();

        var redCount = await query.Query<GuidDoc>().Where(x => x.TenantIsOneOf("red"))
            .CountAsync();

        redCount.ShouldBe(0);

        var blueCount = await query.Query<GuidDoc>().Where(x => x.TenantIsOneOf("blue"))
            .CountAsync();

        blueCount.ShouldBe(1);
    }

    [Fact]
    public async Task throw_not_supported_when_trying_to_query_against_non_soft_deleted_docs_1()
    {
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            var dateTimeOffset = DateTimeOffset.Now.Subtract(5.Days());
            await theSession.Query<Target>().Where(x => x.DeletedBefore(dateTimeOffset)).ToListAsync();
        });

        ex.Message.ShouldBe($"Document type {typeof(Target).FullNameInCode()} is not configured as soft deleted");
    }

    [Fact]
    public async Task throw_not_supported_when_trying_to_query_against_non_soft_deleted_docs_2()
    {
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            var dateTimeOffset = DateTimeOffset.Now.Subtract(5.Days());
            await theSession.Query<Target>().Where(x => x.DeletedSince(dateTimeOffset)).ToListAsync();
        });

        ex.Message.ShouldBe($"Document type {typeof(Target).FullNameInCode()} is not configured as soft deleted");
    }

    [Fact]
    public async Task throw_not_supported_when_trying_to_query_against_non_soft_deleted_docs_3()
    {
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await theSession.Query<Target>().Where(x => x.MaybeDeleted()).ToListAsync();
        });

        ex.Message.ShouldBe($"Document type {typeof(Target).FullNameInCode()} is not configured as soft deleted");
    }


    [Fact]
    public async Task throw_not_supported_when_trying_to_query_against_non_soft_deleted_docs_4()
    {
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await theSession.Query<Target>().Where(x => x.IsDeleted()).ToListAsync();
        });

        ex.Message.ShouldBe($"Document type {typeof(Target).FullNameInCode()} is not configured as soft deleted");
    }

    [Fact]
    public async Task should_partition_through_attribute()
    {
        await theStore.EnsureStorageExistsAsync(typeof(SoftDeletedAndPartitionedDocument));

        var table = new DocumentTable(theStore.Options.Storage.MappingFor(typeof(SoftDeletedAndPartitionedDocument)));

        var partitioning = table.Partitioning.ShouldBeOfType<ListPartitioning>();
        partitioning.Partitions.Single().ShouldBe(new ListPartition("deleted", "true"));

    }

    [Fact]
    public async Task delete_sets_ISoftDeleted_properties_on_in_memory_document()
    {
        var doc1 = new SoftDeletedDocument { Id = "50" };
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        await using var session2 = theStore.IdentitySession();
        var loadedDoc = await session2.LoadAsync<SoftDeletedDocument>(doc1.Id);
        loadedDoc.ShouldNotBeNull();

        session2.Delete(loadedDoc);
        await session2.SaveChangesAsync();

        loadedDoc.Deleted.ShouldBeTrue();
        loadedDoc.DeletedAt.ShouldNotBeNull();
    }
}

public class soft_deletes_with_partitioning: OneOffConfigurationsContext, IAsyncLifetime
{
    public soft_deletes_with_partitioning()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<User>().SoftDeletedWithPartitioningAndIndex();
            opts.Schema.For<File>().SoftDeletedWithPartitioning()
                .Metadata(m => m.IsSoftDeleted.MapTo(x => x.Deleted));


            opts.Schema.For<User>()
                .SoftDeletedWithPartitioning()
                .AddSubClass<AdminUser>()
                .AddSubClass<SuperUser>();

            opts.Schema.For<IntDoc>().SoftDeletedWithPartitioning().MultiTenanted();
            opts.Schema.For<LongDoc>().SoftDeletedWithPartitioning().MultiTenanted();
            opts.Schema.For<StringDoc>().SoftDeletedWithPartitioning().MultiTenanted();
            opts.Schema.For<GuidDoc>().SoftDeletedWithPartitioning().MultiTenanted();
        });


    }


    public override ValueTask InitializeAsync()
    {
        return new ValueTask(theStore.Advanced.Clean.DeleteAllDocumentsAsync());
    }

    public override ValueTask DisposeAsync()
    {
        return base.DisposeAsync();
    }


    [Fact]
    public async Task can_query_by_the_deleted_column_if_it_exists()
    {
        var doc1 = new SoftDeletedDocument{Id = "1"};
        var doc2 = new SoftDeletedDocument{Id = "2"};
        var doc3 = new SoftDeletedDocument{Id = "3"};
        var doc4 = new SoftDeletedDocument{Id = "4"};
        var doc5 = new SoftDeletedDocument{Id = "5"};

        theSession.Store(doc1, doc2, doc3, doc4, doc5);
        await theSession.SaveChangesAsync();

        await using var session2 = theStore.LightweightSession();

        session2.Delete(doc1);
        session2.Delete(doc3);
        await session2.SaveChangesAsync();

        await using var query = theStore.QuerySession();


        var deleted = await query.Query<SoftDeletedDocument>().Where(x => x.Deleted)
            .CountAsync();

        var notDeleted = await query.Query<SoftDeletedDocument>().Where(x => !x.Deleted)
            .CountAsync();

        deleted.ShouldBe(2);
        notDeleted.ShouldBe(3);
    }

    [Fact]
    public async Task initial_state_of_deleted_columns()
    {
        using var session = theStore.LightweightSession();
        var user = new User();
        session.Store(user);
        await session.SaveChangesAsync();

        userIsNotMarkedAsDeleted(session, user.Id);
    }

    private void userIsNotMarkedAsDeleted(IDocumentSession session, Guid userId)
    {
        var cmd = session.Connection.CreateCommand($"select mt_deleted, mt_deleted_at from {SchemaName}.mt_doc_user where id = :id")
            .With("id", userId);

        using var reader = cmd.ExecuteReader();
        reader.Read();

        reader.GetFieldValue<bool>(0).ShouldBeFalse();
        reader.IsDBNull(1).ShouldBeTrue();
    }

    private void assertDocumentIsHardDeleted<T>(IDocumentSession session, object id)
    {
        var mapping = theStore.Options.Storage.MappingFor(typeof(T));


        var cmd = session.Connection.CreateCommand($"select count(*) from {mapping.TableName} where id = :id")
            .With("id", id);

        var count = (long)cmd.ExecuteScalar();
        count.ShouldBe(0);
    }

    [Fact]
    public async Task soft_delete_a_document_row_state()
    {
        using var session = theStore.LightweightSession();
        var user = new User();
        session.Store(user);
        await session.SaveChangesAsync();

        session.Delete(user);
        await session.SaveChangesAsync();

        userIsMarkedAsDeleted(session, user.Id);
    }

    [Fact]
    public async Task hard_delete_a_document_row_state()
    {
        using var session = theStore.LightweightSession();
        var user = new User();
        session.Store(user);
        await session.SaveChangesAsync();

        session.HardDelete(user);
        await session.SaveChangesAsync();

        assertDocumentIsHardDeleted<User>(session, user.Id);
    }

    [Fact]
    public async Task hard_delete_by_linq()
    {
        var doc1 = new StringDoc {Id = "red", Size = "big"};
        var doc2 = new StringDoc {Id = "blue", Size = "small"};
        var doc3 = new StringDoc {Id = "green", Size = "big"};
        var doc4 = new StringDoc {Id = "purple", Size = "medium"};

        theSession.Store(doc1, doc2, doc3, doc4);
        await theSession.SaveChangesAsync();

        theSession.HardDeleteWhere<StringDoc>(x => x.Size == "big");
        await theSession.SaveChangesAsync();

        assertDocumentIsHardDeleted<StringDoc>(theSession,"red");
        assertDocumentIsHardDeleted<StringDoc>(theSession, "green");

        var count = await theSession.Query<StringDoc>().Where(x => x.MaybeDeleted()).CountAsync();
        count.ShouldBe(2);
    }

    [Fact]
    public async Task hard_delete_a_document_row_state_int()
    {
        using var session = theStore.LightweightSession();
        var doc = new IntDoc();
        session.Store(doc);
        await session.SaveChangesAsync();

        session.HardDelete<IntDoc>(doc.Id);
        await session.SaveChangesAsync();

        assertDocumentIsHardDeleted<IntDoc>(session, doc.Id);
    }

    [Fact]
    public async Task hard_delete_a_document_row_state_long()
    {
        using var session = theStore.LightweightSession();
        var doc = new LongDoc();
        session.Store(doc);
        await session.SaveChangesAsync();

        session.HardDelete<LongDoc>(doc.Id);
        await session.SaveChangesAsync();

        assertDocumentIsHardDeleted<LongDoc>(session, doc.Id);
    }

    [Fact]
    public async Task hard_delete_a_document_row_state_string()
    {
        using var session = theStore.LightweightSession();
        var doc = new StringDoc{Id = Guid.NewGuid().ToString()};
        session.Store(doc);
        await session.SaveChangesAsync();

        session.HardDelete<StringDoc>(doc.Id);
        await session.SaveChangesAsync();

        assertDocumentIsHardDeleted<StringDoc>(session, doc.Id);
    }

    private void userIsMarkedAsDeleted(IDocumentSession session, Guid userId)
    {
        var cmd = session.Connection.CreateCommand()
            .Sql($"select mt_deleted, mt_deleted_at from {SchemaName}.mt_doc_user where id = :id")
            .With("id", userId);

        using var reader = cmd.ExecuteReader();
        reader.Read();

        reader.GetFieldValue<bool>(0).ShouldBeTrue();
        reader.IsDBNull(1).ShouldBeFalse();
    }

    [Fact]
    public async Task soft_delete_a_document_by_where_row_state()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3);
        await session.SaveChangesAsync();

        session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        userIsNotMarkedAsDeleted(session, user1.Id);
        userIsMarkedAsDeleted(session, user2.Id);
        userIsMarkedAsDeleted(session, user3.Id);
    }

    [Fact]
    public async Task un_delete_a_document_by_where_row_state()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3);
        await session.SaveChangesAsync();

        session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        userIsNotMarkedAsDeleted(session, user1.Id);
        userIsMarkedAsDeleted(session, user2.Id);
        userIsMarkedAsDeleted(session, user3.Id);

        session.UndoDeleteWhere<User>(x => x.UserName == "bar");
        await session.SaveChangesAsync();

        userIsNotMarkedAsDeleted(session, user1.Id);
        userIsNotMarkedAsDeleted(session, user2.Id);
        userIsMarkedAsDeleted(session, user3.Id);
    }

    #region sample_query_soft_deleted_docs
    [Fact]
    public async Task query_soft_deleted_docs()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        await session.SaveChangesAsync();

        // Deleting 'bar' and 'baz'
        session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        // no where clause, deleted docs should be filtered out
        (await session.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("foo", "jack");

        // with a where clause
        (await session.Query<User>().Where(x => x.UserName != "jack")
            .ToListAsync()).Single().UserName.ShouldBe("foo");
    }

    #endregion

    #region sample_query_maybe_soft_deleted_docs
    [Fact]
    public async Task query_maybe_soft_deleted_docs()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        await session.SaveChangesAsync();

        session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        // no where clause, all documents are returned
        (await session.Query<User>().Where(x => x.MaybeDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("bar", "baz", "foo", "jack");

        // with a where clause, all documents are returned
        (await session.Query<User>().Where(x => x.UserName != "jack" && x.MaybeDeleted())
            .OrderBy(x => x.UserName)
            .ToListAsync())
            .Select(x => x.UserName)
            .ShouldHaveTheSameElementsAs("bar", "baz", "foo");
    }

    #endregion

    #region sample_query_is_soft_deleted_docs
    [Fact]
    public async Task query_is_soft_deleted_docs()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        await session.SaveChangesAsync();

        session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        // no where clause
        (await session.Query<User>().Where(x => x.IsDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("bar", "baz");

        // with a where clause
        (await session.Query<User>().Where(x => x.UserName != "baz" && x.IsDeleted())
            .OrderBy(x => x.UserName)
            .ToListAsync())
            .Select(x => x.UserName)
            .Single().ShouldBe("bar");
    }

    #endregion

    #region sample_query_soft_deleted_since
    [Fact]
    public async Task query_is_soft_deleted_since_docs()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        await session.SaveChangesAsync();

        session.Delete(user3);
        await session.SaveChangesAsync();

        var epoch = (await session.MetadataForAsync(user3)).DeletedAt;
        session.Delete(user4);
        await session.SaveChangesAsync();

        (await session.Query<User>().Where(x => x.DeletedSince(epoch.Value)).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("jack");
    }

    #endregion

    [Fact]
    public async Task query_is_soft_deleted_before_docs()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        await session.SaveChangesAsync();

        session.Delete(user3);
        await session.SaveChangesAsync();

        session.Delete(user4);
        await session.SaveChangesAsync();

        var epoch = (await session.MetadataForAsync(user4)).DeletedAt;

        (await session.Query<User>().Where(x => x.DeletedBefore(epoch.Value)).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("baz");
    }

    [Fact]
    public async Task top_level_of_hierarchy()
    {


        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        await session.SaveChangesAsync();

        session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        // no where clause
        (await session.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("foo", "jack");

        // with a where clause
        (await session.Query<User>().Where(x => x.UserName != "jack")
            .ToListAsync()).Single().UserName.ShouldBe("foo");
    }

    [Fact]
    public async Task sub_level_of_hierarchy()
    {

        var user1 = new SuperUser { UserName = "foo" };
        var user2 = new SuperUser { UserName = "bar" };
        var user3 = new SuperUser { UserName = "baz" };
        var user4 = new SuperUser { UserName = "jack" };
        var user5 = new AdminUser { UserName = "admin" };

        using var session = theStore.LightweightSession();
        session.StoreObjects(new User[] { user1, user2, user3, user4, user5 });
        await session.SaveChangesAsync();

        session.DeleteWhere<SuperUser>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        // no where clause
        (await session.Query<SuperUser>().OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("foo", "jack");

        // with a where clause
        (await session.Query<SuperUser>().Where(x => x.UserName != "jack")
            .ToListAsync()).Single().UserName.ShouldBe("foo");
    }

    [Fact]
    public async Task sub_level_of_hierarchy_maybe_deleted()
    {

        var user1 = new SuperUser { UserName = "foo" };
        var user2 = new SuperUser { UserName = "bar" };
        var user3 = new SuperUser { UserName = "baz" };
        var user4 = new SuperUser { UserName = "jack" };
        var user5 = new AdminUser { UserName = "admin" };

        using var session = theStore.LightweightSession();
        session.StoreObjects(new User[] { user1, user2, user3, user4, user5 });
        await session.SaveChangesAsync();

        session.DeleteWhere<SuperUser>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        // no where clause
        (await session.Query<SuperUser>().Where(x => x.MaybeDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("bar", "baz", "foo", "jack");

        // with a where clause
        (await session.Query<SuperUser>().Where(x => x.UserName != "jack" && x.MaybeDeleted())
            .OrderBy(x => x.UserName)
            .Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("bar", "baz", "foo");
    }

    [Fact]
    public async Task sub_level_of_hierarchy_is_deleted()
    {

        var user1 = new SuperUser { UserName = "foo" };
        var user2 = new SuperUser { UserName = "bar" };
        var user3 = new SuperUser { UserName = "baz" };
        var user4 = new SuperUser { UserName = "jack" };
        var user5 = new AdminUser { UserName = "admin" };

        using var session = theStore.LightweightSession();
        session.StoreObjects(new User[] { user1, user2, user3, user4, user5 });
        await session.SaveChangesAsync();

        session.DeleteWhere<SuperUser>(x => x.UserName.StartsWith("b"));
        await session.SaveChangesAsync();

        // no where clause
        (await session.Query<SuperUser>().Where(x => x.IsDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("bar", "baz");

        // with a where clause
        (await session.Query<SuperUser>().Where(x => x.UserName != "bar" && x.IsDeleted())
            .OrderBy(x => x.UserName)
            .Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("baz");
    }

    [Fact]
    public async Task soft_deleted_documents_work_with_linq_include()
    {
        using var session = theStore.LightweightSession();

        var user = new User();
        session.Store(user);
        var file1 = new File() { UserId = user.Id };
        session.Store(file1);
        var file2 = new File() { UserId = user.Id };
        session.Store(file2);
        await session.SaveChangesAsync();
        session.Delete(file2);
        await session.SaveChangesAsync();

        var users = new List<User>();
        var files = (await session.Query<File>().Include(u => u.UserId, users).ToListAsync());
        files.Count.ShouldBe(1);
        users.Count.ShouldBe(1);
        files = (await session.Query<File>().Where(f => f.MaybeDeleted()).Include(u => u.UserId, users).ToListAsync());
        files.Count.ShouldBe(2);
        files = (await session.Query<File>().Where(f => f.IsDeleted()).Include(u => u.UserId, users).ToListAsync());
        files.Count.ShouldBe(1);
    }

    [Fact]
    public async Task hard_delete_by_document_and_tenant_by_string()
    {
        var doc1 = new StringDoc{Id = "big"};

        theSession.ForTenant("read").Store(doc1);
        theSession.ForTenant("blue").Store(doc1);

        await theSession.SaveChangesAsync();

        theSession.ForTenant("red").HardDelete(doc1);

        await using var query = theStore.QuerySession();

        var redCount = await query.Query<StringDoc>().Where(x => x.TenantIsOneOf("red"))
            .CountAsync();

        redCount.ShouldBe(0);

        var blueCount = await query.Query<StringDoc>().Where(x => x.TenantIsOneOf("blue"))
            .CountAsync();

        blueCount.ShouldBe(1);
    }

    [Fact]
    public async Task hard_delete_by_document_and_tenant_by_int()
    {
        var doc1 = new IntDoc{Id = 5000};

        theSession.ForTenant("red").Store(doc1);
        theSession.ForTenant("blue").Store(doc1);

        await theSession.SaveChangesAsync();

        theSession.ForTenant("red").HardDelete(doc1);

        await using var query = theStore.QuerySession();

        var redCount = await query.Query<IntDoc>().Where(x => x.TenantIsOneOf("red"))
            .CountAsync();

        redCount.ShouldBe(0);

        var blueCount = await query.Query<IntDoc>().Where(x => x.TenantIsOneOf("blue"))
            .CountAsync();

        blueCount.ShouldBe(1);
    }

    [Fact]
    public async Task hard_delete_by_document_and_tenant_by_long()
    {
        var doc1 = new LongDoc{Id = 5000};

        theSession.ForTenant("red").Store(doc1);
        theSession.ForTenant("blue").Store(doc1);

        await theSession.SaveChangesAsync();

        theSession.ForTenant("red").HardDelete(doc1);

        await using var query = theStore.QuerySession();

        var redCount = await query.Query<LongDoc>().Where(x => x.TenantIsOneOf("red"))
            .CountAsync();

        redCount.ShouldBe(0);

        var blueCount = await query.Query<LongDoc>().Where(x => x.TenantIsOneOf("blue"))
            .CountAsync();

        blueCount.ShouldBe(1);
    }

    [Fact]
    public async Task hard_delete_by_document_and_tenant_by_Guid()
    {
        var doc1 = new GuidDoc{Id = Guid.NewGuid()};


        theSession.ForTenant("red").Store(doc1);
        theSession.ForTenant("blue").Store(doc1);

        await theSession.SaveChangesAsync();

        theSession.ForTenant("red").HardDelete(doc1);

        await using var query = theStore.QuerySession();

        var redCount = await query.Query<GuidDoc>().Where(x => x.TenantIsOneOf("red"))
            .CountAsync();

        redCount.ShouldBe(0);

        var blueCount = await query.Query<GuidDoc>().Where(x => x.TenantIsOneOf("blue"))
            .CountAsync();

        blueCount.ShouldBe(1);
    }

    [Fact]
    public async Task throw_not_supported_when_trying_to_query_against_non_soft_deleted_docs_1()
    {
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            var dateTimeOffset = DateTimeOffset.Now.Subtract(5.Days());
            await theSession.Query<Target>().Where(x => x.DeletedBefore(dateTimeOffset)).ToListAsync();
        });

        ex.Message.ShouldBe($"Document type {typeof(Target).FullNameInCode()} is not configured as soft deleted");
    }

    [Fact]
    public async Task throw_not_supported_when_trying_to_query_against_non_soft_deleted_docs_2()
    {
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            var dateTimeOffset = DateTimeOffset.Now.Subtract(5.Days());
            await theSession.Query<Target>().Where(x => x.DeletedSince(dateTimeOffset)).ToListAsync();
        });

        ex.Message.ShouldBe($"Document type {typeof(Target).FullNameInCode()} is not configured as soft deleted");
    }

    [Fact]
    public async Task throw_not_supported_when_trying_to_query_against_non_soft_deleted_docs_3()
    {
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await theSession.Query<Target>().Where(x => x.MaybeDeleted()).ToListAsync();
        });

        ex.Message.ShouldBe($"Document type {typeof(Target).FullNameInCode()} is not configured as soft deleted");
    }


    [Fact]
    public async Task throw_not_supported_when_trying_to_query_against_non_soft_deleted_docs_4()
    {
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await theSession.Query<Target>().Where(x => x.IsDeleted()).ToListAsync();
        });

        ex.Message.ShouldBe($"Document type {typeof(Target).FullNameInCode()} is not configured as soft deleted");
    }

    [Fact]
    public async Task should_partition_through_attribute()
    {
        await theStore.EnsureStorageExistsAsync(typeof(SoftDeletedAndPartitionedDocument));

        var table = new DocumentTable(theStore.Options.Storage.MappingFor(typeof(SoftDeletedAndPartitionedDocument)));

        var partitioning = table.Partitioning.ShouldBeOfType<ListPartitioning>();
        partitioning.Partitions.Single().ShouldBe(new ListPartition("deleted", "true"));

    }

    public static void sample_configuration()
    {
        #region sample_soft_deletes_with_partitioning

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Opt into partitioning for one document type
            opts.Schema.For<User>().SoftDeletedWithPartitioning();

            // Opt into partitioning and an index on deletion timestamp for one document type
            opts.Schema.For<User>().SoftDeletedWithPartitioningAndIndex();

            // Opt into partitioning for all soft-deleted documents
            opts.Policies.AllDocumentsSoftDeletedWithPartitioning();
        });

        #endregion
    }
}



public class File
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Path { get; set; }
    public bool Deleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

#region sample_soft_deleted_attribute_with_partitioning

[SoftDeleted(UsePartitioning = true)]
public class SoftDeletedAndPartitionedDocument
{
    public Guid Id { get; set; }
}

#endregion
