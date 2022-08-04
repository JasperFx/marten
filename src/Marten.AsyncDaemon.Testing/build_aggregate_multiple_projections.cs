using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Testing;
using NpgsqlTypes;
using Shouldly;
using Weasel.Postgresql;
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
        public class CarAggregation: SingleStreamAggregation<CarView>
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
        public class TruckAggregation: SingleStreamAggregation<TruckView>
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
            await agent.Tracker.WaitForShardState(new ShardState("TruckView:All", expectedSequence), 15.Seconds());
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

        [Fact]
        public async Task bug_repro_2()
        {
            // register projection
            StoreOptions(x =>
            {
                x.Projections.Add<CarAggregation>();
            }, true);

            // create car stream with 1000 events
            using (var session = theStore.LightweightSession())
            {
                var events = new List<CarNamed>();

                for (var i = 0; i < 1000; i++)
                {
                    events.Add(new CarNamed { Value = $"car-name-{i}" });
                }

                session.Events.StartStream(Guid.NewGuid(), events);
                await session.SaveChangesAsync();
            }

            // create some gaps
            long startingId = 25;
            var group1 = Enumerable.Range(0, 5).Select(x => x + startingId).ToArray();
            var group2 = Enumerable.Range(0, 5).Select(x => x + startingId + 10).ToArray();
            var group3 = Enumerable.Range(0, 5).Select(x => x + startingId + 20).ToArray();
            var group4 = Enumerable.Range(0, 5).Select(x => x + startingId + 45).ToArray();

            var groupsToRemove = group1.Concat(group2).Concat(group3).Concat(group4).ToArray();

            await deleteEvents(groupsToRemove);

            var projections = new[] { "CarView" };

            // rebuild the projection
            var daemon = await theStore.BuildProjectionDaemonAsync(logger: Logger);

            await daemon.StartDaemon();

            try
            {
                foreach (var projection in projections)
                {
                    await daemon.RebuildProjection($"{nameof(build_aggregate_multiple_projections)}.{projection}", default);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                await daemon.StopAll();
                daemon.Dispose();
            }

            // assert that the projections have reached max seq_id
            using (var session = theStore.QuerySession())
            {
                var waterMark = await GetHighWaterMark();
                var maxSeqId = await GetMaxSeqId();

                var cars = await session.Query<CarView>().ToListAsync();

                waterMark.ShouldBe(maxSeqId);
                cars.Last().Name.ShouldBe("car-name-999");
            }
        }

        protected async Task deleteEvents(params long[] ids)
        {
            using var conn = theStore.CreateConnection();
            await conn.OpenAsync();

            await conn
                .CreateCommand($"delete from {theStore.Events.DatabaseSchemaName}.mt_events where seq_id = ANY(:ids)")
                .With("ids", ids, NpgsqlDbType.Bigint | NpgsqlDbType.Array)
                .ExecuteNonQueryAsync();
        }

        protected async Task<long> GetHighWaterMark()
        {
            using var conn = theStore.CreateConnection();
            await conn.OpenAsync();

            return (long)await conn
                .CreateCommand($"select last_seq_id from {theStore.Events.DatabaseSchemaName}.mt_event_progression where name = 'HighWaterMark'")
                .ExecuteScalarAsync();
        }

        protected async Task<long> GetMaxSeqId()
        {
            using var conn = theStore.CreateConnection();
            await conn.OpenAsync();

            return (long)await conn
                .CreateCommand($"select max(seq_id) from {theStore.Events.DatabaseSchemaName}.mt_events")
                .ExecuteScalarAsync();
        }
    }
}
