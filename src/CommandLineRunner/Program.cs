using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using LamarCodeGeneration;
using Marten;
using Marten.AsyncDaemon.Testing;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Aggregation;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Oakton;
using Weasel.Core;

namespace CommandLineRunner
{
    #region sample_configuring_pre_build_types

    public class Program
    {
        public static Task<int> Main(string[] args)
        {
            return CreateHostBuilder(args).RunOaktonCommands(args);
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddMarten(opts =>
                    {
                        opts.AutoCreateSchemaObjects = AutoCreate.All;
                        opts.DatabaseSchemaName = "cli";
                        opts.Connection(ConnectionSource.ConnectionString);

                        // This is important, setting this option tells Marten to
                        // *try* to use pre-generated code at runtime
                        opts.GeneratedCodeMode = TypeLoadMode.Auto;

                        opts.Schema.For<Activity>().AddSubClass<Trip>();

                        // You have to register all persisted document types ahead of time
                        // RegisterDocumentType<T>() is the equivalent of saying Schema.For<T>()
                        // just to let Marten know that document type exists
                        opts.RegisterDocumentType<Target>();
                        opts.RegisterDocumentType<User>();

                        // If you use compiled queries, you will need to register the
                        // compiled query types with Marten ahead of time
                        opts.RegisterCompiledQueryType(typeof(FindUserByAllTheThings));

                        // Register all event store projections ahead of time
                        opts.Projections.Add(new TripAggregationWithCustomName(), ProjectionLifecycle.Inline);
                        opts.Projections.Add(new DayProjection(), ProjectionLifecycle.Async);
                        opts.Projections.Add(new DistanceProjection(), ProjectionLifecycle.Async);

                        opts.Projections.Add(new SimpleAggregate(), ProjectionLifecycle.Inline);

                        // This is actually important to register "live" aggregations too for the code generation
                        opts.Projections.SelfAggregate<SelfAggregatingTrip>(ProjectionLifecycle.Live);
                    }).AddAsyncDaemon(DaemonMode.Solo);
                });
        }
    }

    #endregion

    public class SelfAggregatingTrip
    {
        public Guid Id { get; set; }
        public void Apply(Arrival e) => State = e.State;
        public string State { get; set; } = null!;

        public void Apply(Travel e) => Traveled += e.TotalDistance();
        public double Traveled { get; set; }

        public void Apply(TripEnded e)
        {
            Active = false;
            EndedOn = e.Day;
        }

        public int EndedOn { get; set; }

        public bool Active { get; set; }
    }

    public class SimpleAggregate: AggregateProjection<MyAggregate>
    {
        public SimpleAggregate()
        {
            ProjectionName = "AllGood";
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
