using System.Collections.Concurrent;

namespace Marten.ScaleTesting.Instrumentation;

/// <summary>
/// #4684 item 4: quantify the "cross-aggregate lookups per event" pathology from inside
/// projection user code. The harness owns its Telehealth projections, so the explicit lookup
/// call sites (<c>operations.LoadAsync</c>, the <c>EnrichUsingEntityQuery</c> escape hatch)
/// report here — one <see cref="Record"/> per Apply/Enrich invocation with the lookup count and
/// the event count it served.
///
/// Static + toggle-gated: the projection classes are constructed by the daemon, so there's no
/// clean DI path from the command into them. When <see cref="Enabled"/> is false (the default),
/// call sites pay one branch and allocate nothing.
///
/// Limitations, documented rather than hidden: the declarative <c>EnrichWith&lt;T&gt;()...AddReferences()</c>
/// lookups run inside JasperFx.Events and are NOT counted here — their round-trips do show up
/// in the per-batch <c>grouping</c> round-trip count from <see cref="BatchSpanSampler"/>.
/// </summary>
public static class LookupCounters
{
    private static readonly ConcurrentQueue<(string Projection, int Lookups, int Events)> _samples = new();

    /// <summary>Master toggle, flipped by RebuildInstrumentation. Off ⇒ Record is a no-op.</summary>
    public static bool Enabled { get; set; }

    public static void Record(string projection, int lookups, int events)
    {
        if (!Enabled)
        {
            return;
        }

        _samples.Enqueue((projection, lookups, events));
    }

    public static void Reset()
    {
        while (_samples.TryDequeue(out _))
        {
        }
    }

    public static IReadOnlyDictionary<string, LookupStats> Capture()
    {
        var byProjection = _samples
            .GroupBy(s => s.Projection)
            .ToDictionary(g => g.Key, g =>
            {
                var invocations = g.Count();
                var lookups = g.Sum(x => (long)x.Lookups);
                var events = g.Sum(x => (long)x.Events);

                // per-event ratio distribution across invocations
                var ratios = g
                    .Where(x => x.Events > 0)
                    .Select(x => (double)x.Lookups / x.Events)
                    .OrderBy(x => x)
                    .ToArray();

                return new LookupStats(
                    invocations,
                    lookups,
                    events,
                    events > 0 ? (double)lookups / events : 0,
                    PerEventPercentile(ratios, 0.50),
                    PerEventPercentile(ratios, 0.95));
            });

        return byProjection;
    }

    private static double PerEventPercentile(IReadOnlyList<double> sorted, double q)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var rank = (int)Math.Ceiling(q * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }
}

/// <summary>Lookup profile for one projection over a run.</summary>
public sealed record LookupStats(
    long Invocations,
    long Lookups,
    long Events,
    double LookupsPerEvent,
    double PerEventP50,
    double PerEventP95);
