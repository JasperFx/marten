using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.Resiliency;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.Events;

namespace DaemonTests.TestingSupport;

public class TripPublisher
{
    public List<object> Events { get; set; } = new();


}

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

    public static readonly string[] States = new string[] {"Texas", "Arkansas", "Missouri", "Kansas", "Oklahoma", "Connecticut", "New Jersey", "New York" };

    public static string RandomState()
    {
        var index = Random.Shared.Next(0, States.Length - 1);
        return States[index];
    }

    public static Direction RandomDirection()
    {
        var index = Random.Shared.Next(0, 3);
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

    public static TimeOnly RandomTime()
    {
        var hour = Random.Shared.Next(0, 24);
        return new TimeOnly(hour, 0, 0);
    }

    public Guid StreamId = CombGuidIdGeneration.NewGuid();

    public readonly List<object> Events = new List<object>();

    public TripStream(List<object> events)
    {
        Events = events;
    }

    public TripStream()
    {
        var random = Random.Shared;
        var startDay = random.Next(1, 100);

        var start = new TripStarted {Day = startDay};
        Events.Add(start);


        var state = RandomState();

        Events.Add(new Departure{Day = startDay, State = state});

        var duration = random.Next(1, 20);

        var randomNumber = random.NextDouble();
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

        Events.Add(new FailingEvent());

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

    public static async Task PublishMultiplesSimple(IDocumentSession session, TripStream[] streams)
    {
        var list = streams.ToList();
        while (list.Any())
        {
            foreach (var stream in list.ToArray())
            {
                if (stream.TryCheckOutEvents(out var events))
                {
                    if (stream.Started)
                    {
                        session.Events.Append(stream.StreamId, events);
                    }
                    else
                    {
                        stream.Started = true;
                        session.Events.StartStream<Trip>(stream.StreamId, events);
                    }
                }

                if (stream.IsFinishedPublishing())
                {
                    list.Remove(stream);
                }
            }

            await session.SaveChangesAsync();
        }
    }

    public bool Started { get; private set; }

    public async Task PublishSingleFileSimple(IDocumentSession session)
    {
        while (TryAppendSimple(session))
        {
            await session.SaveChangesAsync();
        }
    }

    public async Task PublishSingleFileWithFetchForWriting(IDocumentSession session)
    {
        while (await TryAppendWithFetchForWriting(session))
        {
            await session.SaveChangesAsync();
        }
    }

    public async Task<bool> TryAppendWithFetchForWriting(IDocumentSession session)
    {
        if (TryCheckOutEvents(out var events))
        {
            if (Started)
            {
                var stream = await session.Events.FetchForWriting<Trip>(StreamId);
                stream.AppendMany(events);
            }
            else
            {
                session.Events.StartStream<Trip>(StreamId, events);
                Started = true;
            }
        }

        return !IsFinishedPublishing();
    }

    public bool TryAppendSimple(IDocumentSession session)
    {
        if (TryCheckOutEvents(out var events))
        {
            if (Started)
            {
                session.Events.Append(StreamId, events);
            }
            else
            {
                session.Events.StartStream<Trip>(StreamId, events);
                Started = true;
            }
        }

        return !IsFinishedPublishing();
    }

    public bool TryCheckOutEvents(out object[] events)
    {
        if (IsFinishedPublishing())
        {
            events = new object[0];
            return false;
        }

        var number = Random.Shared.Next(1, 5);

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

    public StreamAction ToAction(EventGraph graph)
    {
        return StreamAction.Start(graph, StreamId, Events.ToArray());
    }

    public string TenantId { get; set; }

    public TripStream TravelIsUnder(int miles)
    {
        var movements = Events.OfType<Travel>().SelectMany(x => x.Movements).ToArray();
        foreach (var movement in movements)
        {
            if (movements.Sum(x => x.Distance) > miles)
            {
                movement.Distance = 0.1;
            }
        }

        return this;
    }

    public TripStream TravelIsOver(int miles)
    {
        var movements = Events.OfType<Travel>().SelectMany(x => x.Movements).ToArray();
        movements[0].Distance = miles + 1;
        return this;
    }
}
