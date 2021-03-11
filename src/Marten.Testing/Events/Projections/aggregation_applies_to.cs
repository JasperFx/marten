using System;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Testing.Documents;
using Marten.Testing.Events.Aggregation;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class aggregation_applies_to
    {
        [Theory]
        [InlineData(typeof(AEvent), true)]
        [InlineData(typeof(BEvent), true)]
        [InlineData(typeof(CEvent), true)]
        [InlineData(typeof(DEvent), true)]
        [InlineData(typeof(EEvent), true)]
        [InlineData(typeof(UserDeleted), true)]
        [InlineData(typeof(Thing), true)]
        [InlineData(typeof(CreateEvent), true)]
        [InlineData(typeof(QuestStarted), false)]
        public void applies_to_type(Type eventType, bool shouldApply)
        {
            var aggregate = new SampleAggregate();
            aggregate.AppliesTo(new Type[]{eventType})
                .ShouldBe(shouldApply);
        }
    }

    public interface IThing{}
    public class Thing : IThing{}

    public class SampleAggregate: AggregateProjection<MyAggregate>
    {
        public SampleAggregate()
        {
            DeleteEvent<UserDeleted>();
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

        public void Apply(IThing e, MyAggregate aggregate)
        {

        }

        public async Task Apply(UserUpdated @event, MyAggregate aggregate, IQuerySession session)
        {
            var user = await session.LoadAsync<User>(@event.UserId);
            aggregate.UpdatedBy = user.UserName;
        }

        public void Apply(AEvent @event, MyAggregate aggregate)
        {
            aggregate.ACount++;
        }

        public void Apply(BEvent @event, MyAggregate aggregate)
        {
            aggregate.BCount++;
        }

        public void Apply(MyAggregate aggregate, CEvent @event)
        {
            aggregate.CCount++;
        }

        public void Apply(MyAggregate aggregate, DEvent @event)
        {
            aggregate.DCount++;
        }

        public bool ShouldDelete(MyAggregate aggregate, EEvent @event)
        {
            return true;
        }
    }
}
