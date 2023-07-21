using System;
using System.Linq;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Internal.CodeGeneration;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections.CodeGeneration;

public class ProjectionCodeGenerationTests
{
    [Fact]
    public void Snapshot_GeneratesCodeFiles()
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

        options.BuildFiles()
            .OfType<DocumentProviderBuilder>()
            .Where(e => e.ProviderName == typeof(Something).ToSuffixedTypeName("Provider"))
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void LiveStreamAggregation_GeneratesCodeFiles()
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

        options.BuildFiles()
            .OfType<DocumentProviderBuilder>()
            .Where(e => e.ProviderName == typeof(Something).ToSuffixedTypeName("Provider"))
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void SingleStreamProjection_GeneratesCodeFiles()
    {
        var options = new StoreOptions();
        options.Connection("Dummy");

        // Given
        options.Projections.Add<SomethingElseSingleStreamProjection>(ProjectionLifecycle.Inline);

        // When
        var store = new DocumentStore(options);

        // Then
        store.Events.As<ICodeFileCollection>().BuildFiles()
            .OfType<SingleStreamProjection<SomethingElse>>()
            .ShouldHaveSingleItem();

        options.BuildFiles()
            .OfType<DocumentProviderBuilder>()
            .Where(e => e.ProviderName == typeof(SomethingElse).ToSuffixedTypeName("Provider"))
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void MultiStreamProjection_GeneratesCodeFiles()
    {
        var options = new StoreOptions();
        options.Connection("Dummy");

        // Given
        options.Projections.Add<SomethingElseMultiStreamProjection>(ProjectionLifecycle.Inline);

        // When
        var store = new DocumentStore(options);

        // Then
        store.Events.As<ICodeFileCollection>().BuildFiles()
            .OfType<MultiStreamProjection<SomethingElse, Guid>>()
            .ShouldHaveSingleItem();

        options.BuildFiles()
            .OfType<DocumentProviderBuilder>()
            .Where(e => e.ProviderName == typeof(SomethingElse).ToSuffixedTypeName("Provider"))
            .ShouldHaveSingleItem();
    }

    public record SomethingHasHappened(Guid SomethingId, string SomethingSomething);

    public record SomethingElseHasHappened(Guid SomethingId, string SomethingSomething);

    public record Something(Guid Id, string SomethingSomething)
    {
        public static Something Create(SomethingHasHappened @event) =>
            new Something(@event.SomethingId, @event.SomethingSomething);

        public Something Apply(SomethingElseHasHappened @event) =>
            this with { SomethingSomething = @event.SomethingSomething };
    }

    public record SomethingElse(Guid Id, string SomethingSomething)
    {
        public static SomethingElse Create(SomethingHasHappened @event) =>
            new SomethingElse(@event.SomethingId, @event.SomethingSomething);

        public SomethingElse Apply(SomethingElseHasHappened @event) =>
            this with { SomethingSomething = @event.SomethingSomething };
    }

    public class SomethingElseSingleStreamProjection: SingleStreamProjection<SomethingElse>
    {
    }

    public class SomethingElseMultiStreamProjection: MultiStreamProjection<SomethingElse, Guid>
    {
        public SomethingElseMultiStreamProjection()
        {
            Identity<SomethingHasHappened>(e => e.SomethingId);
            Identity<SomethingHasHappened>(e => e.SomethingId);
        }
    }
}
