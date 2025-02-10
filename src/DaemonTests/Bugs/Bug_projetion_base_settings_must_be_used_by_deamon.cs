using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Bugs;

public class Bug_projetion_base_settings_must_be_used_by_deamon : BugIntegrationContext
{
    [Theory]
    [InlineData(0, false, typeof(EventA))]
    [InlineData(1, true, typeof(EventA))]
    [InlineData(0, false)]
    [InlineData(2, true)]
    [InlineData(0, false, typeof(EventA), typeof(EventB))]
    [InlineData(2, true, typeof(EventA), typeof(EventB))]
    public async Task projection_wrapper_honor_projectionbase_settings_and_propagates_to_deamon(int expectedEventsCount, bool includeArchived, params Type[] filteredEventType)
    {
        var eventsCount = 0;

        StoreOptions(o =>
        {
            o.Projections.Add(new FilterableProjection(includeArchived, () => eventsCount++, filteredEventType), ProjectionLifecycle.Async);
        });

        var aggId = Guid.NewGuid();
        theSession.Events.Append(aggId, new EventA(), new EventB());
        await theSession.SaveChangesAsync();
        theSession.Events.ArchiveStream(aggId);
        await theSession.SaveChangesAsync();

        var deamon = await theStore.BuildProjectionDaemonAsync();
        await deamon.StartAllAsync();
        await deamon.WaitForNonStaleData(10.Seconds());

        eventsCount.ShouldBe(expectedEventsCount);
    }

    private class EventA
    {
    }

    private class EventB
    {
    }

    internal class FilterableProjection: ProjectionBase, IProjection
    {
        private readonly Action _feedback;
        public FilterableProjection(bool includeArchivedEvents, Action feedback, params Type[] includedEventTypes)
        {
            IncludeArchivedEvents = includeArchivedEvents;
            if (includedEventTypes.Any())
            {
                IncludedEventTypes.AddRange(includedEventTypes);
            }

            _feedback = feedback;
        }

        public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
        {
            InvokeNTimes(streams.Select(s => s.Events.Count).Sum());
        }

        public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
        {
            InvokeNTimes(streams.Select(s => s.Events.Count).Sum());
            return Task.CompletedTask;
        }

        private void InvokeNTimes(int times)
        {
            for (var i = 0; i < times; i++)
            {
                _feedback.Invoke();
            }
        }
    }
}
