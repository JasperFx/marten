using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Bugs;

public class Bug_projection_wrapper_should_honor_projetionbase_settings : BugIntegrationContext
{
    [Theory]
    [InlineData(false, typeof(EventA))]
    [InlineData(true, typeof(EventA))]
    [InlineData(false, null)]
    [InlineData(true, null)]
    public void projection_wrapper_honor_projectionbase_settings(bool includeArchived, Type? filteredEventType)
    {
        Type[] filteredTypeArray = filteredEventType is null ? [] : [filteredEventType];

        var projection = new FilterableProjection(includeArchived, filteredTypeArray);
        var wrapper = new ProjectionWrapper(projection, ProjectionLifecycle.Async);
        var shard = (wrapper as IProjectionSource).AsyncProjectionShards(theStore);

        shard.First().IncludeArchivedEvents.ShouldBe(includeArchived);
        shard.First().EventTypes.ShouldBe(filteredTypeArray);
    }

    private class EventA
    {
        public Guid Id { get; set; }
    }

    internal class FilterableProjection: ProjectionBase, IProjection
    {
        public FilterableProjection(bool includeArchivedEvents, params Type[] includedEventTypes)
        {
            IncludeArchivedEvents = includeArchivedEvents;
            if (includedEventTypes.Any())
            {
                IncludedEventTypes.AddRange(includedEventTypes);
            }
        }

        public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
        {
        }

        public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
        {
            return Task.CompletedTask;
        }
    }
}
