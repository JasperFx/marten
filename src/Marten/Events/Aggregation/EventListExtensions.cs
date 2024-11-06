using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Events;

namespace Marten.Events.Aggregation;

public static class EventListExtensions
{
    public static void FanOut<TSource, TChild>(this List<IEvent> events,
        Func<TSource, IEnumerable<TChild>> fanOutFunc)
    {
        FanOut<TSource, TChild>(events, source => fanOutFunc(source.Data));
    }

    public static void FanOut<TSource, TChild>(this List<IEvent> events, Func<IEvent<TSource>, IEnumerable<TChild>> fanOutFunc)
    {
        var matches = events.OfType<Event<TSource>>().ToArray();
        var starting = 0;
        foreach (var source in matches)
        {
            var index = events.IndexOf(source, starting);
            var range = fanOutFunc(source).Select(x => source.WithData(x)).ToArray();

            events.InsertRange(index + 1, range);

            starting = index + range.Length;
        }
    }
}
