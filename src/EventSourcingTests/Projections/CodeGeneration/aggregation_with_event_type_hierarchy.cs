using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.CodeGeneration;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Projections.CodeGeneration;

public class aggregation_with_event_type_hierarchy: OneOffConfigurationsContext
{
    public aggregation_with_event_type_hierarchy()
    {
        StoreOptions(x =>
        {
            x.GeneratedCodeMode = TypeLoadMode.Auto;
            x.AutoCreateSchemaObjects = AutoCreate.All;

            x.Schema.For<Something>().Identity(something => something.Id);
            x.Projections.Snapshot<Something>(SnapshotLifecycle.Inline);
        });
    }

    [Fact]
    public async Task inline_snapshot_can_project_with_base_types()
    {
        var id = Guid.NewGuid();

        var stream = theSession.Events.StartStream<Something>(
            id,
            new SomethingCreated(id),
            new SomethingAdded(id),
            new SomethingUpdated(id)
        );

        await theSession.SaveChangesAsync();

        var something = theSession.Load<Something>(id);

        something.Id.ShouldBe(id);
        something.Value.ShouldBe(SomethingUpdated.DefaultValue);
        something.AddedCount.ShouldBe(1);

        theSession.Events.Append(id, new SomethingManuallySynched(id));
        await theSession.SaveChangesAsync();

        something = theSession.Load<Something>(id);
        something.Value.ShouldBe(SomethingManuallySynched.DefaultValue);

        theSession.Events.Append(id, new SomethingAutoSynched(id));
        await theSession.SaveChangesAsync();

        something = theSession.Load<Something>(id);
        something.Value.ShouldBe(SomethingAutoSynched.DefaultValue);
    }

    [Fact]
    public async Task inline_snapshot_can_project_with_interface()
    {
        var id = Guid.NewGuid();

        var stream = theSession.Events.StartStream<Something>(
            id,
            new SomethingCreated(id),
            new SomethingUpdated(id),
            new SomethingEvent(id),
            new SomethingUnmappedEvent(id)
        );

        await theSession.SaveChangesAsync();

        var something = theSession.Load<Something>(id);

        something.Id.ShouldBe(id);
        something.Value.ShouldBe(SomethingUpdated.DefaultValue);
        something.AddedCount.ShouldBe(0);
        something.InterfaceCount.ShouldBe(1);
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
}



