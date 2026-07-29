using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Newtonsoft;
using Marten.Patching;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

/// <summary>
/// #5076 — a lazy LINQ sequence assigned to a persisted member used to be written by
/// Newtonsoft's TypeNameHandling.Auto as $type = the iterator's concrete type. The write
/// succeeded and every later read threw, because types like
/// System.Linq.Enumerable+AppendPrepend1Iterator cannot be constructed.
/// </summary>
public class Bug_5076_lazy_enumerable_round_trip: BugIntegrationContext
{
    public class Basket
    {
        public Guid Id { get; set; }

        // Declared as IEnumerable<T> on purpose: that is what makes Newtonsoft record the
        // runtime type, and it is a perfectly ordinary way to model a read-only sequence.
        public IEnumerable<string> Items { get; set; } = [];
    }

    private void useNewtonsoft() => StoreOptions(opts =>
    {
        // Pinned so the test means the same thing on both CI serializer legs.
        opts.UseNewtonsoftForSerialization();
        opts.Schema.For<Basket>();
    });

    [Fact]
    public async Task can_read_back_a_document_holding_an_appended_sequence()
    {
        useNewtonsoft();

        var basket = new Basket { Items = new[] { "first" }.Append("second") };

        await using (var session = theStore.LightweightSession())
        {
            session.Store(basket);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<Basket>(basket.Id);

        loaded.ShouldNotBeNull();
        loaded.Items.ShouldBe(new[] { "first", "second" });
    }

    [Fact]
    public async Task can_read_back_a_document_holding_a_filtered_sequence()
    {
        useNewtonsoft();

        var basket = new Basket { Items = new[] { "keep", "drop", "keep too" }.Where(x => x.StartsWith("keep")) };

        await using (var session = theStore.LightweightSession())
        {
            session.Store(basket);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<Basket>(basket.Id);

        loaded.ShouldNotBeNull();
        loaded.Items.ShouldBe(new[] { "keep", "keep too" });
    }

    [Fact]
    public async Task a_materialised_sequence_still_round_trips()
    {
        useNewtonsoft();

        var basket = new Basket { Items = new List<string> { "one", "two" } };

        await using (var session = theStore.LightweightSession())
        {
            session.Store(basket);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<Basket>(basket.Id);

        loaded.ShouldNotBeNull();
        loaded.Items.ShouldBe(new[] { "one", "two" });
    }

    [Fact]
    public async Task can_read_back_after_patching_with_a_lazy_sequence()
    {
        useNewtonsoft();

        var basket = new Basket { Items = new[] { "original" } };

        await using (var session = theStore.LightweightSession())
        {
            session.Store(basket);
            await session.SaveChangesAsync();
        }

        // Patch values go through the withTypes serializer, where TypeNameHandling.Objects
        // stamps $type onto everything -- so a lazy sequence hits the same trap there.
        await using (var session = theStore.LightweightSession())
        {
            session.Patch<Basket>(basket.Id).Set(x => x.Items, new[] { "a", "b", "skip" }.Where(x => x != "skip"));
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<Basket>(basket.Id);

        loaded.ShouldNotBeNull();
        loaded.Items.ShouldBe(new[] { "a", "b" });
    }
}
