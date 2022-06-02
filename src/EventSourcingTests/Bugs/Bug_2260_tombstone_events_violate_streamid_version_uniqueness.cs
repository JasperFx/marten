using EventSourcingTests.Aggregation;
using Marten.Events;
using Marten.Events.Operations;
using Marten.Testing.Harness;
using Shouldly;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using static EventSourcingTests.appending_events_workflow_specs;

namespace EventSourcingTests.Bugs
{
    public class Bug_2260_tombstone_events_violate_streamid_version_uniqueness : BugIntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public Bug_2260_tombstone_events_violate_streamid_version_uniqueness(ITestOutputHelper output)
        {
            _output = output;

            theSession.Logger = new TestOutputMartenLogger(output);
        }

        [Fact]
        public async Task subsequent_tombstone_events_increment_tombstone_stream_version()
        {
            if (theStore.Events.StreamIdentity == StreamIdentity.AsGuid)
            {
                theSession.Events.Append(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent());
            }
            else
            {
                theSession.Events.Append(Guid.NewGuid().ToString(), new AEvent(), new BEvent(), new CEvent());
            }

            theSession.QueueOperation(new FailingOperation());

            await Should.ThrowAsync<DivideByZeroException>(async () =>
            {
                await theSession.SaveChangesAsync();
            });

            // Append more events that will fail, to ensure they get tombstone events appended to the event stream
            if (theStore.Events.StreamIdentity == StreamIdentity.AsGuid)
            {
                theSession.Events.Append(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent());
            }
            else
            {
                theSession.Events.Append(Guid.NewGuid().ToString(), new AEvent(), new BEvent(), new CEvent());
            }

            theSession.QueueOperation(new FailingOperation());

            await Should.ThrowAsync<DivideByZeroException>(async () =>
            {
                await theSession.SaveChangesAsync();
            });

            if (theStore.Events.StreamIdentity == StreamIdentity.AsGuid)
            {
                (await theSession.Events.FetchStreamStateAsync(EstablishTombstoneStream.StreamId)).ShouldNotBeNull();

                var events = await theSession.Events.FetchStreamAsync(EstablishTombstoneStream.StreamId);
                events.Count().ShouldBe(6);
            }
            else
            {
                (await theSession.Events.FetchStreamStateAsync(EstablishTombstoneStream.StreamKey)).ShouldNotBeNull();

                var events = await theSession.Events.FetchStreamAsync(EstablishTombstoneStream.StreamKey);
                events.Count().ShouldBe(6);
            }
        }
    }
}
