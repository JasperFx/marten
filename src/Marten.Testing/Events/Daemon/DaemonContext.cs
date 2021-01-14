using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Testing.Events.Daemon
{
    public abstract class DaemonContext
    {

    }

    public class TripEventPublisher
    {
        private readonly List<TripStream> _streams;

        public TripEventPublisher(int numberOfStreams)
        {
            NumberOfStreams = numberOfStreams;

            _streams = TripStream.RandomStreams(numberOfStreams);
        }

        public int NumberOfStreams { get;}

        public async Task PublishSingleThreaded(DocumentStore store)
        {
            foreach (var stream in _streams)
            {
                using (var session = store.LightweightSession())
                {
                    session.Events.StartStream(stream.StreamId, stream.Events);
                    await session.SaveChangesAsync();
                }
            }
        }

        public Task PublishMultiThreaded(DocumentStore store, int threads)
        {
            foreach (var stream in _streams)
            {
                stream.Reset();
            }

            var publishers = createPublishers(threads);

            var tasks = publishers.Select(x => x.PublishAll());
            return Task.WhenAll(tasks);
        }

        private List<EventPublisher> createPublishers(int threads)
        {
            var streamsPerPublisher = (int) Math.Floor((double) _streams.Count / threads);
            var index = 0;

            var publishers = new List<EventPublisher>();
            foreach (var publisher in publishers)
            {
                publisher.Streams.AddRange(_streams.GetRange(index, index + streamsPerPublisher));
                index += streamsPerPublisher;
            }

            if (index < _streams.Count - 1)
            {
                publishers.Last().Streams.Add(_streams.Last());
            }

            return publishers;
        }

        public class EventPublisher
        {
            private readonly DocumentStore _store;

            public EventPublisher(DocumentStore store)
            {
                _store = store;
            }

            public IList<TripStream> Streams { get; } = new List<TripStream>();

            public Task PublishAll()
            {
                return Task.Factory.StartNew(publishAll);
            }

            private async Task publishAll()
            {
                while (Streams.Any() && !Streams.All(x => x.IsFinishedPublishing()))
                {
                    using (var session = _store.LightweightSession())
                    {
                        foreach (var stream in Streams)
                        {
                            if (stream.TryCheckOutEvents(out var events))
                            {
                                session.Events.Append(stream.StreamId, events);
                            }
                        }

                        await session.SaveChangesAsync();
                    }
                }

                Streams.RemoveAll(s => s.IsFinishedPublishing());

            }
        }
    }

    public class TripStarted
    {
        public int Day { get; set; }
    }

    public class TripEnded
    {
        public int Day { get; set; }
        public string State { get; set; }
    }


    public class Arrival
    {
        public int Day { get; set; }
        public string State { get; set; }
    }

    public class Departure
    {
        public int Day { get; set; }
        public string State { get; set; }
    }

    public class TripAborted
    {

    }

    public enum Direction
    {
        North,
        South,
        East,
        West
    }

    public class Travel
    {
        public static Travel Random(int day)
        {
            var travel = new Travel {Day = day,};

            var length = TripStream.Random.Next(1, 20);
            for (var i = 0; i < length; i++)
            {
                var movement = new Movement
                {
                    Direction = TripStream.RandomDirection(), Distance = TripStream.Random.NextDouble() * 100
                };

                travel.Movements.Add(movement);
            }

            return travel;
        }

        public int Day { get; set; }
        public IList<Movement> Movements { get; set; } = new List<Movement>();

        public double TotalDistance()
        {
            return Movements.Sum(x => x.Distance);
        }
    }

    public class Movement
    {
        public Direction Direction
        {
            get;
            set;

        }

        public double Distance { get; set; }
    }
}
