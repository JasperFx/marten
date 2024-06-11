using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Vogen;

namespace ValueTypeTests.Vogen;

public class string_id_document_operations : IDisposable, IAsyncDisposable
{
    private readonly DocumentStore theStore;

    public string_id_document_operations()
    {
        theStore = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "strong_typed";

            opts.ApplicationAssembly = GetType().Assembly;
            opts.GeneratedCodeMode = TypeLoadMode.Auto;
            opts.GeneratedCodeOutputPath =
                AppContext.BaseDirectory.ParentDirectory().ParentDirectory().ParentDirectory().AppendPath("Internal", "Generated");
        });

        theSession = theStore.LightweightSession();
    }

    public void Dispose()
    {
        theStore?.Dispose();
        theSession?.Dispose();
    }

    private IDocumentSession theSession;

    public async ValueTask DisposeAsync()
    {
        if (theStore != null)
        {
            await theStore.DisposeAsync();
        }
    }

    [Fact]
    public async Task store_a_document_smoke_test()
    {
        var team = new Team{Id = TeamId.From(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Team>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task insert_a_document_smoke_test()
    {
        var team = new Team{Id = TeamId.From(Guid.NewGuid().ToString())};
        theSession.Insert(team);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Team>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task update_a_document_smoke_test()
    {
        var team = new Team{Id = TeamId.From(Guid.NewGuid().ToString())};
        theSession.Insert(team);
        await theSession.SaveChangesAsync();

        team.Name = "updated";
        await theSession.SaveChangesAsync();

        var loaded = await theSession.LoadAsync<Team>(team.Id);
        loaded.Name.ShouldBeNull("updated");
    }

    [Fact]
    public async Task use_within_identity_map()
    {
        var team = new Team{Id = TeamId.From(Guid.NewGuid().ToString())};
        theSession.Insert(team);
        await theSession.SaveChangesAsync();

        await using var session = theStore.IdentitySession();
        var loaded1 = await session.LoadAsync<Team>(team.Id);
        var loaded2 = await session.LoadAsync<Team>(team.Id);

        loaded1.ShouldBeSameAs(loaded2);
    }

    [Fact]
    public async Task usage_within_dirty_checking()
    {
        var team = new Team{Id = TeamId.From(Guid.NewGuid().ToString())};
        theSession.Insert(team);
        await theSession.SaveChangesAsync();

        await using var session = theStore.DirtyTrackedSession();
        var loaded1 = await session.LoadAsync<Team>(team.Id);
        loaded1.Name = "something else";

        await session.SaveChangesAsync();

        var loaded2 = await theSession.LoadAsync<Team>(team.Id);
        loaded2.Name.ShouldBe(loaded1.Name);
    }

    [Fact]
    public async Task load_document()
    {
        var team = new Team{Name = Guid.NewGuid().ToString(), Id = TeamId.From(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Team>(team.Id))
            .Name.ShouldBe(team.Name);
    }

    [Fact]
    public async Task load_many()
    {
        var team1 = new Team{Name = Guid.NewGuid().ToString(), Id = TeamId.From(Guid.NewGuid().ToString())};
        var team2 = new Team{Name = Guid.NewGuid().ToString(), Id = TeamId.From(Guid.NewGuid().ToString())};
        var team3 = new Team{Name = Guid.NewGuid().ToString(), Id = TeamId.From(Guid.NewGuid().ToString())};
        theSession.Store(team1, team2, team3);

        await theSession.SaveChangesAsync();

        var results = await theSession
            .Query<Team>()
            .Where(x => x.Id.IsOneOf(team1.Id, team2.Id, team3.Id))
            .ToListAsync();

        results.Count.ShouldBe(3);
    }

    [Fact]
    public async Task delete_by_id()
    {
        var team = new Team{Name = Guid.NewGuid().ToString(), Id = TeamId.From(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        theSession.Delete<Team>(team.Id);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Team>(team.Id))
            .ShouldBeNull();
    }

    [Fact]
    public async Task delete_by_document()
    {
        var team = new Team{Name = Guid.NewGuid().ToString(), Id = TeamId.From(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        theSession.Delete(team);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Team>(team.Id))
            .ShouldBeNull();
    }


    [Theory]
    [InlineData(1)]
    [InlineData(1L)]
    [InlineData("something")]
    public async Task throw_id_mismatch_when_wrong(object id)
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Team>(id));
    }

    [Fact]
    public async Task can_not_use_just_string_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Team>("something"));
    }

    [Fact]
    public async Task can_not_use_another_string_based_strong_typed_id_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Team>(WrongStringId.From(Guid.NewGuid().ToString())));
    }

    [Fact]
    public async Task use_in_LINQ_where_clause()
    {
        var team = new Team{Name = Guid.NewGuid().ToString(), Id = TeamId.From(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Team>().FirstOrDefaultAsync(x => x.Id == team.Id);

        loaded
            .Name.ShouldBe(team.Name);
    }

    [Fact]
    public async Task use_in_LINQ_order_clause()
    {
        var team = new Team{Name = Guid.NewGuid().ToString(), Id = TeamId.From(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Team>().OrderBy(x => x.Id).Take(3).ToListAsync();
    }

    [Fact]
    public async Task use_in_LINQ_select_clause()
    {
        var team = new Team{Name = Guid.NewGuid().ToString(), Id = TeamId.From(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Team>().Select(x => x.Id).Take(3).ToListAsync();

    }

}

[ValueObject<string>]
public partial struct TeamId;

[ValueObject<string>]
public partial struct WrongStringId;


public class Team
{
    // Marten will use this for the identifier
    // of the Team document
    public TeamId? Id { get; set; }
    public string Name { get; set; }
}

