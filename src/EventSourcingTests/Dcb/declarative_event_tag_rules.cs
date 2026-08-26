#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

public interface IInvoiceEvent
{
    Guid Invoice { get; }
}

public record InvoiceId(Guid Value);

public record LineId(Guid Value);

public record InvoiceRaised(Guid Invoice, decimal Amount): IInvoiceEvent;

public record LineInvoiced(Guid Invoice, Guid Line): IInvoiceEvent;

public record UnrelatedThingHappened(string Description);

/// <summary>
/// Tag rules derive a tag from the event's own data, wherever the event is built.
/// <para>
/// <see cref="EventTagInference" /> covers neither case here: it matches a property by its <i>type</i>, so
/// an event naming its identifiers as <c>Guid</c> can never be inferred, and it only runs on the
/// <c>IEventBoundary</c> path. These events reach the store through an ordinary append.
/// </para>
/// </summary>
[Collection("OneOffs")]
public class declarative_event_tag_rules: OneOffConfigurationsContext
{
    private void ConfigureStore(Action<StoreOptions>? extra = null)
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;
            opts.Events.DcbStorageMode = DcbStorageMode.HStore;

            opts.Events.RegisterTagType<InvoiceId>("invoice");
            opts.Events.RegisterTagType<LineId>("line");

            // One rule per tag type. The first is declared on the interface, so it reaches every event that
            // belongs to an invoice without naming them one by one.
            opts.Events.TagWith<IInvoiceEvent>(e => new InvoiceId(e.Invoice));
            opts.Events.TagWith<LineInvoiced>(e => new LineId(e.Line));

            extra?.Invoke(opts);
        });
    }

    [Fact]
    public async Task a_rule_tags_an_ordinary_append()
    {
        ConfigureStore();

        var invoice = Guid.NewGuid();
        var line = Guid.NewGuid();

        theSession.Events.StartStream(Guid.NewGuid(), new InvoiceRaised(invoice, 12.5m));
        theSession.Events.StartStream(Guid.NewGuid(), new LineInvoiced(invoice, line));
        await theSession.SaveChangesAsync();

        // The interface rule reaches both events, across the two streams they were appended to.
        var byInvoice = await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or(new InvoiceId(invoice)));
        byInvoice.Count.ShouldBe(2);

        // The second rule contributes a different tag type to one of the same events.
        var byLine = await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or(new LineId(line)));
        byLine.Count.ShouldBe(1);
        byLine[0].Data.ShouldBeOfType<LineInvoiced>().Line.ShouldBe(line);
    }

    [Fact]
    public async Task an_event_no_rule_matches_is_left_alone()
    {
        ConfigureStore();

        theSession.Events.StartStream(Guid.NewGuid(), new UnrelatedThingHappened("nothing to see"));
        await theSession.SaveChangesAsync();

        var found = await theSession.Events.QueryByTagsAsync(
            new EventTagQuery().Or(new InvoiceId(Guid.NewGuid())));

        found.Count.ShouldBe(0);
    }

    [Fact]
    public async Task a_rule_that_returns_null_leaves_the_event_untagged()
    {
        var invoice = Guid.NewGuid();
        ConfigureStore(opts => opts.Events.TagWith<UnrelatedThingHappened>(_ => null));

        theSession.Events.StartStream(Guid.NewGuid(), new UnrelatedThingHappened("still nothing"));
        await theSession.SaveChangesAsync();

        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or(new InvoiceId(invoice))))
            .Count.ShouldBe(0);
    }

    [Fact]
    public async Task an_explicit_tag_wins_over_the_rule()
    {
        ConfigureStore();

        var claimed = Guid.NewGuid();
        var overridden = Guid.NewGuid();

        var tagged = new Event<InvoiceRaised>(new InvoiceRaised(claimed, 1m));
        tagged.WithTag(new InvoiceId(overridden));

        theSession.Events.StartStream(Guid.NewGuid(), tagged);
        await theSession.SaveChangesAsync();

        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or(new InvoiceId(overridden))))
            .Count.ShouldBe(1);

        // A tag type already on the event is left alone rather than added a second time, which in HStore
        // mode would throw instead of overwriting.
        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or(new InvoiceId(claimed))))
            .Count.ShouldBe(0);
    }

    [Fact]
    public async Task tags_from_a_rule_survive_a_bulk_insert()
    {
        ConfigureStore();

        var invoice = Guid.NewGuid();
        var action = StreamAction.Start(theStore.Events, Guid.NewGuid(), new InvoiceRaised(invoice, 3m));

        await theStore.BulkInsertEventsAsync(new List<StreamAction> { action });

        var found = await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or(new InvoiceId(invoice)));
        found.Count.ShouldBe(1);
    }

    /// <summary>
    /// One store-wide rule, for an application that already owns a single place that knows what an event is
    /// about. Without it the same translator has to be restated as one registration per tag type.
    /// </summary>
    [Fact]
    public async Task one_store_wide_rule_can_carry_every_tag()
    {
        var invoice = Guid.NewGuid();
        var line = Guid.NewGuid();

        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;
            opts.Events.DcbStorageMode = DcbStorageMode.HStore;
            opts.Events.RegisterTagType<InvoiceId>("invoice");
            opts.Events.RegisterTagType<LineId>("line");

            opts.Events.TagEventsBy(data => data switch
            {
                LineInvoiced e => [new InvoiceId(e.Invoice), new LineId(e.Line)],
                IInvoiceEvent e => [new InvoiceId(e.Invoice)],
                _ => Array.Empty<object>()
            });
        });

        theSession.Events.StartStream(Guid.NewGuid(), new InvoiceRaised(invoice, 4m));
        theSession.Events.StartStream(Guid.NewGuid(), new LineInvoiced(invoice, line));
        theSession.Events.StartStream(Guid.NewGuid(), new UnrelatedThingHappened("no tags here"));
        await theSession.SaveChangesAsync();

        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or(new InvoiceId(invoice))))
            .Count.ShouldBe(2);

        var byLine = await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or(new LineId(line)));
        byLine.Count.ShouldBe(1);
        byLine[0].Data.ShouldBeOfType<LineInvoiced>();
    }

    [Fact]
    public async Task a_rule_producing_an_unregistered_tag_type_says_so()
    {
        ConfigureStore(opts => opts.Events.TagWith<UnrelatedThingHappened>(e => new LineId(Guid.NewGuid())));
        // LineId *is* registered above, so use a type that is not.
        var options = new StoreOptions();
        options.Connection(ConnectionSource.ConnectionString);
        options.Events.DcbStorageMode = DcbStorageMode.HStore;
        options.Events.TagWith<UnrelatedThingHappened>(_ => new InvoiceId(Guid.NewGuid()));

        await using var store = new DocumentStore(options);
        await using var session = store.LightweightSession();

        var ex = Should.Throw<InvalidOperationException>(() =>
            session.Events.StartStream(Guid.NewGuid(), new UnrelatedThingHappened("boom")));

        ex.Message.ShouldContain("not a registered tag type");
        ex.Message.ShouldContain("RegisterTagType<InvoiceId>()");
    }
}
