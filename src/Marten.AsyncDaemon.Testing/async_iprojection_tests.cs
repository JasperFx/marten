using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Util;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing
{
    public record UserAdded(Guid UserId);

    public record CompanyAdded(Guid CompanyId);

    public interface IMartenEventsConsumer
    {
        Task ConsumeAsync(IReadOnlyList<StreamAction> streamActions);
    }

    public class MartenEventsConsumer: IMartenEventsConsumer
    {
        public static List<object> Events { get; } = new();

        public Task ConsumeAsync(IReadOnlyList<StreamAction> streamActions)
        {
            foreach (var @event in streamActions.SelectMany(streamAction => streamAction.Events))
            {
                Events.Add(@event);
                Console.WriteLine($"{@event.Sequence} - {@event.EventTypeName}");
            }

            return Task.CompletedTask;
        }
    }

    public class MartenSubscription: IProjection
    {
        private readonly IMartenEventsConsumer consumer;

        public MartenSubscription(IMartenEventsConsumer consumer)
        {
            this.consumer = consumer;
        }

        public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
        {
            using (NoSynchronizationContextScope.Enter())
            {
                consumer.ConsumeAsync(streams).Wait();
            }
        }

        public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
            CancellationToken cancellation)
        {
            return consumer.ConsumeAsync(streams);
        }
    }

    public class async_iprojection_tests: DaemonContext
    {
        public async_iprojection_tests(ITestOutputHelper output): base(output)
        {
        }

        [Fact]
        public async Task AsyncDaemon_Should_PublishEvents_ToMartenSubscription()
        {
            StoreOptions(x =>
                x.Projections.Add(
                    new MartenSubscription(new MartenEventsConsumer()),
                    ProjectionLifecycle.Async,
                    "customConsumer"
                )
            );

            using var daemon = await StartDaemon();
            await daemon.StartAllShards();

            for (var i = 0; i < 10; i++)
            {
                var userId = Guid.NewGuid();
                theSession.Events.Append(userId, new UserAdded(userId));
                var companyId = Guid.NewGuid();
                theSession.Events.Append(companyId, new CompanyAdded(companyId));

                await theSession.SaveChangesAsync();
            }

            await daemon.Tracker.WaitForHighWaterMark(20, 30.Seconds());
            await daemon.Tracker.WaitForShardState("customConsumer:All", 20, 30.Seconds());

            daemon.Tracker.HighWaterMark.ShouldBe(20);

            await daemon.StopAll();

            MartenEventsConsumer.Events.Count.ShouldBe(20);
        }
    }
}
