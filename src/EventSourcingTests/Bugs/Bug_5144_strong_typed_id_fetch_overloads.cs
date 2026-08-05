using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

// #5144: FetchForWriting<T, TId> / FetchForExclusiveWriting / FetchLatest accept a strong-typed
// identifier. Before the fix, a TId that is neither Guid nor string planned down the natural-key
// branch, which passes a null identity strategy; the lifecycle planners matched anyway and stored
// the null, so the call died with a bare NullReferenceException.
public readonly record struct Bug5144PaymentId(Guid Value);

public readonly record struct Bug5144InvoiceId(string Value);

public record Bug5144PaymentRaised(decimal Amount);

public record Bug5144PaymentSettled(decimal Amount);

public class Bug5144Payment
{
    public Bug5144PaymentId Id { get; set; }
    public decimal Outstanding { get; set; }

    public static Bug5144Payment Create(IEvent<Bug5144PaymentRaised> e)
        => new() { Id = new Bug5144PaymentId(e.StreamId), Outstanding = e.Data.Amount };

    public void Apply(Bug5144PaymentSettled e) => Outstanding -= e.Amount;
}

public class Bug5144Invoice
{
    public Bug5144InvoiceId Id { get; set; }
    public decimal Outstanding { get; set; }

    public static Bug5144Invoice Create(IEvent<Bug5144PaymentRaised> e)
        => new() { Id = new Bug5144InvoiceId(e.StreamKey!), Outstanding = e.Data.Amount };

    public void Apply(Bug5144PaymentSettled e) => Outstanding -= e.Amount;
}

public class Bug_5144_strong_typed_id_fetch_overloads: OneOffConfigurationsContext
{
    [Theory]
    [InlineData(SnapshotLifecycle.Inline)]
    [InlineData(SnapshotLifecycle.Async)]
    public async Task fetch_for_writing_by_a_guid_backed_strong_typed_id(SnapshotLifecycle lifecycle)
    {
        StoreOptions(opts =>
        {
            opts.RegisterValueType<Bug5144PaymentId>();
            opts.Projections.Snapshot<Bug5144Payment>(lifecycle);
        });

        var streamId = theSession.Events
            .StartStream<Bug5144Payment>(new Bug5144PaymentRaised(100m)).Id;
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        var stream = await session.Events
            .FetchForWriting<Bug5144Payment, Bug5144PaymentId>(new Bug5144PaymentId(streamId));

        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.Id.Value.ShouldBe(streamId);
        stream.Aggregate.Outstanding.ShouldBe(100m);
    }

    [Fact]
    public async Task fetch_for_exclusive_writing_by_a_strong_typed_id()
    {
        StoreOptions(opts =>
        {
            opts.RegisterValueType<Bug5144PaymentId>();
            opts.Projections.Snapshot<Bug5144Payment>(SnapshotLifecycle.Inline);
        });

        var streamId = theSession.Events
            .StartStream<Bug5144Payment>(new Bug5144PaymentRaised(60m)).Id;
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        var stream = await session.Events
            .FetchForExclusiveWriting<Bug5144Payment, Bug5144PaymentId>(new Bug5144PaymentId(streamId));

        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.Outstanding.ShouldBe(60m);
    }

    [Fact]
    public async Task fetch_latest_by_a_strong_typed_id()
    {
        StoreOptions(opts =>
        {
            opts.RegisterValueType<Bug5144PaymentId>();
            opts.Projections.Snapshot<Bug5144Payment>(SnapshotLifecycle.Inline);
        });

        var streamId = theSession.Events
            .StartStream<Bug5144Payment>(new Bug5144PaymentRaised(100m), new Bug5144PaymentSettled(40m)).Id;
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        var payment = await session.Events
            .FetchLatest<Bug5144Payment, Bug5144PaymentId>(new Bug5144PaymentId(streamId));

        payment.ShouldNotBeNull();
        payment.Outstanding.ShouldBe(60m);
    }

    [Fact]
    public async Task fetch_for_writing_by_a_string_backed_strong_typed_id()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.RegisterValueType<Bug5144InvoiceId>();
            opts.Projections.Snapshot<Bug5144Invoice>(SnapshotLifecycle.Inline);
        });

        var key = "invoice/" + Guid.NewGuid().ToString("N");
        theSession.Events.StartStream<Bug5144Invoice>(key, new Bug5144PaymentRaised(25m));
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        var stream = await session.Events
            .FetchForWriting<Bug5144Invoice, Bug5144InvoiceId>(new Bug5144InvoiceId(key));

        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.Id.Value.ShouldBe(key);
        stream.Aggregate.Outstanding.ShouldBe(25m);
    }

    [Fact]
    public async Task an_unknown_strong_typed_id_yields_an_empty_handle()
    {
        StoreOptions(opts =>
        {
            opts.RegisterValueType<Bug5144PaymentId>();
            opts.Projections.Snapshot<Bug5144Payment>(SnapshotLifecycle.Inline);
        });

        await using var session = theStore.LightweightSession();
        var stream = await session.Events
            .FetchForWriting<Bug5144Payment, Bug5144PaymentId>(new Bug5144PaymentId(Guid.NewGuid()));

        stream.Aggregate.ShouldBeNull();
        stream.StartingVersion.ShouldBe(0);
    }
}
