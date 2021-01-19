using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Marten.Testing.Events.Daemon.TestingSupport
{
    public class TripStream
    {
        public static List<TripStream> RandomStreams(int number)
        {
            var list = new List<TripStream>();
            for (var i = 0; i < number; i++)
            {
                var stream = new TripStream();
                list.Add(stream);
            }

            return list;
        }

        public static readonly Random Random = new Random();
        public static readonly string[] States = new string[] {"Texas", "Arkansas", "Missouri", "Kansas", "Oklahoma", "Connecticut", "New Jersey", "New York" };

        public static string RandomState()
        {
            var index = Random.Next(0, States.Length - 1);
            return States[index];
        }

        public static Direction RandomDirection()
        {
            var index = Random.Next(0, 3);
            switch (index)
            {
                case 0:
                    return Direction.East;
                case 1:
                    return Direction.North;
                case 2:
                    return Direction.South;
                default:
                    return Direction.West;
            }
        }

        public Guid StreamId = Guid.NewGuid();

        public readonly List<object> Events = new List<object>();



        public TripStream()
        {
            var startDay = Random.Next(1, 100);

            var start = new TripStarted {Day = startDay};
            Events.Add(start);


            var state = RandomState();

            Events.Add(new Departure{Day = startDay, State = state});

            var duration = Random.Next(1, 20);

            var randomNumber = Random.NextDouble();
            for (var i = 0; i < duration; i++)
            {
                var day = startDay + i;

                var travel = Travel.Random(day);
                Events.Add(travel);

                if (i > 0 && randomNumber > .3)
                {
                    var departure = new Departure {Day = day, State = state};

                    Events.Add(departure);

                    state = RandomState();

                    var arrival = new Arrival {State = state, Day = i};
                    Events.Add(arrival);
                }
            }

            if (randomNumber > .5)
            {
                Events.Add(new TripEnded
                {
                    Day = startDay + duration,
                    State = state
                });
            }
            else if (randomNumber > .9)
            {
                Events.Add(new TripAborted());
            }


        }

        public bool IsFinishedPublishing()
        {
            return _index >= Events.Count;
        }

        private readonly Mutex _mutex = new Mutex();
        private int _index;


        public void Reset()
        {
            _index = 0;
        }

        public bool TryCheckOutEvents(out object[] events)
        {
            if (IsFinishedPublishing())
            {
                events = new object[0];
                return false;
            }

            var number = Random.Next(1, 5);

            if (_index + number >= Events.Count)
            {
                events = Events.Skip(_index).ToArray();
            }
            else
            {
                events = Events.GetRange(_index, number).ToArray();
            }

            _index += number;
            return true;
        }

        public Trip Expected { get; set; }
    }
}
