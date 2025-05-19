using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using Marten.Events;
using Marten.Exceptions;
using Marten.Services;
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
        }

        [Fact]
        public async Task append_optimistic_sad_path_because_the_stream_does_not_already_exist()
        {
            var streamId = Guid.NewGuid();
            var ex = await Should.ThrowAsync<NonExistentStreamException>(async () =>
            {
                await theSession.Events.AppendOptimistic(streamId, new AEvent(), new BEvent());
            });

            ex.Id.ShouldBe(streamId);
        }

        [Fact]
        public async Task append_optimistic_happy_path()
        {
            var streamId = Guid.NewGuid();
            theSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await theSession.SaveChangesAsync();

            await theSession.Events.AppendOptimistic(streamId, new CEvent(), new BEvent());
            await theSession.SaveChangesAsync();

            var state = await theSession.Events.FetchStreamStateAsync(streamId);
            state.Version.ShouldBe(4);
        }

        [Fact]
        public async Task append_optimistic_sad_path_with_concurrency_issue()
        {
            var streamId = Guid.NewGuid();
            await using var session1 = theStore.LightweightSession(new SessionOptions { Timeout = 1 });

            session1.Events.StartStream(streamId, new AEvent(), new BEvent());
            await session1.SaveChangesAsync();

            // Fetch the expected version
            await session1.Events.AppendOptimistic(streamId, new CEvent(), new BEvent());

            await using (var session = theStore.LightweightSession(new SessionOptions{Timeout = 1}))
            {
                session.Events.Append(streamId, new DEvent());
                await session.SaveChangesAsync();
            }

            // Should fail a concurrency check
            await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(async () =>
            {
                await session1.SaveChangesAsync();
            });
        }

        [Fact]
        public async Task append_exclusive_sad_path_because_the_stream_does_not_already_exist()
        {
            var streamId = Guid.NewGuid();
            var ex = await Should.ThrowAsync<NonExistentStreamException>(async () =>
            {
                await theSession.Events.AppendExclusive(streamId, new AEvent(), new BEvent());
            });

            ex.Id.ShouldBe(streamId);
        }

        [Fact]
        public async Task append_exclusive_happy_path()
        {
            theSession.Logger = new TestOutputMartenLogger(_output);

            var streamId = Guid.NewGuid();
            theSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await theSession.SaveChangesAsync();

            await theSession.Events.AppendExclusive(streamId, new CEvent(), new BEvent());
            await theSession.SaveChangesAsync();

            var state = await theSession.Events.FetchStreamStateAsync(streamId);
            state.Version.ShouldBe(4);
        }

        [Fact]
        public async Task append_exclusive_sad_path_with_concurrency_issue()
        {
            var streamId = Guid.NewGuid();
            theSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await theSession.SaveChangesAsync();

            // Fetch the expected version
            await theSession.Events.AppendExclusive(streamId, new CEvent(), new BEvent());

            await using (var session = theStore.LightweightSession())
            {
                session.Events.Append(streamId, new DEvent());
                var ex = await Should.ThrowAsync<MartenCommandException>(async () =>
                {
                    await session.SaveChangesAsync();
                });

                ex.Message.ShouldContain(MartenCommandException.MaybeLockedRowsMessage);
            }
        }

        [Fact]
        public async Task append_exclusive_sad_path_with_concurrency_issue_2()
        {
            var streamId = Guid.NewGuid();
            theSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await theSession.SaveChangesAsync();

            // Fetch the expected version
            await theSession.Events.AppendExclusive(streamId, new CEvent(), new BEvent());

            await using (var session = theStore.LightweightSession(new SessionOptions{Timeout = 1}))
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
        }

        [Fact]
        public async Task append_optimistic_sad_path_because_the_stream_does_not_already_exist()
        {
            var streamId = Guid.NewGuid().ToString();
            var ex = await Should.ThrowAsync<NonExistentStreamException>(async () =>
            {
                await theSession.Events.AppendOptimistic(streamId, new AEvent(), new BEvent());
            });

            ex.Id.ShouldBe(streamId);
        }

        [Fact]
        public async Task append_optimistic_happy_path()
        {
            var streamId = Guid.NewGuid().ToString();
            theSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await theSession.SaveChangesAsync();

            await theSession.Events.AppendOptimistic(streamId, new CEvent(), new BEvent());
            await theSession.SaveChangesAsync();

            var state = await theSession.Events.FetchStreamStateAsync(streamId);
            state.Version.ShouldBe(4);
        }

        [Fact]
        public async Task append_optimistic_sad_path_with_concurrency_issue()
        {
            var streamId = Guid.NewGuid().ToString();
            theSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await theSession.SaveChangesAsync();

            // Fetch the expected version
            await theSession.Events.AppendOptimistic(streamId, new CEvent(), new BEvent());

            await using (var session = theStore.LightweightSession())
            {
                session.Events.Append(streamId, new DEvent());
                await session.SaveChangesAsync();
            }

            // Should fail a concurrency check
            await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(async () =>
            {
                await theSession.SaveChangesAsync();
            });
        }

        [Fact]
        public async Task append_exclusive_sad_path_because_the_stream_does_not_already_exist()
        {
            var streamId = Guid.NewGuid().ToString();
            var ex = await Should.ThrowAsync<NonExistentStreamException>(async () =>
            {
                await theSession.Events.AppendExclusive(streamId, new AEvent(), new BEvent());
            });

            ex.Id.ShouldBe(streamId);
        }

        [Fact]
        public async Task append_exclusive_happy_path()
        {
            var streamId = Guid.NewGuid().ToString();
            theSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await theSession.SaveChangesAsync();

            await theSession.Events.AppendExclusive(streamId, new CEvent(), new BEvent());
            await theSession.SaveChangesAsync();

            var state = await theSession.Events.FetchStreamStateAsync(streamId);
            state.Version.ShouldBe(4);
        }

        [Fact]
        public async Task append_exclusive_sad_path_with_concurrency_issue()
        {
            var streamId = Guid.NewGuid().ToString();
            theSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await theSession.SaveChangesAsync();

            // Fetch the expected version
            await theSession.Events.AppendExclusive(streamId, new CEvent(), new BEvent());

            await using (var session = theStore.LightweightSession(new SessionOptions{Timeout = 1}))
            {
                session.Events.Append(streamId, new DEvent());
                var ex = await Should.ThrowAsync<MartenCommandException>(async () =>
                {
                    await session.SaveChangesAsync();
                });

                ex.Message.ShouldContain(MartenCommandException.MaybeLockedRowsMessage);
            }
        }

        [Fact]
        public async Task append_exclusive_sad_path_with_concurrency_issue_2()
        {
            var streamId = Guid.NewGuid().ToString();
            theSession.Events.StartStream(streamId, new AEvent(), new BEvent());
            await theSession.SaveChangesAsync();

            // Fetch the expected version
            await theSession.Events.AppendExclusive(streamId, new CEvent(), new BEvent());

            await using (var session = theStore.LightweightSession(new SessionOptions{Timeout = 1}))
            {
                await Should.ThrowAsync<StreamLockedException>(async () =>
                {
                    await session.Events.AppendExclusive(streamId, new DEvent());
                });
            }
        }
    }
}
