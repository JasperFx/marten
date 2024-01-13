using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using Marten.Events;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests;

public class append_events_with_optimistic_or_exclusive_locks
{
    public class append_events_optimistic_or_exclusive_with_guid_identity: OneOffConfigurationsContext
    {
        private readonly ITestOutputHelper _output;

        public append_events_optimistic_or_exclusive_with_guid_identity(ITestOutputHelper output)
        {
            _output = output;
            theStore.Advanced.Clean.DeleteAllEventData();
        }

        [Fact]
        public async Task append_optimistic_sad_path_because_the_stream_does_not_already_exist()
        {
            var streamId = Guid.NewGuid();
            var ex = await Should.ThrowAsync<NonExistentStreamException>(async () =>
            {
                await TheSession.Events.AppendOptimistic(streamId, new AEvent(), new BEvent());
            });

            ex.Id.ShouldBe(streamId);
        }

        [Fact]
        public async Task append_optimistic_happy_path()
        {
            var streamId = Guid.NewGuid();
            TheSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await TheSession.SaveChangesAsync();

            await TheSession.Events.AppendOptimistic(streamId, new CEvent(), new BEvent());
            await TheSession.SaveChangesAsync();

            var state = await TheSession.Events.FetchStreamStateAsync(streamId);
            state.Version.ShouldBe(4);
        }

        [Fact]
        public async Task append_optimistic_sad_path_with_concurrency_issue()
        {
            var streamId = Guid.NewGuid();
            TheSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await TheSession.SaveChangesAsync();

            // Fetch the expected version
            await TheSession.Events.AppendOptimistic(streamId, new CEvent(), new BEvent());

            await using (var session = theStore.LightweightSession())
            {
                session.Events.Append(streamId, new DEvent());
                await session.SaveChangesAsync();
            }

            // Should fail a concurrency check
            await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(async () =>
            {
                await TheSession.SaveChangesAsync();
            });
        }

        [Fact]
        public async Task append_exclusive_sad_path_because_the_stream_does_not_already_exist()
        {
            var streamId = Guid.NewGuid();
            var ex = await Should.ThrowAsync<NonExistentStreamException>(async () =>
            {
                await TheSession.Events.AppendExclusive(streamId, new AEvent(), new BEvent());
            });

            ex.Id.ShouldBe(streamId);
        }

        [Fact]
        public async Task append_exclusive_happy_path()
        {
            TheSession.Logger = new TestOutputMartenLogger(_output);

            var streamId = Guid.NewGuid();
            TheSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await TheSession.SaveChangesAsync();

            await TheSession.Events.AppendExclusive(streamId, new CEvent(), new BEvent());
            await TheSession.SaveChangesAsync();

            var state = await TheSession.Events.FetchStreamStateAsync(streamId);
            state.Version.ShouldBe(4);
        }

        [Fact]
        public async Task append_exclusive_sad_path_with_concurrency_issue()
        {
            var streamId = Guid.NewGuid();
            TheSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await TheSession.SaveChangesAsync();

            // Fetch the expected version
            await TheSession.Events.AppendExclusive(streamId, new CEvent(), new BEvent());

            await using (var session = theStore.LightweightSession())
            {
                session.Events.Append(streamId, new DEvent());
                var ex = await Should.ThrowAsync<MartenCommandException>(async () =>
                {
                    await session.SaveChangesAsync();
                });

                ex.Message.ShouldContain(MartenCommandException.MaybeLockedRowsMessage,
                    StringComparisonOption.Default);
            }
        }

        [Fact]
        public async Task append_exclusive_sad_path_with_concurrency_issue_2()
        {
            var streamId = Guid.NewGuid();
            TheSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await TheSession.SaveChangesAsync();

            // Fetch the expected version
            await TheSession.Events.AppendExclusive(streamId, new CEvent(), new BEvent());

            await using (var session = theStore.LightweightSession())
            {
                await Should.ThrowAsync<StreamLockedException>(async () =>
                {
                    await session.Events.AppendExclusive(streamId, new DEvent());
                });
            }
        }
    }


