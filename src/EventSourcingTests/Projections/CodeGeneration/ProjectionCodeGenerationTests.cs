using System;
using System.Linq;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections.CodeGeneration;

public class ProjectionCodeGenerationTests
{
    [Fact]
    public void Snapshot_GeneratesCodeFile()
    {
        var options = new StoreOptions();
        options.Connection("Dummy");

        // Given
        options.Projections.Snapshot<Something>(SnapshotLifecycle.Inline);

        // When
        var store = new DocumentStore(options);

        // Then
        store.Events.As<ICodeFileCollection>().BuildFiles()
            .OfType<SingleStreamProjection<Something>>()
            .ShouldHaveSingleItem();
    }


    [Fact]
    public void LiveStreamAggregation_GeneratesCodeFile()
    {
        var options = new StoreOptions();
        options.Connection("Dummy");

        // Given
        options.Projections.LiveStreamAggregation<Something>();

        // When
        var store = new DocumentStore(options);

        // Then
        store.Events.As<ICodeFileCollection>().BuildFiles()
            .OfType<SingleStreamProjection<Something>>()
            .ShouldHaveSingleItem();
    }

    public record SomethingHappened(Guid SomethingId, string SomethingSomething);

    public record SomethingDifferentHappened(Guid SomethingId, string SomethingSomething);

    public record Something(Guid Id, string SomethingSomething)
    {
        public static Something Create(SomethingHappened @event) =>
            new Something(@event.SomethingId, @event.SomethingSomething);

        public Something Apply(SomethingHappened @event) =>
            this with { SomethingSomething = @event.SomethingSomething };
    }
}
