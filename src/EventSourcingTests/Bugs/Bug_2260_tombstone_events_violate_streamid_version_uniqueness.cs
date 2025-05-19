using EventSourcingTests.Aggregation;
using Marten.Events;
using Marten.Events.Operations;
using Marten.Testing.Harness;
using Shouldly;
using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Xunit;
using Xunit.Abstractions;
using static EventSourcingTests.appending_events_workflow_specs;

namespace EventSourcingTests.Bugs;

public class Bug_2260_tombstone_events_violate_streamid_version_uniqueness : IntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_2260_tombstone_events_violate_streamid_version_uniqueness(ITestOutputHelper output, DefaultStoreFixture fixture) : base(fixture)
    {
        _output = output;
    }

    [Theory]
    [InlineData(StreamIdentity.AsGuid)]
    [InlineData(StreamIdentity.AsString)]
    public async Task subsequent_tombstone_events_increment_tombstone_stream_version(StreamIdentity identity)
    {
        UseStreamIdentity(identity);


        if (theStore.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            theSession.Events.Append(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent());
        }
        else
        {
            theSession.Events.Append(Guid.NewGuid().ToString(), new AEvent(), new BEvent(), new CEvent());
        }

        theSession.QueueOperation(new FailingOperation());

        theSession.Logger = new TestOutputMartenLogger(_output);

        await Should.ThrowAsync<DivideByZeroException>(async () =>
        {
            await theSession.SaveChangesAsync();
        });

        await using var session = theStore.LightweightSession();

        // Append more events that will fail, to ensure they get tombstone events appended to the event stream
        if (theStore.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            session.Events.Append(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent());
        }
        else
        {
            session.Events.Append(Guid.NewGuid().ToString(), new AEvent(), new BEvent(), new CEvent());
        }

        session.QueueOperation(new FailingOperation());

        await Should.ThrowAsync<DivideByZeroException>(async () =>
        {
            await session.SaveChangesAsync();
        });

    }
}