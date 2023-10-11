using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Lamar.IoC.Instances;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Internal.CodeGeneration;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections.CodeGeneration;


public class ProjectionCodeGenerationApplyOrderTests
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
        var projectionCodeFile = store.Events.As<ICodeFileCollection>().BuildFiles()
            .OfType<SingleStreamProjection<Something>>().FirstOrDefault();

        projectionCodeFile.ShouldNotBeNull();
        projectionCodeFile.Compile(options);

        var provider = options
            .BuildFiles()
            .OfType<DocumentProviderBuilder>()
            .SingleOrDefault(e => e.ProviderName == typeof(Something).ToSuffixedTypeName("Provider"));

        provider.ShouldNotBeNull();
    }

    public class SomethingCreated
    {
        public SomethingCreated(Guid id)
        {
            Id = id;
        }
        public Guid Id { get; set; }
    }

    public class SomethingAdded
    {
        public SomethingAdded(Guid id)
        {
            Id = id;
        }
        public Guid Id { get; set; }
    }

    public class SomethingUpdated
    {
        public SomethingUpdated(Guid id, string somethingSomething)
        {
            Id = id;
            SomethingSomething = somethingSomething;
        }

        public Guid Id { get; set; }
        public string SomethingSomething { get; set; }
    }

    public class SomethingManuallySynched: SomethingUpdated
    {
        public SomethingManuallySynched(Guid id, string somethingSomething) : base(id, somethingSomething) { }

    }

    public class SomethingAutoSynched: SomethingUpdated
    {
        public SomethingAutoSynched(Guid id, string somethingSomething) : base(id, somethingSomething) { }
    }

    public record Something(Guid Id)
    {
        public string SomethingSomething { get; set; } 

        public static Something Create(SomethingCreated @event) => new(@event.Id);

        public Something Apply(SomethingAdded @event) => this;

        public Something Apply(SomethingAutoSynched @event) => this with { SomethingSomething = @event.SomethingSomething };

        public Something Apply(SomethingManuallySynched @event) => this with { SomethingSomething = @event.SomethingSomething };

        public Something Apply(SomethingUpdated @event) => this with { SomethingSomething = @event.SomethingSomething };

        

    }
}
