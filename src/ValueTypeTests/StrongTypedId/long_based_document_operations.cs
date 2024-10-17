using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using StronglyTypedIds;
using Vogen;

namespace ValueTypeTests.StrongTypedId;

public class long_based_document_operations : IAsyncLifetime
{
    private readonly DocumentStore theStore;

    public long_based_document_operations()
    {
        theStore = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "strong_typed";
        });

        theSession = theStore.LightweightSession();
    }

    public async Task InitializeAsync()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Issue2));
    }

    public async Task DisposeAsync()
    {
        await theStore.DisposeAsync();
        theSession?.Dispose();
    }

    private IDocumentSession theSession;

    [Fact]
    public void store_document_will_assign_the_identity()
    {
        var issue = new Issue2();
        theSession.Store(issue);

        issue.Id.ShouldNotBeNull();
        ShouldBeTestExtensions.ShouldNotBe<long>(issue.Id.Value.Value, 0);
    }

    [Fact]
    public async Task store_a_document_smoke_test()
    {
        var issue = new Issue2();
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Issue2>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task insert_a_document_smoke_test()
    {
        var issue = new Issue2();
        theSession.Insert(issue);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Issue2>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task update_a_document_smoke_test()
    {
        var issue = new Issue2();
        theSession.Insert(issue);
        await theSession.SaveChangesAsync();

        issue.Name = "updated";
        await theSession.SaveChangesAsync();

        var loaded = await theSession.LoadAsync<Issue2>(issue.Id);
        loaded.Name.ShouldBeNull("updated");
    }

    [Fact]
    public async Task use_within_identity_map()
    {
        var issue = new Issue2();
        theSession.Insert(issue);
        await theSession.SaveChangesAsync();

        await using var session = theStore.IdentitySession();
        var loaded1 = await session.LoadAsync<Issue2>(issue.Id);
        var loaded2 = await session.LoadAsync<Issue2>(issue.Id);

        loaded1.ShouldBeSameAs(loaded2);
    }

    [Fact]
    public async Task usage_within_dirty_checking()
    {
        var issue = new Issue2();
        theSession.Insert(issue);
        await theSession.SaveChangesAsync();

        await using var session = theStore.DirtyTrackedSession();
        var loaded1 = await session.LoadAsync<Issue2>(issue.Id);
        loaded1.Name = "something else";

        await session.SaveChangesAsync();

        var loaded2 = await theSession.LoadAsync<Issue2>(issue.Id);
        loaded2.Name.ShouldBe(loaded1.Name);
    }

    [Fact]
    public async Task load_document()
    {
        var issue = new Issue2{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Issue2>(issue.Id))
            .Name.ShouldBe(issue.Name);
    }

    #region sample_strong_typed_identifier_and_is_one_of

    [Fact]
    public async Task load_many()
    {
        var issue1 = new Issue2{Name = Guid.NewGuid().ToString()};
        var issue2 = new Issue2{Name = Guid.NewGuid().ToString()};
        var issue3 = new Issue2{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue1, issue2, issue3);

        await theSession.SaveChangesAsync();

        var results = await theSession.Query<Issue2>()
            .Where(x => x.Id.IsOneOf(issue1.Id, issue2.Id, issue3.Id))
            .ToListAsync();

        results.Count.ShouldBe(3);
    }

    #endregion

    [Fact]
    public async Task delete_by_id()
    {
        var issue = new Issue2{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        theSession.Delete<Issue2>(issue.Id);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Issue2>(issue.Id))
            .ShouldBeNull();
    }

    [Fact]
    public async Task delete_by_document()
    {
        var issue = new Issue2{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        theSession.Delete(issue);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Issue2>(issue.Id))
            .ShouldBeNull();
    }


    [Theory]
    [InlineData(1)]
    [InlineData(1L)]
    [InlineData("something")]
    public async Task throw_id_mismatch_when_wrong(object id)
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Issue2>(id));
    }

    [Fact]
    public async Task can_not_use_just_guid_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Issue2>(Guid.NewGuid()));
    }

    [Fact]
    public async Task can_not_use_another_guid_based_strong_typed_id_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Issue2>(new WrongId(Guid.NewGuid())));
    }

    [Fact]
    public async Task bulk_writing_async()
    {
        Issue2[] invoices = [
            new Issue2{Name = Guid.NewGuid().ToString()},
            new Issue2{Name = Guid.NewGuid().ToString()},
            new Issue2{Name = Guid.NewGuid().ToString()},
            new Issue2{Name = Guid.NewGuid().ToString()},
            new Issue2{Name = Guid.NewGuid().ToString()}
        ];

        await theStore.BulkInsertDocumentsAsync(invoices);
    }

    [Fact]
    public void bulk_writing_sync()
    {
        Issue2[] invoices = [
            new Issue2{Name = Guid.NewGuid().ToString()},
            new Issue2{Name = Guid.NewGuid().ToString()},
            new Issue2{Name = Guid.NewGuid().ToString()},
            new Issue2{Name = Guid.NewGuid().ToString()},
            new Issue2{Name = Guid.NewGuid().ToString()}
        ];

        theStore.BulkInsertDocuments(invoices);
    }

    [Fact]
    public async Task use_in_LINQ_where_clause()
    {
        var issue = new Issue2{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Issue2>().FirstOrDefaultAsync(x => x.Id == issue.Id);

        loaded
            .Name.ShouldBe(issue.Name);
    }

    [Fact]
    public async Task use_in_LINQ_issue_clause()
    {
        var issue = new Issue2{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Issue2>().OrderBy(x => x.Id).Take(3).ToListAsync();
    }

    [Fact]
    public async Task use_in_LINQ_select_clause()
    {
        var issue = new Issue2{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Issue2>().Select(x => x.Id).Take(3).ToListAsync();

    }
}

[StronglyTypedId(Template.Long)]
public partial struct Issue2Id;

public class Issue2
{
    public Issue2Id? Id { get; set; }
    public string Name { get; set; }
}

public class Issue3
{
    public Issue2Id Id { get; set; }
    public string Name { get; set; }
}

public class long_based_document_operations_with_non_nullable_id : IAsyncLifetime
{
    private readonly DocumentStore theStore;

    public long_based_document_operations_with_non_nullable_id()
    {
        theStore = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "strong_typed";
        });

        theSession = theStore.LightweightSession();
    }

    public async Task InitializeAsync()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Issue3));
    }

    public async Task DisposeAsync()
    {
        await theStore.DisposeAsync();
        theSession?.Dispose();
    }

    private IDocumentSession theSession;

    [Fact]
    public void store_document_will_assign_the_identity()
    {
        var issue = new Issue3();

        theSession.Store(issue);

        issue.Id.ShouldNotBeNull();
        issue.Id.Value.ShouldNotBe(0);
    }

    [Fact]
    public async Task store_a_document_smoke_test()
    {
        var issue = new Issue3();
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Issue3>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task insert_a_document_smoke_test()
    {
        var issue = new Issue3();
        theSession.Insert(issue);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Issue3>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task update_a_document_smoke_test()
    {
        var issue = new Issue3();
        theSession.Insert(issue);
        await theSession.SaveChangesAsync();

        issue.Name = "updated";
        await theSession.SaveChangesAsync();

        var loaded = await theSession.LoadAsync<Issue3>(issue.Id);
        loaded.Name.ShouldBeNull("updated");
    }

    [Fact]
    public async Task use_within_identity_map()
    {
        var issue = new Issue3();
        theSession.Insert(issue);
        await theSession.SaveChangesAsync();

        await using var session = theStore.IdentitySession();
        var loaded1 = await session.LoadAsync<Issue3>(issue.Id);
        var loaded2 = await session.LoadAsync<Issue3>(issue.Id);

        loaded1.ShouldBeSameAs(loaded2);
    }

    [Fact]
    public async Task usage_within_dirty_checking()
    {
        var issue = new Issue3();
        theSession.Insert(issue);
        await theSession.SaveChangesAsync();

        await using var session = theStore.DirtyTrackedSession();
        var loaded1 = await session.LoadAsync<Issue3>(issue.Id);
        loaded1.Name = "something else";

        await session.SaveChangesAsync();

        var loaded2 = await theSession.LoadAsync<Issue3>(issue.Id);
        loaded2.Name.ShouldBe(loaded1.Name);
    }

    [Fact]
    public async Task load_document()
    {
        var issue = new Issue3{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Issue3>(issue.Id))
            .Name.ShouldBe(issue.Name);
    }

    #region sample_strong_typed_identifier_and_is_one_of

    [Fact]
    public async Task load_many()
    {
        var issue1 = new Issue3{Name = Guid.NewGuid().ToString()};
        var Issue3 = new Issue3{Name = Guid.NewGuid().ToString()};
        var issue3 = new Issue3{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue1, Issue3, issue3);

        await theSession.SaveChangesAsync();

        var results = await theSession.Query<Issue3>()
            .Where(x => x.Id.IsOneOf(issue1.Id, Issue3.Id, issue3.Id))
            .ToListAsync();

        results.Count.ShouldBe(3);
    }

    #endregion

    [Fact]
    public async Task delete_by_id()
    {
        var issue = new Issue3{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        theSession.Delete<Issue3>(issue.Id);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Issue3>(issue.Id))
            .ShouldBeNull();
    }

    [Fact]
    public async Task delete_by_document()
    {
        var issue = new Issue3{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        theSession.Delete(issue);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Issue3>(issue.Id))
            .ShouldBeNull();
    }


    [Theory]
    [InlineData(1)]
    [InlineData(1L)]
    [InlineData("something")]
    public async Task throw_id_mismatch_when_wrong(object id)
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Issue3>(id));
    }

    [Fact]
    public async Task can_not_use_just_guid_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Issue3>(Guid.NewGuid()));
    }

    [Fact]
    public async Task can_not_use_another_guid_based_strong_typed_id_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Issue3>(new WrongId(Guid.NewGuid())));
    }

    [Fact]
    public async Task bulk_writing_async()
    {
        Issue3[] invoices = [
            new Issue3{Name = Guid.NewGuid().ToString()},
            new Issue3{Name = Guid.NewGuid().ToString()},
            new Issue3{Name = Guid.NewGuid().ToString()},
            new Issue3{Name = Guid.NewGuid().ToString()},
            new Issue3{Name = Guid.NewGuid().ToString()}
        ];

        await theStore.BulkInsertDocumentsAsync(invoices);
    }

    [Fact]
    public void bulk_writing_sync()
    {
        Issue3[] invoices = [
            new Issue3{Name = Guid.NewGuid().ToString()},
            new Issue3{Name = Guid.NewGuid().ToString()},
            new Issue3{Name = Guid.NewGuid().ToString()},
            new Issue3{Name = Guid.NewGuid().ToString()},
            new Issue3{Name = Guid.NewGuid().ToString()}
        ];

        theStore.BulkInsertDocuments(invoices);
    }

    [Fact]
    public async Task use_in_LINQ_where_clause()
    {
        var issue = new Issue3{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Issue3>().FirstOrDefaultAsync(x => x.Id == issue.Id);

        loaded
            .Name.ShouldBe(issue.Name);
    }

    [Fact]
    public async Task use_in_LINQ_issue_clause()
    {
        var issue = new Issue3{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Issue3>().OrderBy(x => x.Id).Take(3).ToListAsync();
    }

    [Fact]
    public async Task use_in_LINQ_select_clause()
    {
        var issue = new Issue3{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Issue3>().Select(x => x.Id).Take(3).ToListAsync();

    }
}

