#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
/// #5649. Two hazards in how tag types reach the store, both invisible until Native AOT.
/// <para>
/// <c>EventGraph.Build&lt;TDoc&gt;()</c> closed <c>SingleStreamProjection&lt;,&gt;</c> at runtime for a
/// <c>[BoundaryAggregate]</c> even though both type arguments are known at compile time, so ILC had
/// no such instantiation and the fetch failed with "No parameterless constructor defined".
/// </para>
/// <para>
/// <c>autoDiscoverTagTypesFromProjections</c> swallowed every exception alike, so the AOT failure of
/// <c>MakeGenericMethod</c> left a store running with the tag type absent and nothing anywhere
/// saying so. That half cannot be reproduced under the JIT, where the reflection always succeeds;
/// what is pinned here is the regression it risks, which is warning ordinary applications on every
/// start about identities there was never anything to discover from.
/// </para>
/// </summary>
public class Bug_5649_tag_type_discovery_diagnostics: OneOffConfigurationsContext
{
    [Fact]
    public void boundary_aggregate_builds_a_directly_constructed_single_stream_projection()
    {
        StoreOptions(_ => { });

        var source = theStore.Options.EventGraph.Build<SubscriptionState>();

        // The id is vestigial for a boundary aggregate and is unconditionally string; what matters
        // is that the closed type is the one the dispatcher looks up, however it was constructed.
        source.ShouldBeOfType<SingleStreamProjection<SubscriptionState, string>>();
    }

    [Fact]
    public void discovery_is_silent_for_a_store_it_has_nothing_to_complain_about()
    {
        var logger = new RecordingLogger();

        StoreOptions(opts =>
        {
            opts.DotNetLogger = logger;

            // One projection whose identity is a strong-typed id, which discovery registers, and
            // one keyed on a primitive, which it passes over before it reflects on anything.
            opts.Projections.Add<TicketSummaryProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<PlainAggregateProjection>(ProjectionLifecycle.Inline);
        });

        theStore.Events.FindTagType(typeof(TicketId)).ShouldNotBeNull();

        logger.Warnings.ShouldBeEmpty();
    }

    public record PlainHappened(Guid Id);

    public class PlainAggregate
    {
        public Guid Id { get; set; }
        public int Count { get; set; }
        public void Apply(PlainHappened _) => Count++;
    }

    public class PlainAggregateProjection: SingleStreamProjection<PlainAggregate, Guid>
    {
    }

    private class RecordingLogger: ILogger
    {
        private readonly ConcurrentQueue<string> _warnings = new();

        public string[] Warnings => _warnings.ToArray();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                _warnings.Enqueue(formatter(state, exception));
            }
        }

        private class NullScope: IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
