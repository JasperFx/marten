using System;
using System.Collections.Generic;
using System.Diagnostics;
using Baseline;
using Marten.Testing;
using Marten.Testing.Events;
using Marten.Testing.Events.Projections;
using Marten.Testing.Harness;
using StoryTeller;
using StoryTeller.Grammars;

namespace Marten.Storyteller.Fixtures.EventStore
{
    public class InlineAggregationFixture: Fixture
    {
        private readonly LightweightCache<string, Guid> _streams = new LightweightCache<string, Guid>();
        private IDocumentStore _store;
        private Guid _lastStream;

        public InlineAggregationFixture()
        {
            Title = "Inline Aggregation by Stream using QuestParty";
        }

        public override void SetUp()
        {
            _store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.Projections.SelfAggregate<QuestParty>();
            });

            Context.State.Store(_store);
        }

        [FormatAs("For a new 'Quest' named {name} that started on {date}")]
        public void ForNewQuestStream(string name, DateTime date)
        {
            var started = new QuestStarted { Name = name };
            using (var session = _store.LightweightSession())
            {
                _lastStream = session.Events.StartStream<Quest>(started).Id;

                _streams[name] = _lastStream;

                session.SaveChanges();
            }
        }

        public IGrammar HasAdditionalEvents()
        {
            return Embed<QuestEventFixture>("With events").Before(c => c.State.Store("streamId", _lastStream));
        }

        [FormatAs("For stream {streamName}")]
        public void ForStream(string streamName)
        {
            using (var session = _store.LightweightSession())
            {
                var party = session.Load<QuestParty>(_streams[streamName]);
                Debug.WriteLine("Party members are: " + party.Members.Join(", "));

                CurrentObject = party;
            }
        }

        public IGrammar QuestPartyShouldBe()
        {
            return Paragraph("The QuestParty projection should be", _ =>
            {
                _ += CheckPropertyGrammar.For<QuestParty>(x => x.Name);
                _ += this["Members"];
            });
        }

        [FormatAs("Members should be {Members}")]
        public List<string> Members()
        {
            return CurrentObject.As<QuestParty>().Members;
        }
    }
}
