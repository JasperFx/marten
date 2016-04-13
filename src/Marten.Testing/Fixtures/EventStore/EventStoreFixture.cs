using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Testing.Events;
using Marten.Util;
using NpgsqlTypes;
using StoryTeller;
using StoryTeller.Grammars.Tables;
using StructureMap;
using StructureMap.Util;

namespace Marten.Testing.Fixtures.EventStore
{
    public class EventStoreFixture : Fixture
    {
        private readonly Cache<string, Guid> _streams = new Cache<string, Guid>();
        private IContainer _container;
        private IDocumentStore _store;
        private Guid _lastStream;
        private int _version;
        private DateTime _time;

        public override void SetUp()
        {
            _streams.ClearAll();

            _container = Container.For<DevelopmentModeRegistry>();
            _store = _container.GetInstance<IDocumentStore>();
            _store.Advanced.Clean.CompletelyRemoveAll();

            Context.State.Store(_store);
        }


        [FormatAs("For a new 'Quest' named {name} that started on {date}")]
        public void ForNewQuestStream(string name, DateTime date)
        {
            var started = new QuestStarted { Name = name };
            using (var session = _store.LightweightSession())
            {
                _lastStream = session.Events.StartStream<Quest>(started);

                _streams[name] = _lastStream;

                session.SaveChanges();

                rewriteEventTime(date, session, _lastStream);
            }
        }

        [FormatAs("The version of quest {name} should be {version}")]
        public int TheQuestVersionShouldBe(string name)
        {
            using (var session = _store.LightweightSession())
            {
                var streamId = _streams[name];
                var state = session.Events.FetchStreamState(streamId);

                return state.Version;
            }
        }

        [ExposeAsTable("If the Event Timestamps were")]
        public void OverwriteTimestamps(int version, DateTime time)
        {
            var store = Context.State.Retrieve<IDocumentStore>();
            using (var session = store.OpenSession())
            {
                var cmd = session.Connection.CreateCommand()
                    .WithText("update mt_events set timestamp = :time where stream_id = :stream and version = :version")
                    .With("stream", _lastStream)
                    .With("version", version)
                    .With("time", time.ToUniversalTime());

                cmd.ExecuteNonQuery();

                session.SaveChanges();
            }
        }

        public IGrammar AllTheCapturedEventsShouldBe()
        {
            return VerifyStringList(allEvents)
                .Titled("When fetching the entire event stream, the captured events returned should be")
                .Ordered();
        }

        private IEnumerable<string> allEvents()
        {
            using (var session = _store.LightweightSession())
            {
                return session.Events.FetchStream(_lastStream).Select(x => x.ToString()).ToArray();
            }
        }


        [Hidden, FormatAs("For version # {version}")]
        public void Version(int version)
        {
            _version = version;
        }

        [Hidden]
        public IGrammar EventsAtTimeShouldBe()
        {
            return VerifyStringList(() => allEvents(_time))
                .Titled("The captured events for this stream and specified time should be")
                .Ordered();
        }

        public IGrammar FetchEventsByTimestamp()
        {
            return Paragraph("Fetch the events by time", _ =>
            {
                _ += this["Time"];
                _ += this["EventsAtTimeShouldBe"];
            });
        }

        private IEnumerable<string> allEvents(DateTime time)
        {
            using (var session = _store.LightweightSession())
            {
                return session.Events.FetchStream(_lastStream, time.ToUniversalTime()).Select(x => x.ToString()).ToArray();
            }
        }




        [Hidden, FormatAs("For time # {time}")]
        public void Time(DateTime time)
        {
            _time = time;
        }

        [Hidden]
        public IGrammar EventsAtVersionShouldBe()
        {
            return VerifyStringList(() => allEvents(_version))
                .Titled("The captured events for this stream and specified version should be")
                .Ordered();
        }

        public IGrammar FetchEventsByVersion()
        {
            return Paragraph("Fetch the events by version", _ =>
            {
                _ += this["Version"];
                _ += this["EventsAtVersionShouldBe"];
            });
        }

        private IEnumerable<string> allEvents(int version)
        {
            using (var session = _store.LightweightSession())
            {
                // TODO -- eliminate the aggregate type here
                return session.Events.FetchStream(_lastStream, version).Select(x => x.ToString()).ToArray();
            }
        }






        public IGrammar HasAdditionalEvents()
        {
            return Embed<QuestEventFixture>("With events").Before(c => c.State.Store("streamId", _lastStream));
        }

        private static void rewriteEventTime(DateTime date, IDocumentSession session, Guid id)
        {
            // TODO -- let's rethink this one later
            session.Connection.CreateCommand().WithText("update mt_events set timestamp = :date where id = :id")
                .With("date", date.ToUniversalTime(), NpgsqlDbType.Timestamp)
                .With("id", id)
                .ExecuteNonQuery();

            session.SaveChanges();
        }
    }
}