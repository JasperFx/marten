#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

public record BadgeIssued(string Badge);

public record BadgeScanned;

public class BadgeHolder
{
    public Guid Id { get; set; }

    [NaturalKey]
    public string? Badge { get; set; }

    public int Scans { get; set; }

    [NaturalKeySource]
    public void Apply(BadgeIssued e) => Badge = e.Badge;

    public void Apply(BadgeScanned e) => Scans++;
}

/// <summary>
///     marten#6133: a batched natural-key fetch resolves the key in the batch and then loads the stream it found with
///     commands of its own. Inside a transaction the session has one connection, so those commands ran into the
///     batch's open reader ("A command is already in progress") and failed the whole batch, whatever the aggregate's
///     lifecycle. An exclusive fetch always opens that transaction; a caller can open it too.
/// </summary>
public class batched_natural_key_fetch_inside_a_transaction: OneOffConfigurationsContext
{
    public static TheoryData<string, bool> Cases => new()
    {
        { "inline", true }, { "inline", false },
        { "async", true }, { "async", false },
        { "live", true }, { "live", false }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task several_natural_key_items_and_a_stream_id_item_share_the_batch(string lifecycle,
        bool exclusive)
    {
        StoreOptions(opts =>
        {
            switch (lifecycle)
            {
                case "inline":
                    opts.Projections.Snapshot<BadgeHolder>(SnapshotLifecycle.Inline);
                    break;
                case "async":
                    opts.Projections.Snapshot<BadgeHolder>(SnapshotLifecycle.Async);
                    break;
                default:
                    opts.Projections.LiveStreamAggregation<BadgeHolder>();
                    break;
            }
        });

        var token = TestContext.Current.CancellationToken;
        var (first, second) = ($"B-{Guid.NewGuid():N}", $"B-{Guid.NewGuid():N}");

        var firstId = theSession.Events.StartStream<BadgeHolder>(new BadgeIssued(first), new BadgeScanned()).Id;
        var secondId = theSession.Events
            .StartStream<BadgeHolder>(new BadgeIssued(second), new BadgeScanned(), new BadgeScanned()).Id;
        var keylessId = theSession.Events.StartStream<BadgeHolder>(new BadgeScanned()).Id;
        await theSession.SaveChangesAsync(token);

        await using var session = theStore.LightweightSession();
        if (!exclusive)
        {
            await session.BeginTransactionAsync(token);
        }

        var batch = session.CreateBatchQuery();

        var latestFirst = batch.Events.FetchLatest<BadgeHolder>(first);
        var writingSecond = exclusive
            ? batch.Events.FetchForExclusiveWriting<BadgeHolder>(second)
            : batch.Events.FetchForWriting<BadgeHolder>(second);
        var keyless = batch.Events.FetchLatest<BadgeHolder>(keylessId);
        var writingFirst = batch.Events.FetchForWriting<BadgeHolder>(first);
        var nobody = batch.Events.FetchLatest<BadgeHolder>($"B-{Guid.NewGuid():N}");

        await batch.Execute(token);

        var holder = (await latestFirst).ShouldNotBeNull();
        holder.Id.ShouldBe(firstId);
        holder.Scans.ShouldBe(1);

        var stream = await writingSecond;
        stream.Id.ShouldBe(secondId);
        stream.StartingVersion.ShouldBe(3);
        stream.Aggregate.ShouldNotBeNull().Badge.ShouldBe(second);
        stream.Aggregate.Scans.ShouldBe(2);

        var keylessHolder = (await keyless).ShouldNotBeNull();
        keylessHolder.Badge.ShouldBeNull();
        keylessHolder.Scans.ShouldBe(1);

        var firstStream = await writingFirst;
        firstStream.Id.ShouldBe(firstId);
        firstStream.StartingVersion.ShouldBe(2);

        (await nobody).ShouldBeNull();

        stream.AppendOne(new BadgeScanned());
        await session.SaveChangesAsync(token);

        (await theSession.Events.FetchStreamStateAsync(secondId, token))!.Version.ShouldBe(4);

        // The same natural key outside a batch finds the same aggregate, identity included
        await using var query = theStore.LightweightSession();
        var unbatched = (await query.Events.FetchLatest<BadgeHolder, string>(first, token)).ShouldNotBeNull();
        unbatched.Id.ShouldBe(firstId);
        (await query.Events.FetchForWriting<BadgeHolder, string>(second, token)).Aggregate.ShouldNotBeNull()
            .Id.ShouldBe(secondId);
    }
}
