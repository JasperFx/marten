using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.CodeGeneration;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Aggregation
{
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



        [Fact]
        public void aggregate_id_is_wrong_type_1()
        {
            var message = errorMessageFor(x =>
            {
                x.Events.StreamIdentity = StreamIdentity.AsGuid;
                x.Events.Projections.AsyncSelfAggregate<StringIdentifiedAggregate>();
            });

            message.ShouldContain("Id type mismatch. The stream identity type is System.Guid, but the aggregate document Marten.Testing.Events.Aggregation.aggregation_projection_validation_rules.StringIdentifiedAggregate id type is string", StringComparisonOption.Default);
        }


        [Fact]
        public void aggregate_id_is_wrong_type_2()
        {
            var message = errorMessageFor(x =>
            {
                x.Events.StreamIdentity = StreamIdentity.AsString;
                x.Events.Projections.AsyncSelfAggregate<GuidIdentifiedAggregate>();
            });

            message.ShouldContain("Id type mismatch. The stream identity type is string, but the aggregate document Marten.Testing.Events.Aggregation.aggregation_projection_validation_rules.GuidIdentifiedAggregate id type is Guid", StringComparisonOption.Default);
        }

        [Fact]
        public void if_events_are_multi_tenanted_so_must_the_projected_view()
        {
            errorMessageFor(opts =>
            {
                opts.Events.TenancyStyle = TenancyStyle.Conjoined;
                opts.Events.Projections.AsyncSelfAggregate<GuidIdentifiedAggregate>();
            }).ShouldContain("Tenancy storage style mismatch between the events (Conjoined) and the aggregate type Marten.Testing.Events.Aggregation.aggregation_projection_validation_rules.GuidIdentifiedAggregate (Single)", StringComparisonOption.Default);
        }

        [Fact]
        public void if_the_aggregate_is_multi_tenanted_but_the_events_are_not()
        {
            errorMessageFor(opts =>
            {
                opts.Events.Projections.AsyncSelfAggregate<GuidIdentifiedAggregate>();
                opts.Schema.For<GuidIdentifiedAggregate>().MultiTenanted();
            }).ShouldContain("Tenancy storage style mismatch between the events (Single) and the aggregate type Marten.Testing.Events.Aggregation.aggregation_projection_validation_rules.GuidIdentifiedAggregate (Conjoined)", StringComparisonOption.Default);
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
            projection.As<IValidatedProjection>().AssertValidity();
        }

        [Fact]
        public void find_bad_method_names_that_are_not_ignored()
        {
            var projection = new BadMethodName().As<IValidatedProjection>();
            var ex = Should.Throw<InvalidProjectionException>(() => projection.AssertValidity());

            ex.Message.ShouldContain("Unrecognized method name 'DoStuff'. Either mark with [MartenIgnore] or use one of 'Apply', 'Create', 'ShouldDelete'", StringComparisonOption.NormalizeWhitespaces);
        }

        [Fact]
        public void find_invalid_argument_type()
        {
            var projection = new InvalidArgumentType().As<IValidatedProjection>();
            var ex = Should.Throw<InvalidProjectionException>(() => projection.AssertValidity());
            ex.InvalidMethods.Single()
                .Errors
                .ShouldContain("Parameter of type 'Marten.IDocumentOperations' is not supported. Valid options are System.Threading.CancellationToken, Marten.IQuerySession, Marten.Testing.Events.Aggregation.MyAggregate, Marten.Testing.Events.Aggregation.AEvent, Marten.Events.Event<Marten.Testing.Events.Aggregation.AEvent>");
        }

        [Fact]
        public void missing_event_altogether()
        {
            var projection = new MissingEventType1().As<IValidatedProjection>();
            var ex = Should.Throw<InvalidProjectionException>(() => projection.AssertValidity());
            ex.InvalidMethods.Single()
                .Errors.ShouldContain(MethodSlot.NoEventType);
        }

        [Fact]
        public void marten_can_guess_the_event_based_on_what_is_left()
        {
            var projection = new CanGuessEventType().As<IValidatedProjection>();
            projection.AssertValidity();
        }

        [Fact]
        public void invalid_return_type()
        {
            var projection = new BadReturnType().As<IValidatedProjection>();
            var ex = Should.Throw<InvalidProjectionException>(() => projection.AssertValidity());
            ex.InvalidMethods.Single()
                .Errors.ShouldContain(
                    "Parameter of type 'Marten.IDocumentOperations' is not supported. Valid options are System.Threading.CancellationToken, Marten.IQuerySession, Marten.Testing.Events.Aggregation.MyAggregate, Marten.Testing.Events.Aggregation.AEvent, Marten.Events.Event<Marten.Testing.Events.Aggregation.AEvent>", "Return type 'string' is invalid. The valid options are System.Threading.CancellationToken, Marten.IQuerySession, Marten.Testing.Events.Aggregation.MyAggregate");
        }

        [Fact]
        public void missing_required_parameter()
        {
            var projection = new MissingMandatoryType().As<IValidatedProjection>();
            var ex = Should.Throw<InvalidProjectionException>(() => projection.AssertValidity());

            ex.InvalidMethods.Single()
                .Errors.ShouldContain("Aggregate type 'Marten.Testing.Events.Aggregation.MyAggregate' is required as a parameter");
        }
    }

    public class MissingMandatoryType: AggregateProjection<MyAggregate>
    {
        public void Apply(AEvent @event)
        {

        }
    }

    public class BadReturnType: AggregateProjection<MyAggregate>
    {
        public string Apply(AEvent @event, MyAggregate aggregate, IDocumentOperations operations)
        {
            return null;
        }
    }

    public class MissingEventType1: AggregateProjection<MyAggregate>
    {
        public void Apply(MyAggregate aggregate, IDocumentOperations operations)
        {

        }
    }

    public class CanGuessEventType: AggregateProjection<MyAggregate>
    {
        public void Apply(AEvent a, MyAggregate aggregate, IQuerySession session)
        {

        }
    }

    public class InvalidArgumentType: AggregateProjection<MyAggregate>
    {
        public void Apply(AEvent @event, MyAggregate aggregate, IDocumentOperations operations)
        {

        }
    }

    public class BadMethodName: AggregateProjection<MyAggregate>
    {
        public void DoStuff(AEvent @event, MyAggregate aggregate)
        {

        }
    }

    public class AllGood: AggregateProjection<MyAggregate>
    {
        [MartenIgnore]
        public void RandomMethodName()
        {

        }

        public MyAggregate Create(CreateEvent @event)
        {
            return new MyAggregate
            {
                ACount = @event.A,
                BCount = @event.B,
                CCount = @event.C,
                DCount = @event.D
            };
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
}
