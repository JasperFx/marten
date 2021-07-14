using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing
{
    public class build_aggregate_multiple_projections: DaemonContext
    {
        //Aggregate 1
        public class Car
        {
            public string Name { get; set; }
        }

        //View 1
        public class CarView
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
        }

        //Event 1
        public class CarNamed
        {
            public string Value { get; set; }
        }

        //Aggregation 2
        public class CarAggregation: AggregateProjection<CarView>
        {
            public void Apply(CarView view, CarNamed ev)
            {
                view.Name = ev.Value;
            }
        }

        //Aggregate 2
        public class Truck
        {
            public string Name { get; set; }
        }

        //View 2
        public class TruckView
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
        }

        //Event 2
        public class TruckNamed
        {
            public string Value { get; set; }
        }

        //Aggregation 2
        public class TruckAggregation: AggregateProjection<TruckView>
        {
            public void Apply(TruckView view, TruckNamed ev)
            {
                view.Name = ev.Value;
            }
        }


        public build_aggregate_multiple_projections(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task bug_repro()
        {
            const int expectedSequence = 4;

            //Register both projections
            StoreOptions(x =>
            {
                x.Projections.Add<CarAggregation>(ProjectionLifecycle.Async);
                x.Projections.Add<TruckAggregation>(ProjectionLifecycle.Async);
            }, true);

            var agent = await StartDaemon();

            var carStreamId = Guid.NewGuid();
            var truckStreamId = Guid.NewGuid();

            //Create car stream - Transaction 1
            using (var session = theStore.LightweightSession())
            {
                session.Events.StartStream(carStreamId, new CarNamed() { Value = "car-name-1" });
                await session.SaveChangesAsync();
            }

            //Create truck stream - Transaction 2
            using (var session = theStore.LightweightSession())
            {
                session.Events.StartStream(truckStreamId, new TruckNamed() { Value = "truck-name-1" });
                await session.SaveChangesAsync();
            }

            //Send TruckNamed Event - Transaction 3
            using (var session = theStore.LightweightSession())
            {
                session.Events.Append(truckStreamId, new TruckNamed() { Value = "truck-name-2" });

                await session.SaveChangesAsync();
            }

            //Send CarNamed Event - Transaction 4
            using (var session = theStore.LightweightSession())
            {
                session.Events.Append(carStreamId, new CarNamed() { Value = "car-name-2" });
                await session.SaveChangesAsync();
            }

            //Wait for shards and highwater agent to catchup on the events
            await agent.Tracker.WaitForShardState(new ShardState("CarView:All", expectedSequence), 15.Seconds());
            await agent.Tracker.WaitForShardState(new ShardState("TruckView:All", expectedSequence), 15.Seconds()); // Will fail on this line
            await agent.Tracker.WaitForHighWaterMark(expectedSequence);


            //Assert results are latest
            using (var session = theStore.QuerySession())
            {
                var carName = session.Query<CarView>().FirstOrDefault()?.Name;
                var truckName = session.Query<TruckView>().FirstOrDefault()?.Name;

                carName.ShouldBe("car-name-2");
                truckName.ShouldBe("truck-name-2");
            }

        }
    }
}
