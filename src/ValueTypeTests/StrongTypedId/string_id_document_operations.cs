using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using StronglyTypedIds;
using Vogen;

namespace ValueTypeTests.StrongTypedId;

public class string_id_document_operations : IDisposable, IAsyncDisposable
{
    private readonly DocumentStore theStore;

    public string_id_document_operations()
    {
        theStore = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "strong_typed4";

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
        var team = new Team2{Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Team2>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task insert_a_document_smoke_test()
    {
        var team = new Team2{Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Insert(team);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Team2>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task update_a_document_smoke_test()
    {
        var team = new Team2{Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Insert(team);
        await theSession.SaveChangesAsync();

        team.Name = "updated";
        await theSession.SaveChangesAsync();

        var loaded = await theSession.LoadAsync<Team2>(team.Id);
        loaded.Name.ShouldBeNull("updated");
    }

    [Fact]
    public async Task use_within_identity_map()
    {
        var team = new Team2{Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Insert(team);
        await theSession.SaveChangesAsync();

        await using var session = theStore.IdentitySession();
        var loaded1 = await session.LoadAsync<Team2>(team.Id);
        var loaded2 = await session.LoadAsync<Team2>(team.Id);

        loaded1.ShouldBeSameAs(loaded2);
    }

    [Fact]
    public async Task usage_within_dirty_checking()
    {
        var team = new Team2{Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Insert(team);
        await theSession.SaveChangesAsync();

        await using var session = theStore.DirtyTrackedSession();
        var loaded1 = await session.LoadAsync<Team2>(team.Id);
        loaded1.Name = "something else";

        await session.SaveChangesAsync();

        var loaded2 = await theSession.LoadAsync<Team2>(team.Id);
        loaded2.Name.ShouldBe(loaded1.Name);
    }

    [Fact]
    public async Task load_document()
    {
        var team = new Team2{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Team2>(team.Id))
            .Name.ShouldBe(team.Name);
    }

    [Fact]
    public async Task load_many()
    {
        var team1 = new Team2{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        var team2 = new Team2{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        var team3 = new Team2{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Store(team1, team2, team3);

        await theSession.SaveChangesAsync();

        var results = await theSession
            .Query<Team2>()
            .Where(x => x.Id.IsOneOf(team1.Id, team2.Id, team3.Id))
            .ToListAsync();

        results.Count.ShouldBe(3);
    }

    [Fact]
    public async Task delete_by_id()
    {
        var team = new Team2{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        theSession.Delete<Team2>(team.Id);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Team2>(team.Id))
            .ShouldBeNull();
    }

    [Fact]
    public async Task delete_by_document()
    {
        var team = new Team2{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        theSession.Delete(team);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Team2>(team.Id))
            .ShouldBeNull();
    }


    [Theory]
    [InlineData(1)]
    [InlineData(1L)]
    [InlineData("something")]
    public async Task throw_id_mismatch_when_wrong(object id)
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Team2>(id));
    }

    [Fact]
    public async Task can_not_use_just_string_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Team2>("something"));
    }

    [Fact]
    public async Task can_not_use_another_string_based_strong_typed_id_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Team2>(new WrongStringId(Guid.NewGuid().ToString())));
    }

    [Fact]
    public async Task use_in_LINQ_where_clause()
    {
        var team = new Team2{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Team2>().FirstOrDefaultAsync(x => x.Id == team.Id);

        loaded
            .Name.ShouldBe(team.Name);
    }

    [Fact]
    public async Task use_in_LINQ_order_clause()
    {
        var team = new Team2{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Team2>().OrderBy(x => x.Id).Take(3).ToListAsync();
    }

    [Fact]
    public async Task use_in_LINQ_select_clause()
    {
        var team = new Team2{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Team2>().Select(x => x.Id).Take(3).ToListAsync();

    }

}

[StronglyTypedId(Template.String)]
public partial struct Team2Id;

[StronglyTypedId(Template.String)]
public partial struct WrongStringId;


public class Team2
{
    // Marten will use this for the identifier
    // of the Team document
    public Team2Id? Id { get; set; }
    public string Name { get; set; }
}

public class Team3
{
    // Marten will use this for the identifier
    // of the Team document
    public Team2Id Id { get; set; }
    public string Name { get; set; }
}

public class string_id_document_operations_with_non_nullable_id : IDisposable, IAsyncDisposable
{
    private readonly DocumentStore theStore;

    public string_id_document_operations_with_non_nullable_id()
    {
        theStore = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "strong_typed23";

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
        var team = new Team3{Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Team3>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task insert_a_document_smoke_test()
    {
        var team = new Team3{Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Insert(team);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Team3>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task update_a_document_smoke_test()
    {
        var team = new Team3{Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Insert(team);
        await theSession.SaveChangesAsync();

        team.Name = "updated";
        await theSession.SaveChangesAsync();

        var loaded = await theSession.LoadAsync<Team3>(team.Id);
        loaded.Name.ShouldBeNull("updated");
    }

    [Fact]
    public async Task use_within_identity_map()
    {
        var team = new Team3{Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Insert(team);
        await theSession.SaveChangesAsync();

        await using var session = theStore.IdentitySession();
        var loaded1 = await session.LoadAsync<Team3>(team.Id);
        var loaded2 = await session.LoadAsync<Team3>(team.Id);

        loaded1.ShouldBeSameAs(loaded2);
    }

    [Fact]
    public async Task usage_within_dirty_checking()
    {
        var team = new Team3{Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Insert(team);
        await theSession.SaveChangesAsync();

        await using var session = theStore.DirtyTrackedSession();
        var loaded1 = await session.LoadAsync<Team3>(team.Id);
        loaded1.Name = "something else";

        await session.SaveChangesAsync();

        var loaded2 = await theSession.LoadAsync<Team3>(team.Id);
        loaded2.Name.ShouldBe(loaded1.Name);
    }

    [Fact]
    public async Task load_document()
    {
        var team = new Team3{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Team3>(team.Id))
            .Name.ShouldBe(team.Name);
    }

    [Fact]
    public async Task load_many()
    {
        var team1 = new Team3{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        var Team3 = new Team3{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        var team3 = new Team3{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Store(team1, Team3, team3);

        await theSession.SaveChangesAsync();

        var results = await theSession
            .Query<Team3>()
            .Where(x => x.Id.IsOneOf(team1.Id, Team3.Id, team3.Id))
            .ToListAsync();

        results.Count.ShouldBe(3);
    }

    [Fact]
    public async Task delete_by_id()
    {
        var team = new Team3{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        theSession.Delete<Team3>(team.Id);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Team3>(team.Id))
            .ShouldBeNull();
    }

    [Fact]
    public async Task delete_by_document()
    {
        var team = new Team3{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        theSession.Delete(team);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Team3>(team.Id))
            .ShouldBeNull();
    }


    [Theory]
    [InlineData(1)]
    [InlineData(1L)]
    [InlineData("something")]
    public async Task throw_id_mismatch_when_wrong(object id)
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Team3>(id));
    }

    [Fact]
    public async Task can_not_use_just_string_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Team3>("something"));
    }

    [Fact]
    public async Task can_not_use_another_string_based_strong_typed_id_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Team3>(new WrongStringId(Guid.NewGuid().ToString())));
    }

    [Fact]
    public async Task use_in_LINQ_where_clause()
    {
        var team = new Team3{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Team3>().FirstOrDefaultAsync(x => x.Id == team.Id);

        loaded
            .Name.ShouldBe(team.Name);
    }

    [Fact]
    public async Task use_in_LINQ_order_clause()
    {
        var team = new Team3{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Team3>().OrderBy(x => x.Id).Take(3).ToListAsync();
    }

    [Fact]
    public async Task use_in_LINQ_select_clause()
    {
        var team = new Team3{Name = Guid.NewGuid().ToString(), Id = new Team2Id(Guid.NewGuid().ToString())};
        theSession.Store(team);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Team3>().Select(x => x.Id).Take(3).ToListAsync();

    }

}


