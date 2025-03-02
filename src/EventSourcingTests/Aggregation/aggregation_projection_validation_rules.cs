using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Internals;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.CodeGeneration;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class aggregation_projection_validation_rules
{
    protected string errorMessageFor(Action<StoreOptions> configure)
    {
        var ex = Should.Throw<InvalidProjectionException>(() =>
        {
            DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                configure(opts);
            });
        });

        return ex.Message;
    }

    protected void shouldNotThrow(Action<StoreOptions> configure)
    {
        Should.NotThrow(() =>
        {
            DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                configure(opts);
            });
        });
    }

    [Fact]
    public void aggregate_id_is_wrong_type_1()
    {
        var message = errorMessageFor(x =>
        {
            x.Events.StreamIdentity = StreamIdentity.AsGuid;
            x.Projections.Snapshot<StringIdentifiedAggregate>(SnapshotLifecycle.Async);
        });

        message.ShouldContain(
            $"Id type mismatch");
    }


    [Fact]
    public void aggregate_id_is_wrong_type_2()
    {
        errorMessageFor(x =>
        {
            x.Events.StreamIdentity = StreamIdentity.AsString;
            x.Projections.Snapshot<GuidIdentifiedAggregate>(SnapshotLifecycle.Async);
        }).ShouldContain("Id type mismatch");
    }

    [Fact]
    public void if_events_are_multi_tenanted_and_global_projections_are_disabled_so_must_the_projected_view()
    {
        errorMessageFor(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.Snapshot<GuidIdentifiedAggregate>(SnapshotLifecycle.Async);
        }).ShouldContain(
            $"Tenancy storage style mismatch between the events (Conjoined) and the aggregate type {typeof(GuidIdentifiedAggregate).FullNameInCode()} (Single)");
    }

    [Fact]
    public void if_events_are_multi_tenanted_and_global_projections_are_enabled()
    {
        shouldNotThrow(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;

            #region sample_enabling_global_projections_for_conjoined_tenancy

            opts.Events.EnableGlobalProjectionsForConjoinedTenancy = true;

            #endregion

            opts.Projections.Snapshot<GuidIdentifiedAggregate>(SnapshotLifecycle.Async);
        });
    }

    [Fact]
    public void if_the_aggregate_is_multi_tenanted_but_the_events_are_not()
    {
        errorMessageFor(opts =>
        {
            opts.Projections.Snapshot<GuidIdentifiedAggregate>(SnapshotLifecycle.Async);
            opts.Schema.For<GuidIdentifiedAggregate>().MultiTenanted();
        }).ShouldContain(
            $"Tenancy storage style mismatch between the events (Single) and the aggregate type {typeof(GuidIdentifiedAggregate).FullNameInCode()} (Conjoined)");
    }

    [Fact]
    public void validation_errors_on_empty_aggregation()
    {
        errorMessageFor(opts =>
        {
            opts.Projections.Add(new Projections.EmptyProjection(), ProjectionLifecycle.Inline);
        }).ShouldNotBeNull();
    }

    public class EmptyProjection: SingleStreamProjection<GuidIdentifiedAggregate, Guid>
    {
    }


    public class GuidIdentifiedAggregate
    {
        public Guid Id { get; set; }

        public void Apply(AEvent a)
        {
        }
    }

    public class StringIdentifiedAggregate
    {
        public string Id { get; set; }

        public void Apply(AEvent a)
        {
        }
    }


    [Fact]
    public void happy_path_validation_for_aggregation()
    {
        var projection = new AllGood();
        projection.AssembleAndAssertValidity();
    }

    [Fact]
    public void blow_on_soft_deleted_aggregates()
    {
        var projection = new AllGood();
        var options = new StoreOptions();
        options.Schema.For<MyAggregate>().SoftDeleted();
        throw new NotImplementedException();
        // var errors = projection.ValidateConfiguration(options).ToArray();
        // errors.Single().ShouldBe("AggregateProjection cannot support aggregates that are soft-deleted");
    }

    [Fact]
    public void find_bad_method_names_that_are_not_ignored()
    {
        var projection = new BadMethodName();
        var ex = Should.Throw<InvalidProjectionException>(() => projection.AssembleAndAssertValidity());

        ex.Message.ShouldContain(
            "Unrecognized method name 'DoStuff'. Either mark with [MartenIgnore] or use one of 'Apply', 'Create', 'ShouldDelete'");
    }

    [Fact]
    public void find_invalid_argument_type()
    {
        var projection = new InvalidArgumentType();
        var ex = Should.Throw<InvalidProjectionException>(() => projection.AssembleAndAssertValidity());
        ex.InvalidMethods.Single()
            .Errors
            .ShouldContain(
                $"Parameter of type 'Marten.IDocumentOperations' is not supported. Valid options are System.Threading.CancellationToken, Marten.IQuerySession, {typeof(MyAggregate).FullNameInCode()}, {typeof(AEvent).FullNameInCode()}, JasperFx.Events.IEvent, JasperFx.Events.IEvent<{typeof(AEvent).FullNameInCode()}>");
    }

    [Fact]
    public void missing_event_altogether()
    {
        var projection = new MissingEventType1();
        var ex = Should.Throw<InvalidProjectionException>(() => projection.AssembleAndAssertValidity());
        ex.InvalidMethods.Single()
            .Errors.ShouldContain(MethodSlot.NoEventType);
    }

    [Fact]
    public void marten_can_guess_the_event_based_on_what_is_left()
    {
        var projection = new CanGuessEventType();
        projection.AssembleAndAssertValidity();
    }

    [Fact]
    public void invalid_return_type()
    {
        var projection = new BadReturnType();
        var ex = Should.Throw<InvalidProjectionException>(() => projection.AssembleAndAssertValidity());
        ex.InvalidMethods.Single()
            .Errors.ShouldContain(
                $"Parameter of type 'Marten.IDocumentOperations' is not supported. Valid options are System.Threading.CancellationToken, Marten.IQuerySession, {typeof(MyAggregate).FullNameInCode()}, {typeof(AEvent).FullNameInCode()}, JasperFx.Events.IEvent, JasperFx.Events.IEvent<{typeof(AEvent).FullNameInCode()}>",
                "Return type 'string' is invalid. The valid options are System.Threading.CancellationToken, Marten.IQuerySession, Marten.Testing.Events.Aggregation.MyAggregate");
    }

    [Fact]
    public void missing_required_parameter()
    {
        var projection = new MissingMandatoryType();
        var ex = Should.Throw<InvalidProjectionException>(() => projection.AssembleAndAssertValidity());

        ex.InvalidMethods.Single()
            .Errors
            .ShouldContain($"Aggregate type '{typeof(MyAggregate).FullNameInCode()}' is required as a parameter");
    }
}

