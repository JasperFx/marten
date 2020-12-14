using System;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    [Collection("events")]
    public class cannot_start_a_stream_with_zero_events : OneOffConfigurationsContext
    {
        public cannot_start_a_stream_with_zero_events() : base("events")
        {
        }

        public class IssueCreated
        {
            public Guid IssueId { get; set; }
            public string Description { get; set; }
        }

        public class IssueUpdated
        {
            public Guid IssueId { get; set; }
            public string Description { get; set; }
        }

        public class IssuesList { }


        [Fact]
        public void should_be_unable_to_start_a_stream_without_any_events_as_guid()
        {
            StoreOptions(x => x.Events.StreamIdentity = StreamIdentity.AsGuid);

            var @event = new IssueCreated { IssueId = Guid.NewGuid(), Description = "Description" };

            var ex = Should.Throw<EmptyEventStreamException>(() =>
            {
                var streamId = theSession.Events.StartStream<IssuesList>(@event.IssueId);
            });

            ex.Message.ShouldContain("cannot be started without any events", Case.Insensitive);
        }

        [Fact]
        public void should_be_unable_to_start_a_stream_without_any_events_as_string()
        {
            StoreOptions(x => x.Events.StreamIdentity = StreamIdentity.AsString);

            var @event = new IssueCreated { IssueId = Guid.NewGuid(), Description = "Description" };

            var ex = Should.Throw<EmptyEventStreamException>(() =>
            {
                var streamId = theSession.Events.StartStream<IssuesList>(@event.IssueId.ToString());
            });

            ex.Message.ShouldContain("cannot be started without any events", Case.Insensitive);
        }

        [Fact]
        public async Task Bug_1388_cannot_start_the_same_stream_twice_with_the_same_session()
        {
            StoreOptions(x => x.Events.StreamIdentity = StreamIdentity.AsString);

            var @event = new IssueCreated { IssueId = Guid.NewGuid(), Description = "Description" };

            theSession.Events.StartStream<IssuesList>(@event.IssueId.ToString(), @event);
            await theSession.SaveChangesAsync();

            var ex = await Should.ThrowAsync<ExistingStreamIdCollisionException>(async () =>
            {
                // Do it again
                theSession.Events.StartStream<IssuesList>(@event.IssueId.ToString(), @event);
                await theSession.SaveChangesAsync();
            });

        }
    }
}
