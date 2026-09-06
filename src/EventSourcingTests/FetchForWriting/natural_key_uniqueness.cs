using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
/// #5344 / the jasperfx#764 ruling: a natural key already mapped to a live stream is refused to a
/// second claimant rather than repointed at it.
/// </summary>
/// <remarks>
/// The behaviour itself is pinned by <c>NaturalKeyCompliance.a_second_stream_cannot_claim_a_live_natural_key</c>.
/// What is local here is the batch shape that fact does not reach: the refusal is enforced by a
/// storage operation that reads a <c>RETURNING</c> row back in <c>PostprocessAsync</c>, so several
/// claims committed in ONE <c>SaveChangesAsync</c> have to stay aligned with their own result sets.
/// A single-claim fact cannot tell a correct implementation from one that reads the wrong row.
/// </remarks>
public class natural_key_uniqueness: OneOffConfigurationsContext
{
    public record InvoiceNumber(string Value);

    public record InvoiceRaised(string Number, string Customer);

    public record InvoiceRenumbered(string Number);

    public class UniquenessInvoice
    {
        public Guid Id { get; set; }

        [NaturalKey]
        public InvoiceNumber Number { get; set; } = null!;

        public string Customer { get; set; } = string.Empty;

        [NaturalKeySource]
        public static UniquenessInvoice Create(IEvent<InvoiceRaised> e) =>
            new() { Id = e.StreamId, Number = new InvoiceNumber(e.Data.Number), Customer = e.Data.Customer };

        [NaturalKeySource]
        public void Apply(InvoiceRenumbered e) => Number = new InvoiceNumber(e.Number);
    }

    private async Task configure()
    {
        StoreOptions(opts =>
        {
            opts.DatabaseSchemaName = "natural_key_uniqueness";
            opts.RegisterValueType(typeof(InvoiceNumber));
            opts.Projections.Snapshot<UniquenessInvoice>(SnapshotLifecycle.Inline);
        });

        await theStore.Advanced.Clean.DeleteAllEventDataAsync();
    }

    [Fact]
    public async Task several_streams_claiming_distinct_keys_in_one_batch_all_commit()
    {
        await configure();

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<UniquenessInvoice>(first, new InvoiceRaised("INV-1", "Alice"));
            session.Events.StartStream<UniquenessInvoice>(second, new InvoiceRaised("INV-2", "Bob"));
            // Two claims for one stream inside the same batch: the create, then a rename that
            // retires it. The last one has to win.
            session.Events.StartStream<UniquenessInvoice>(third, new InvoiceRaised("INV-3", "Carol"),
                new InvoiceRenumbered("INV-3-REVISED"));
            await session.SaveChangesAsync();
        }

        await using var query = theStore.LightweightSession();

        (await query.Events.FetchLatest<UniquenessInvoice, InvoiceNumber>(new InvoiceNumber("INV-1")))
            .ShouldNotBeNull().Id.ShouldBe(first);
        (await query.Events.FetchLatest<UniquenessInvoice, InvoiceNumber>(new InvoiceNumber("INV-2")))
            .ShouldNotBeNull().Id.ShouldBe(second);
        (await query.Events.FetchLatest<UniquenessInvoice, InvoiceNumber>(new InvoiceNumber("INV-3-REVISED")))
            .ShouldNotBeNull().Id.ShouldBe(third);

        // The superseded key is retired, not left pointing at the stream that gave it up.
        (await query.Events.FetchLatest<UniquenessInvoice, InvoiceNumber>(new InvoiceNumber("INV-3")))
            .ShouldBeNull();
    }

    [Fact]
    public async Task a_duplicate_inside_a_multi_claim_batch_is_refused_and_names_both_streams()
    {
        await configure();

        var original = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<UniquenessInvoice>(original, new InvoiceRaised("INV-DUP", "Alice"));
            await session.SaveChangesAsync();
        }

        var claimant = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            // A legitimate claim and a duplicate in the same batch: the duplicate has to be the one
            // that raises, which only holds if each claim is matched to its own returned row.
            session.Events.StartStream<UniquenessInvoice>(Guid.NewGuid(), new InvoiceRaised("INV-OK", "Bob"));
            session.Events.StartStream<UniquenessInvoice>(claimant, new InvoiceRaised("INV-DUP", "Mallory"));

            var ex = await Should.ThrowAsync<DuplicateNaturalKeyException>(() => session.SaveChangesAsync());

            ex.Key.ShouldBe("INV-DUP");
            ex.AggregateType.ShouldBe(typeof(UniquenessInvoice));
            ex.ExistingStreamId.ShouldBe(original);
            ex.ClaimingStreamId.ShouldBe(claimant);
        }

        // The whole unit of work rolled back, so the legitimate claim in that batch is gone too and
        // the original mapping is untouched.
        await using var query = theStore.LightweightSession();

        var invoice = await query.Events.FetchLatest<UniquenessInvoice, InvoiceNumber>(new InvoiceNumber("INV-DUP"));
        invoice.ShouldNotBeNull();
        invoice.Id.ShouldBe(original);
        invoice.Customer.ShouldBe("Alice");

        (await query.Events.FetchLatest<UniquenessInvoice, InvoiceNumber>(new InvoiceNumber("INV-OK")))
            .ShouldBeNull();
    }

    [Fact]
    public async Task an_archived_streams_key_can_be_claimed_by_a_new_stream()
    {
        await configure();

        var original = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<UniquenessInvoice>(original, new InvoiceRaised("INV-RECYCLED", "Alice"));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.ArchiveStream(original);
            await session.SaveChangesAsync();
        }

        // The ruling refuses a claim on a *live* key. Once the holder is archived the identifier is
        // free again, which is the half that would be lost by refusing on the lookup row alone.
        var replacement = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<UniquenessInvoice>(replacement, new InvoiceRaised("INV-RECYCLED", "Bob"));
            await session.SaveChangesAsync();
        }

        await using var query = theStore.LightweightSession();

        var invoice = await query.Events.FetchLatest<UniquenessInvoice, InvoiceNumber>(new InvoiceNumber("INV-RECYCLED"));
        invoice.ShouldNotBeNull();
        invoice.Id.ShouldBe(replacement);
        invoice.Customer.ShouldBe("Bob");
    }
}