public class MissingMandatoryType: SingleStreamProjection<MyAggregate, Guid>
{
    public void Apply(AEvent @event)
    {
    }
}

public class BadReturnType: SingleStreamProjection<MyAggregate, Guid>
{
    public string Apply(AEvent @event, MyAggregate aggregate, IDocumentOperations operations)
    {
        return null;
    }
}

public class MissingEventType1: SingleStreamProjection<MyAggregate, Guid>
{
    public void Apply(MyAggregate aggregate, IDocumentOperations operations)
    {
    }
}

public class CanGuessEventType: SingleStreamProjection<MyAggregate, Guid>
{
    public void Apply(AEvent a, MyAggregate aggregate, IQuerySession session)
    {
    }
}

public class InvalidArgumentType: SingleStreamProjection<MyAggregate, Guid>
{
    public void Apply(AEvent @event, MyAggregate aggregate, IDocumentOperations operations)
    {
    }
}

public class BadMethodName: SingleStreamProjection<MyAggregate, Guid>
{
    public void DoStuff(AEvent @event, MyAggregate aggregate)
    {
    }

    public MyAggregate Create(CreateEvent @event)
    {
        return new MyAggregate { ACount = @event.A, BCount = @event.B, CCount = @event.C, DCount = @event.D };
    }
}

public class AllGood: SingleStreamProjection<MyAggregate, Guid>
{
    public AllGood()
    {
        ProjectionName = "AllGood";
    }

    [MartenIgnore]
    public void RandomMethodName()
    {
    }

    public MyAggregate Create(CreateEvent @event)
    {
        return new MyAggregate { ACount = @event.A, BCount = @event.B, CCount = @event.C, DCount = @event.D };
    }

    public Task<MyAggregate> Create(CreateEvent @event, IQuerySession session)
    {
        return null;
    }

    public void Apply(AEvent @event, MyAggregate aggregate)
    {
        aggregate.ACount++;
    }

    public MyAggregate Apply(BEvent @event, MyAggregate aggregate)
    {
        return new MyAggregate
        {
            ACount = aggregate.ACount,
            BCount = aggregate.BCount + 1,
            CCount = aggregate.CCount,
            DCount = aggregate.DCount,
            Id = aggregate.Id
        };
    }

    public void Apply(MyAggregate aggregate, CEvent @event)
    {
        aggregate.CCount++;
    }

    public MyAggregate Apply(MyAggregate aggregate, DEvent @event)
    {
        return new MyAggregate
        {
            ACount = aggregate.ACount,
            BCount = aggregate.BCount,
            CCount = aggregate.CCount,
            DCount = aggregate.DCount + 1,
            Id = aggregate.Id
        };
    }
}
