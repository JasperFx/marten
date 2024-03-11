using System;
using System.Linq;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Internal.CodeGeneration;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections.CodeGeneration;


public class ProjectionCodeGenerationApplyOrderTests
{
    [Fact]
    public void Snapshot_GeneratedCodeFile_Compiles()
    {
        var options = new StoreOptions();
        options.Connection(ConnectionSource.ConnectionString);

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

    public class SomethingBase{}

    public class SomethingCreated : SomethingAdded
    {
        public SomethingCreated(Guid id) : base(id) { }
    }

    public class SomethingAdded : SomethingBase
    {
        public SomethingAdded(Guid id)
        {
            Id = id;
        }
        public Guid Id { get; set; }
    }

    public class SomethingUpdated : SomethingBase
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

        public Something Apply(SomethingBase @event) => this;

        public Something Apply(SomethingAdded @event) => this;

        public Something Apply(SomethingAutoSynched @event) => this with { SomethingSomething = @event.SomethingSomething };

        public Something Apply(SomethingManuallySynched @event) => this with { SomethingSomething = @event.SomethingSomething };

        public Something Apply(SomethingUpdated @event) => this with { SomethingSomething = @event.SomethingSomething };



    }
}