    public class append_events_optimistic_or_exclusive_with_string_identity: OneOffConfigurationsContext
    {
        public append_events_optimistic_or_exclusive_with_string_identity()
        {
            StoreOptions(x => x.Events.StreamIdentity = StreamIdentity.AsString);
            theStore.Advanced.Clean.DeleteAllEventData();
        }

        [Fact]
        public async Task append_optimistic_sad_path_because_the_stream_does_not_already_exist()
        {
            var streamId = Guid.NewGuid().ToString();
            var ex = await Should.ThrowAsync<NonExistentStreamException>(async () =>
            {
                await TheSession.Events.AppendOptimistic(streamId, new AEvent(), new BEvent());
            });

            ex.Id.ShouldBe(streamId);
        }

        [Fact]
        public async Task append_optimistic_happy_path()
        {
            var streamId = Guid.NewGuid().ToString();
            TheSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await TheSession.SaveChangesAsync();

            await TheSession.Events.AppendOptimistic(streamId, new CEvent(), new BEvent());
            await TheSession.SaveChangesAsync();

            var state = await TheSession.Events.FetchStreamStateAsync(streamId);
            state.Version.ShouldBe(4);
        }

        [Fact]
        public async Task append_optimistic_sad_path_with_concurrency_issue()
        {
            var streamId = Guid.NewGuid().ToString();
            TheSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await TheSession.SaveChangesAsync();

            // Fetch the expected version
            await TheSession.Events.AppendOptimistic(streamId, new CEvent(), new BEvent());

            await using (var session = theStore.LightweightSession())
            {
                session.Events.Append(streamId, new DEvent());
                await session.SaveChangesAsync();
            }

            // Should fail a concurrency check
            await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(async () =>
            {
                await TheSession.SaveChangesAsync();
            });
        }

        [Fact]
        public async Task append_exclusive_sad_path_because_the_stream_does_not_already_exist()
        {
            var streamId = Guid.NewGuid().ToString();
            var ex = await Should.ThrowAsync<NonExistentStreamException>(async () =>
            {
                await TheSession.Events.AppendExclusive(streamId, new AEvent(), new BEvent());
            });

            ex.Id.ShouldBe(streamId);
        }

        [Fact]
        public async Task append_exclusive_happy_path()
        {
            var streamId = Guid.NewGuid().ToString();
            TheSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await TheSession.SaveChangesAsync();

            await TheSession.Events.AppendExclusive(streamId, new CEvent(), new BEvent());
            await TheSession.SaveChangesAsync();

            var state = await TheSession.Events.FetchStreamStateAsync(streamId);
            state.Version.ShouldBe(4);
        }

        [Fact]
        public async Task append_exclusive_sad_path_with_concurrency_issue()
        {
            var streamId = Guid.NewGuid().ToString();
            TheSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await TheSession.SaveChangesAsync();

            // Fetch the expected version
            await TheSession.Events.AppendExclusive(streamId, new CEvent(), new BEvent());

            await using (var session = theStore.LightweightSession())
            {
                session.Events.Append(streamId, new DEvent());
                var ex = await Should.ThrowAsync<MartenCommandException>(async () =>
                {
                    await session.SaveChangesAsync();
                });

                ex.Message.ShouldContain(MartenCommandException.MaybeLockedRowsMessage,
                    StringComparisonOption.Default);
            }
        }

        [Fact]
        public async Task append_exclusive_sad_path_with_concurrency_issue_2()
        {
            var streamId = Guid.NewGuid().ToString();
            TheSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await TheSession.SaveChangesAsync();

            // Fetch the expected version
            await TheSession.Events.AppendExclusive(streamId, new CEvent(), new BEvent());

            await using (var session = theStore.LightweightSession())
            {
                await Should.ThrowAsync<StreamLockedException>(async () =>
                {
                    await session.Events.AppendExclusive(streamId, new DEvent());
                });
            }
        }
    }
}
