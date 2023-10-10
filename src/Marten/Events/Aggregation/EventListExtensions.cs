using System;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Events.Aggregation;

public static class EventListExtensions
{
    public static void FanOutEventData<TSource, TChild>(this List<IEvent> events,
        Func<TSource, IEnumerable<TChild>> fanOutFunc)
    {
        FanOut<TSource, TChild>(events, source => fanOutFunc(source.Data));
    }

    public static void FanOut<TSource, TChild>(this List<IEvent> events, Func<IEvent<TSource>, IEnumerable<TChild>> processFunc)
    {
        var matches = events.OfType<Event<TSource>>().ToArray();
        var starting = 0;
        foreach (var source in matches)
        {
            var index = events.IndexOf(source, starting);
            var range = processFunc(source).Select(x => source.WithData(x)).ToArray();

            events.InsertRange(index + 1, range);

            starting = index + range.Length;
        }
    }
}
