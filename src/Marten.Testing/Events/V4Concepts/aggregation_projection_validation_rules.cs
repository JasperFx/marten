using System.Linq;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using Marten.Events.V4Concept;
using Marten.Events.V4Concept.Aggregation;
using Marten.Events.V4Concept.CodeGeneration;
using Marten.Exceptions;
using Marten.Testing.Events.V4Concepts.Aggregations;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.V4Concepts
{
    public class aggregation_projection_validation_rules
    {
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
            var ex = Should.Throw<InvalidProjectionDefinitionException>(() => projection.AssertValidity());

            ex.Message.ShouldContain("Unrecognized method name 'DoStuff'. Either mark with [MartenIgnore] or use one of 'Apply', 'Create', 'ShouldDelete'", StringComparisonOption.NormalizeWhitespaces);
        }

        [Fact]
        public void find_invalid_argument_type()
        {
            var projection = new InvalidArgumentType().As<IValidatedProjection>();
            var ex = Should.Throw<InvalidProjectionDefinitionException>(() => projection.AssertValidity());
            ex.InvalidMethods.Single()
                .Errors
                .ShouldContain("Parameter of type 'Marten.IDocumentOperations' is not supported. Valid options are Marten.IQuerySession, Marten.Testing.Events.V4Concepts.Aggregations.MyAggregate, Marten.Testing.Events.V4Concepts.Aggregations.AEvent, Marten.Events.Event<Marten.Testing.Events.V4Concepts.Aggregations.AEvent>"
                );
        }

        [Fact]
        public void missing_event_altogether()
        {
            var projection = new MissingEventType1().As<IValidatedProjection>();
            var ex = Should.Throw<InvalidProjectionDefinitionException>(() => projection.AssertValidity());
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
            var ex = Should.Throw<InvalidProjectionDefinitionException>(() => projection.AssertValidity());
            ex.InvalidMethods.Single()
                .Errors.ShouldContain(
                    "Parameter of type 'Marten.IDocumentOperations' is not supported. Valid options are Marten.IQuerySession, Marten.Testing.Events.V4Concepts.Aggregations.MyAggregate, Marten.Testing.Events.V4Concepts.Aggregations.AEvent, Marten.Events.Event<Marten.Testing.Events.V4Concepts.Aggregations.AEvent>");
        }

        [Fact]
        public void missing_required_parameter()
        {
            var projection = new MissingMandatoryType().As<IValidatedProjection>();
            var ex = Should.Throw<InvalidProjectionDefinitionException>(() => projection.AssertValidity());

            ex.InvalidMethods.Single()
                .Errors.ShouldContain("Aggregate type 'Marten.Testing.Events.V4Concepts.Aggregations.MyAggregate' is required as a parameter");
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
