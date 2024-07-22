using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Internal.CodeGeneration;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_2943_generate_aggregate_generated_code_in_parallel
{
    [Fact]
    public void aggregates_do_not_fail_code_generation_on_parallel_execution()
    {
        var options = new StoreOptions();
        options.Connection(ConnectionSource.ConnectionString);

        // Given
        options.Projections.LiveStreamAggregation<Something>();

        // When
        var store = new DocumentStore(options);
        Parallel.For(1, 100, _ =>
        {
            Parallel.ForEach(store.Events.As<ICodeFileCollection>().BuildFiles().OfType<IProjectionSource>(), projection =>
            {
                projection.Build(store);
            });
        });

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
    public async Task aggregates_do_not_fail_code_generation_on_parallel_FetchForWriting_execution()
    {
        var options = new StoreOptions();
        options.Connection(ConnectionSource.ConnectionString);

        // Given
        options.Projections.LiveStreamAggregation<Something>();

        // When
        var store = new DocumentStore(options);

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync().ConfigureAwait(false);


        Parallel.For(1, 100, _ =>
        {
            store.LightweightSession().Events.FetchForWriting<Something>(Guid.NewGuid()).GetAwaiter().GetResult();
        });

        // Then
        store.Events.As<ICodeFileCollection>().BuildFiles()
            .OfType<SingleStreamProjection<Something>>()
            .ShouldHaveSingleItem();

        options.BuildFiles()
            .OfType<DocumentProviderBuilder>()
            .Where(e => e.ProviderName == typeof(Something).ToSuffixedTypeName("Provider"))
            .ShouldHaveSingleItem();
    }
}

    public record Something(Guid Id)
    {
        public int AddedCount { get; set; } = 0;
        public int InterfaceCount { get; set; } = 0;

        public string Value { get; set; }

        public static Something Create(SomethingCreated @event) => new(@event.Id);

        public Something Apply(ISomethingEvent @event) => this with { InterfaceCount = ++InterfaceCount };

        public Something Apply(SomethingEvent @event) => this;

        public Something Apply(SomethingAdded @event) => this with { AddedCount = ++AddedCount };

        public Something Apply(SomethingAutoSynched @event) => this with { Value = @event.Value };

        public Something Apply(SomethingManuallySynched @event) => this with { Value = @event.Value };

        public Something Apply(SomethingUpdated @event) => this with { Value = @event.Value };

    }


    public interface ISomethingEvent
    {
        Guid Id { get; }
    }

    public class SomethingEvent: ISomethingEvent
    {
        public Guid Id { get; }

        public SomethingEvent(Guid id)
        {
            Id = id;
        }
    }

    public class SomethingUnmappedEvent: ISomethingEvent
    {
        public Guid Id { get; }

        public SomethingUnmappedEvent(Guid id)
        {
            Id = id;
        }
    }

    public class SomethingCreated: SomethingAdded
    {
        public SomethingCreated(Guid id) : base(id) { }
    }

    public class SomethingAdded: SomethingEvent
    {
        public SomethingAdded(Guid id): base(id) { }
    }

    public class SomethingUpdated: SomethingEvent
    {
        public const string DefaultValue = "updated";

        public SomethingUpdated(Guid id, string value = DefaultValue): base(id)
        {
            Value = value;
        }

        public Guid Id { get; set; }
        public string Value { get; set; }
    }

    public class SomethingManuallySynched: SomethingUpdated
    {
        public new const string DefaultValue = "manual-sync";
        public SomethingManuallySynched(Guid id, string value = DefaultValue) : base(id, value) { }

    }

    public class SomethingAutoSynched: SomethingUpdated
    {
        public new const string DefaultValue = "auto-sync";
        public SomethingAutoSynched(Guid id, string value = DefaultValue) : base(id, value) { }
    }



