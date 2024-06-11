using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Vogen;

namespace ValueTypeTests;

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
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Issue));
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
        var issue = new Issue();
        theSession.Store(issue);

        issue.Id.ShouldNotBeNull();
        issue.Id.Value.Value.ShouldNotBe(0);
    }

    [Fact]
    public async Task store_a_document_smoke_test()
    {
        var issue = new Issue();
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Issue>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task insert_a_document_smoke_test()
    {
        var issue = new Issue();
        theSession.Insert(issue);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Issue>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task update_a_document_smoke_test()
    {
        var issue = new Issue();
        theSession.Insert(issue);
        await theSession.SaveChangesAsync();

        issue.Name = "updated";
        await theSession.SaveChangesAsync();

        var loaded = await theSession.LoadAsync<Issue>(issue.Id);
        loaded.Name.ShouldBeNull("updated");
    }

    [Fact]
    public async Task use_within_identity_map()
    {
        var issue = new Issue();
        theSession.Insert(issue);
        await theSession.SaveChangesAsync();

        await using var session = theStore.IdentitySession();
        var loaded1 = await session.LoadAsync<Issue>(issue.Id);
        var loaded2 = await session.LoadAsync<Issue>(issue.Id);

        loaded1.ShouldBeSameAs(loaded2);
    }

    [Fact]
    public async Task usage_within_dirty_checking()
    {
        var issue = new Issue();
        theSession.Insert(issue);
        await theSession.SaveChangesAsync();

        await using var session = theStore.DirtyTrackedSession();
        var loaded1 = await session.LoadAsync<Issue>(issue.Id);
        loaded1.Name = "something else";

        await session.SaveChangesAsync();

        var loaded2 = await theSession.LoadAsync<Issue>(issue.Id);
        loaded2.Name.ShouldBe(loaded1.Name);
    }

    [Fact]
    public async Task load_document()
    {
        var issue = new Issue{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Issue>(issue.Id))
            .Name.ShouldBe(issue.Name);
    }

    [Fact]
    public async Task load_many()
    {
        var issue1 = new Issue{Name = Guid.NewGuid().ToString()};
        var issue2 = new Issue{Name = Guid.NewGuid().ToString()};
        var issue3 = new Issue{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue1, issue2, issue3);

        await theSession.SaveChangesAsync();

        var results = await theSession.Query<Issue>().Where(x => x.Id.IsOneOf(issue1.Id, issue2.Id, issue3.Id)).ToListAsync();
        results.Count.ShouldBe(3);
    }

    [Fact]
    public async Task delete_by_id()
    {
        var issue = new Issue{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        theSession.Delete<Issue>(issue.Id);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Issue>(issue.Id))
            .ShouldBeNull();
    }

    [Fact]
    public async Task delete_by_document()
    {
        var issue = new Issue{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        theSession.Delete(issue);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Issue>(issue.Id))
            .ShouldBeNull();
    }


    [Theory]
    [InlineData(1)]
    [InlineData(1L)]
    [InlineData("something")]
    public async Task throw_id_mismatch_when_wrong(object id)
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Issue>(id));
    }

    [Fact]
    public async Task can_not_use_just_guid_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Issue>(Guid.NewGuid()));
    }

    [Fact]
    public async Task can_not_use_another_guid_based_strong_typed_id_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Issue>(WrongId.From(Guid.NewGuid())));
    }

    [Fact]
    public async Task use_in_LINQ_where_clause()
    {
        var issue = new Issue{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Issue>().FirstOrDefaultAsync(x => x.Id == issue.Id);

        loaded
            .Name.ShouldBe(issue.Name);
    }

    [Fact]
    public async Task use_in_LINQ_issue_clause()
    {
        var issue = new Issue{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Issue>().OrderBy(x => x.Id).Take(3).ToListAsync();
    }

    [Fact]
    public async Task use_in_LINQ_select_clause()
    {
        var issue = new Issue{Name = Guid.NewGuid().ToString()};
        theSession.Store(issue);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Issue>().Select(x => x.Id).Take(3).ToListAsync();

    }
}

[ValueObject<long>]
public partial struct IssueId;

public class Issue
{
    public IssueId? Id { get; set; }
    public string Name { get; set; }
}

