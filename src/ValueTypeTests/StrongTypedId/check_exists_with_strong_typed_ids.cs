using System;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace ValueTypeTests.StrongTypedId;

public class check_exists_with_strong_typed_ids: IDisposable, IAsyncDisposable
{
    private readonly DocumentStore theStore;
    private IDocumentSession theSession;

    public check_exists_with_strong_typed_ids()
    {
        theStore = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "strong_typed_exists";

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

    public async ValueTask DisposeAsync()
    {
        if (theStore != null)
        {
            await theStore.DisposeAsync();
        }
    }

    [Fact]
    public async Task check_exists_with_guid_strong_typed_id_hit()
    {
        var invoice = new Invoice2();
        theSession.Store(invoice);
        await theSession.SaveChangesAsync();

        var exists = await theSession.CheckExistsAsync<Invoice2>((object)invoice.Id!.Value);
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task check_exists_with_guid_strong_typed_id_miss()
    {
        var exists = await theSession.CheckExistsAsync<Invoice2>((object)new Invoice2Id(Guid.NewGuid()));
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task check_exists_with_int_strong_typed_id_hit()
    {
        var order = new Order2 { Id = new Order2Id(42), Name = "Test" };
        theSession.Store(order);
        await theSession.SaveChangesAsync();

        var exists = await theSession.CheckExistsAsync<Order2>((object)order.Id!.Value);
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task check_exists_with_int_strong_typed_id_miss()
    {
        var exists = await theSession.CheckExistsAsync<Order2>((object)new Order2Id(999999));
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task check_exists_with_long_strong_typed_id_hit()
    {
        var issue = new Issue2 { Id = new Issue2Id(500L), Name = "Test" };
        theSession.Store(issue);
        await theSession.SaveChangesAsync();

        var exists = await theSession.CheckExistsAsync<Issue2>((object)issue.Id!.Value);
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task check_exists_with_long_strong_typed_id_miss()
    {
        var exists = await theSession.CheckExistsAsync<Issue2>((object)new Issue2Id(999999L));
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task check_exists_with_string_strong_typed_id_hit()
    {
        var team = new Team2 { Id = new Team2Id("team-exists-test"), Name = "Test" };
        theSession.Store(team);
        await theSession.SaveChangesAsync();

        var exists = await theSession.CheckExistsAsync<Team2>((object)team.Id!.Value);
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task check_exists_with_string_strong_typed_id_miss()
    {
        var exists = await theSession.CheckExistsAsync<Team2>((object)new Team2Id("nonexistent"));
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task check_exists_in_batch_with_guid_strong_typed_id()
    {
        var invoice = new Invoice2();
        theSession.Store(invoice);
        await theSession.SaveChangesAsync();

        var batch = theSession.CreateBatchQuery();
        var existsHit = batch.CheckExists<Invoice2>((object)invoice.Id!.Value);
        var existsMiss = batch.CheckExists<Invoice2>((object)new Invoice2Id(Guid.NewGuid()));
        await batch.Execute();

        (await existsHit).ShouldBeTrue();
        (await existsMiss).ShouldBeFalse();
    }

    [Fact]
    public async Task check_exists_in_batch_with_int_strong_typed_id()
    {
        var order = new Order2 { Id = new Order2Id(88), Name = "Batch Test" };
        theSession.Store(order);
        await theSession.SaveChangesAsync();

        var batch = theSession.CreateBatchQuery();
        var existsHit = batch.CheckExists<Order2>((object)order.Id!.Value);
        var existsMiss = batch.CheckExists<Order2>((object)new Order2Id(777777));
        await batch.Execute();

        (await existsHit).ShouldBeTrue();
        (await existsMiss).ShouldBeFalse();
    }

    [Fact]
    public async Task check_exists_in_batch_with_long_strong_typed_id()
    {
        var issue = new Issue2 { Id = new Issue2Id(600L), Name = "Batch Test" };
        theSession.Store(issue);
        await theSession.SaveChangesAsync();

        var batch = theSession.CreateBatchQuery();
        var existsHit = batch.CheckExists<Issue2>((object)issue.Id!.Value);
        var existsMiss = batch.CheckExists<Issue2>((object)new Issue2Id(888888L));
        await batch.Execute();

        (await existsHit).ShouldBeTrue();
        (await existsMiss).ShouldBeFalse();
    }

    [Fact]
    public async Task check_exists_in_batch_with_string_strong_typed_id()
    {
        var team = new Team2 { Id = new Team2Id("batch-exists-test"), Name = "Batch Test" };
        theSession.Store(team);
        await theSession.SaveChangesAsync();

        var batch = theSession.CreateBatchQuery();
        var existsHit = batch.CheckExists<Team2>((object)team.Id!.Value);
        var existsMiss = batch.CheckExists<Team2>((object)new Team2Id("not-there"));
        await batch.Execute();

        (await existsHit).ShouldBeTrue();
        (await existsMiss).ShouldBeFalse();
    }
}
