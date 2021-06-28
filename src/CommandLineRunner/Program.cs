using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten;
using Marten.AsyncDaemon.Testing;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Aggregation;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Testing.Documents;
using Marten.Testing.Events.Aggregation;
using Marten.Testing.Harness;
using Marten.Testing.Linq.Compiled;
using Microsoft.Extensions.Hosting;
using Oakton;
using Weasel.Postgresql;

namespace CommandLineRunner
{
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

                        opts.GeneratedCodeMode = TypeLoadMode.LoadFromPreBuiltAssembly;

                        opts.RegisterDocumentType<Target>();
                        opts.RegisterDocumentType<User>();

                        opts.RegisterCompiledQueryType(typeof(FindUserByAllTheThings));

                        opts.Projections.Add(new TripAggregation(), ProjectionLifecycle.Async);
                        opts.Projections.Add(new DayProjection(), ProjectionLifecycle.Async);
                        opts.Projections.Add(new DistanceProjection(), ProjectionLifecycle.Async);

                        opts.Projections.Add(new SimpleAggregate(), ProjectionLifecycle.Inline);

                        opts.Projections.AsyncMode = DaemonMode.Solo;
                    });
                });
        }
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
