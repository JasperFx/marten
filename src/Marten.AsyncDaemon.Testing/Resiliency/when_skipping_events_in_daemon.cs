using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Linq;
using Marten.Schema;
using Marten.Testing.Harness;
using Marten.Util;
using Npgsql;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing.Resiliency
{
    public class when_skipping_events_in_daemon : DaemonContext
    {
        private readonly string[] theNames =
        {
            "Jane",
            "Jill",
            "Jack",
            "JohnBad",
            "JakeBad",
            "JillBad",
            "JohnBad",
            "Derrick",
            "Daniel",
            "Donald",
            "DonBad",
            "Bob",
            "Beck",
            "BadName",
            "Jeremy"
        };

        public when_skipping_events_in_daemon(ITestOutputHelper output) : base(output)
        {
            StoreOptions(opts =>
            {
                opts.Events.DatabaseSchemaName = "daemon";
                opts.Projections.Add<ErrorRejectingEventProjection>(ProjectionLifecycle.Async);
                opts.Projections.Add<CollateNames>(ProjectionLifecycle.Async);

                opts.Events.Daemon.OnApplyEventException().SkipEvent();
                opts.Events.Daemon.OnException<NpgsqlException>().RetryLater(50.Milliseconds(), 250.Milliseconds(), 500.Milliseconds())
                    .Then.Stop();
            });
        }



        private async Task<ProjectionDaemon> PublishTheEvents()
        {
            var daemon = await StartDaemon();

            var waiter1 = daemon.Tracker.WaitForShardState("CollateNames:All", theNames.Length);
            var waiter2 = daemon.Tracker.WaitForShardState("NamedDocuments:All", theNames.Length, 5.Minutes());


            var events = theNames.Select(name => new NameEvent {Name = name})
                .OfType<object>()
                .ToArray();

            theSession.Events.StartStream(Guid.NewGuid(), events);
            await theSession.SaveChangesAsync();

            await daemon.Tracker.WaitForHighWaterMark(theNames.Length);

            await waiter1;
            await waiter2;

            return daemon;
        }

        [Fact]
        public async Task the_shards_should_still_be_running()
        {
            var daemon = await PublishTheEvents();

            var shards = daemon.CurrentShards();

            foreach (var shard in shards)
            {
                shard.Status.ShouldBe(AgentStatus.Running);
            }
        }

        [Fact]
        public async Task skip_bad_events_in_event_projection()
        {
            await PublishTheEvents();

            var names = await theSession.Query<NamedDocument>()
                .OrderBy(x => x.Name)
                .Select(x => x.Name)
                .ToListAsync();

            var expected = theNames
                .OrderBy(x => x)
                .Where(x => !x.Contains("bad", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            names.ShouldHaveTheSameElementsAs(expected);
        }

        [Fact]
        public async Task skip_bad_events_in_aggregate_projection()
        {
            await PublishTheEvents();

            var jNames = await theSession.LoadAsync<NamesByLetter>("J");

            jNames.Names.OrderBy(x => x)
                .ShouldHaveTheSameElementsAs("Jack", "Jane", "Jeremy", "Jill");
        }

        [Fact]
        public async Task see_the_dead_letter_events()
        {
            await PublishTheEvents();

            theSession.Logger = new TestOutputMartenLogger(_output);
            var skipped = await theSession.Query<DeadLetterEvent>().ToListAsync();



            skipped.Where(x => x.ProjectionName == "CollateNames" && x.ShardName == "All")
                .Select(x => x.EventSequence).OrderBy(x => x)
                .ShouldHaveTheSameElementsAs(4, 5, 6, 7);

            skipped.Where(x => x.ProjectionName == "NamedDocuments" && x.ShardName == "All")
                .Select(x => x.EventSequence).OrderBy(x => x)
                .ShouldHaveTheSameElementsAs(4, 5, 6, 7, 11, 14);

        }
    }

    public class ErrorRejectingEventProjection: EventProjection
    {
        public ErrorRejectingEventProjection()
        {
            ProjectionName = "NamedDocuments";
        }

        public NamedDocument Create(NameEvent e)
        {
            if (e.Name.Contains("bad", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Bad name.");
            }

            return new NamedDocument {Name = e.Name};
        }
    }

    public class CollateNames: ViewProjection<NamesByLetter, string>
    {
        public CollateNames()
        {
            ProjectionName = "CollateNames";
            Identity<NameEvent>(e => e.Name.First().ToString());
        }

        public void Apply(NameEvent e, NamesByLetter names)
        {
            if (e.Name.Contains("bad", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Bad name.");
            }

            names.Names.Add(e.Name);
        }
    }

    public class NamesByLetter
    {
        [Identity]
        public string Letter { get; set; }
        public List<string> Names { get; set; } = new List<string>();
    }

    public class NameEvent
    {
        public string Name { get; set; }
    }

    public class NamedDocument
    {
        public string Name { get; set; }
        public Guid Id { get; set; }
    }
}
