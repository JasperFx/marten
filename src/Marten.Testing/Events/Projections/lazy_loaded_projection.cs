using System;
using System.Collections.Generic;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class lazy_loaded_projection : DocumentSessionFixture<IdentityMap>
    {
        public class Logger
        {
            public List<string> Logs { get; } = new List<string>();

            public void Log(string message)
            {
                Logs.Add(message);
            }
        }

        public class QuestPaused
        {
            public string Name { get; set; }
            public Guid QuestId { get; set; }

            public override string ToString()
            {
                return $"Quest {Name} paused";
            }
        }

        // SAMPLE: viewprojection-from-class-with-injection
        public class PersistViewProjectionWithInjection : PersistViewProjection
        {
            private readonly Logger logger;

            public PersistViewProjectionWithInjection() : base()
            {
                ProjectEvent<QuestPaused>(@event => @event.QuestId, LogAndPersist);
            }

            public PersistViewProjectionWithInjection(Logger logger) : this()
            {
                this.logger = logger;
            }

            private void LogAndPersist<T>(PersistedView view, T @event)
            {
                logger.Log($"Handled {typeof(T).Name} event: {@event.ToString()}");
                view.Events.Add(@event);
            }
        }
        // ENDSAMPLE

        private static readonly Guid streamId = Guid.NewGuid();

        private QuestStarted started = new QuestStarted { Id = streamId, Name = "Find the Orb" };
        private MembersJoined joined = new MembersJoined { QuestId = streamId, Day = 2, Location = "Faldor's Farm", Members = new[] { "Garion", "Polgara", "Belgarath" } };
        private QuestPaused paused = new QuestPaused { QuestId = streamId, Name = "Find the Orb" };

        [Fact]
        public void from_projection()
        {
            var logger = new Logger();

            // SAMPLE: viewprojection-from-class-with-injection-configuration
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.InlineProjections.Add(() => new PersistViewProjectionWithInjection(logger));
            });
            // ENDSAMPLE

            theSession.Events.StartStream<QuestParty>(streamId, started, joined);
            theSession.SaveChanges();

            var document = theSession.Load<PersistedView>(streamId);
            document.Events.Count.ShouldBe(2);
            logger.Logs.Count.ShouldBe(0);

            //check injection
            theSession.Events.Append(streamId, paused);
            theSession.SaveChanges();

            var document2 = theSession.Load<PersistedView>(streamId);
            document2.Events.Count.ShouldBe(3);

            logger.Logs.Count.ShouldBe(1);
        }
    }
}