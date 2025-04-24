using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Projections;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Bugs;

public class document_and_event_operations_within_page : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public document_and_event_operations_within_page(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task failure_due_to_ordering_change()
    {
        theStore.Options.Events.MetadataConfig.HeadersEnabled = true;
        theStore.Options.Events.TenancyStyle = TenancyStyle.Conjoined;
        theStore.Options.Events.StreamIdentity = StreamIdentity.AsGuid;

        theStore.Options.Projections.Add<SamplesRolledUpProjection>(ProjectionLifecycle.Inline);
        theStore.Options.Projections.Add<SampleProjection>(ProjectionLifecycle.Inline);
        theStore.Options.Projections.Add<SampleEventProjection>(ProjectionLifecycle.Inline);


        using var session = theStore.LightweightSession("tenant");
        session.Logger = new TestOutputMartenLogger(_output);

        var mrCreated = new SamplesRolledUpCreated(Guid.NewGuid());
        session.Events.StartStream(mrCreated.Id, mrCreated);
        await session.SaveChangesAsync();

        for (var count = 1; count <= 2; count++)
        {
            var sampleAdded = new SampleAdded(mrCreated.Id, 23);
            session.Events.Append(sampleAdded.Id, sampleAdded);
            await session.SaveChangesAsync();
        }

        session.Events.Append(mrCreated.Id, new SamplesRolledUpPublished(mrCreated.Id));
        await session.SaveChangesAsync();
    }
}


public record SamplesRolledUp(Guid Id, List<Guid> Samples, bool Published);


public record SamplesRolledUpCreated(Guid Id);

public record SamplesRolledUpPublished(Guid Id);

public class SamplesRolledUpProjection: SingleStreamProjection<SamplesRolledUp, Guid>
{
    public SamplesRolledUpProjection()
    {
        CreateEvent<SamplesRolledUpCreated>(x => new SamplesRolledUp(x.Id, new List<Guid>(), false));
        ProjectEvent<SampleAdded>((x, y) =>
        {
            var existing = x.Samples;
            existing.Add(y.Id);
            return x with { Samples = existing };
        });
        ProjectEvent<SamplesRolledUpPublished>(x => x with { Published = true });
    }
}


public record SampleView(Guid Id, int Value, bool Published);

public record SampleAdded(Guid Id, int Value);

public class SampleProjection : MultiStreamProjection<SampleView, Guid>
{
    public SampleProjection()
    {
        CustomGrouping(new SampleGrouper());
        Identity<SampleAdded>(x => x.Id);

        CreateEvent<SampleAdded>(x=> new SampleView(x.Id, x.Value, false));

        ProjectEvent<SamplesRolledUpPublished>(x=> x with { Published = true });

    }
}

public sealed class SampleGrouper: IAggregateGrouper<Guid>
{
    public async Task Group(IQuerySession session, IEnumerable<IEvent> events, IEventGrouping<Guid> grouping)
    {
        var publishedEvents = events.OfType<IEvent<SamplesRolledUpPublished>>().ToArray();

        foreach (var published in publishedEvents)
        {
            var sample =
                await session.Events.AggregateStreamAsync<SamplesRolledUp>(published.Data.Id, published.Version);

            foreach (var sampleEvent in sample!.Samples)
            {
                grouping.AddEvents(sampleEvent, publishedEvents);
            }
        }
    }
}

public record SampleEventView(Guid Id);

public class SampleEventProjection: EventProjection
{
    public SampleEventProjection()
    {
        Project<SampleAdded>(((added, operations) =>
        {
            operations.Store(new SampleEventView(added.Id));
        }));

        ProjectAsync<SamplesRolledUpPublished>(((async (published,operations, cancellation) =>
        {
            var rolledUp = await operations.Events.AggregateStreamAsync<SamplesRolledUp>(published.Id, token:cancellation);
            foreach (var rolledUpSample in rolledUp.Samples)
            {
                operations.Store(new SampleEventView(rolledUpSample));
            }
        })));
    }
}




