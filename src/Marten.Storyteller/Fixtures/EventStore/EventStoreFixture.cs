using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Weasel.Postgresql;
using Marten.Testing.Events;
using Marten.Testing.Events.Projections;
using Marten.Testing.Harness;
using NpgsqlTypes;
using StoryTeller;
using StoryTeller.Grammars.Tables;
using Weasel.Core;

namespace Marten.Storyteller.Fixtures.EventStore
{
    public class EventStoreFixture: Fixture
    {
        private readonly LightweightCache<string, Guid> _streams = new LightweightCache<string, Guid>();
        private IDocumentStore _store;
        private Guid _lastStream;
        private long _version;
        private DateTime _time;
        private string _mode;

        public override void SetUp()
        {
            _streams.ClearAll();

            _store = DocumentStore.For(opts =>
            {
              opts.Connection(ConnectionSource.ConnectionString);
              opts.AutoCreateSchemaObjects = AutoCreate.All;
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

                rewriteEventTime(date, session, _lastStream);
            }
        }

        [FormatAs("The version of quest {name} should be {version}")]
        public long TheQuestVersionShouldBe(string name)
        {
            using (var session = _store.LightweightSession())
            {
                var streamId = _streams[name];
                var state = session.Events.FetchStreamState(streamId);

                return state.Version;
            }
        }

        [ExposeAsTable("If the Event Timestamps were")]
        public void OverwriteTimestamps(long version, DateTime time)
        {
            var store = Context.State.Retrieve<IDocumentStore>();
            using (var session = store.OpenSession())
            {
                var cmd = session.Connection.CreateCommand()
                    .Sql("update mt_events set timestamp = :time where stream_id = :stream and version = :version")
                    .With("stream", _lastStream)
                    .With("time", time.ToUniversalTime(), NpgsqlDbType.Timestamp)
                    .With("version", version)
                    ;

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
                return session.Events.FetchStream(_lastStream).Select(x => x.Data.ToString()).ToArray();
            }
        }

        [Hidden, FormatAs("For version # {version}")]
        public void Version(long version)
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
                _ += this["FetchMode"];
                _ += this["EventsAtTimeShouldBe"];
            });
        }

        private IEnumerable<string> allEvents(DateTime time)
        {
            using (var session = _store.LightweightSession())
            {
                switch (_mode)
                {
                    case "Synchronously":
                        return session.Events.FetchStream(_lastStream, timestamp: time.ToUniversalTime()).Select(x => x.Data.ToString()).ToArray();

                    case "Asynchronously":
                        return session.Events.FetchStreamAsync(_lastStream, timestamp: time.ToUniversalTime()).GetAwaiter().GetResult().Select(x => x.Data.ToString()).ToArray();

                    case "In a batch":
                        throw new NotSupportedException("Not ready yet");
                }

                throw new NotSupportedException();
            }
        }

        [FormatAs("Fetching {mode}")]
        public void FetchMode([SelectionValues("Synchronously", "Asynchronously", "In a batch"), Default("Synchronously")]string mode)
        {
            _mode = mode;
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
                _ += this["FetchMode"];
                _ += this["Version"];
                _ += this["EventsAtVersionShouldBe"];
            });
        }

        private IEnumerable<string> allEvents(long version)
        {
            using (var session = _store.LightweightSession())
            {
                switch (_mode)
                {
                    case "Synchronously":
                        return session.Events.FetchStream(_lastStream, version).Select(x => x.Data.ToString()).ToArray();

                    case "Asynchronously":
                        return session.Events.FetchStreamAsync(_lastStream, version).GetAwaiter().GetResult().Select(x => x.Data.ToString()).ToArray();

                    case "In a batch":
                        throw new NotSupportedException("Not ready yet");
                }
            }

            using (var session = _store.LightweightSession())
            {
                return session.Events.FetchStream(_lastStream, version).Select(x => x.Data.ToString()).ToArray();
            }
        }

        public IGrammar HasAdditionalEvents()
        {
            return Embed<QuestEventFixture>("With events").Before(c => c.State.Store("streamId", _lastStream));
        }

        private static void rewriteEventTime(DateTime date, IDocumentSession session, Guid id)
        {
            // TODO -- let's rethink this one later
            session.Connection.CreateCommand().Sql("update mt_events set timestamp = :date where id = :id")
                .With("date", date.ToUniversalTime(), NpgsqlDbType.Timestamp)
                .With("id", id)
                .ExecuteNonQuery();

            session.SaveChanges();
        }

        [FormatAs("Live aggregating to QuestParty should be {returnValue}")]
        public string LiveAggregationToQueryPartyShouldBe()
        {
            using (var session = _store.OpenSession())
            {
                switch (_mode)
                {
                    case "Synchronously":
                        return session.Events.AggregateStream<QuestParty>(_lastStream).ToString();

                    case "Asynchronously":
                        return session.Events.AggregateStreamAsync<QuestParty>(_lastStream).GetAwaiter().GetResult().ToString();

                }

                throw new NotSupportedException();
            }
        }

        [FormatAs("Live aggregating to QuestParty at time {timestamp} should be {returnValue}")]
        public string LiveAggregationToQueryPartyByTimestampShouldBe(DateTime timestamp)
        {
            using (var session = _store.OpenSession())
            {
                switch (_mode)
                {
                    case "Synchronously":
                        return session.Events.AggregateStream<QuestParty>(_lastStream, timestamp: timestamp.ToUniversalTime()).ToString();

                    case "Asynchronously":
                        return session.Events.AggregateStreamAsync<QuestParty>(_lastStream, timestamp: timestamp.ToUniversalTime()).GetAwaiter().GetResult().ToString();

                }

                throw new NotSupportedException();
            }
        }

        [FormatAs("Live aggregating to QuestParty at version {version} should be {returnValue}")]
        public string LiveAggregationToQueryPartyVersionShouldBe(long version)
        {
            using (var session = _store.OpenSession())
            {
                switch (_mode)
                {
                    case "Synchronously":
                        return session.Events.AggregateStream<QuestParty>(_lastStream, version).ToString();

                    case "Asynchronously":
                        return session.Events.AggregateStreamAsync<QuestParty>(_lastStream, version).GetAwaiter().GetResult().ToString();

                }
            }

            throw new Exception();
        }
    }
}
