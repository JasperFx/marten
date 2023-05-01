#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten.Events.Aggregation;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Events.Projections;

/// <summary>
///     Project a single document view across events that may span across
///     event streams in a user-defined grouping
/// </summary>
/// <typeparam name="TDoc"></typeparam>
/// <typeparam name="TId"></typeparam>
public abstract class MultiStreamProjection<TDoc, TId>: GeneratedAggregateProjectionBase<TDoc>
{
    private readonly EventSlicer<TDoc, TId> _defaultSlicer = new();

    private IEventSlicer<TDoc, TId>? _customSlicer;

    protected MultiStreamProjection(): base(AggregationScope.MultiStream)
    {
    }

    internal IEventSlicer<TDoc, TId> Slicer => _customSlicer ?? _defaultSlicer;

    protected override Type[] determineEventTypes()
    {
        return base.determineEventTypes().Concat(_defaultSlicer.DetermineEventTypes())
            .Distinct().ToArray();
    }

    public void Identity<TEvent>(Func<TEvent, TId> identityFunc)
    {
        if (_customSlicer != null)
        {
            throw new InvalidOperationException(
                "There is already a custom event slicer registered for this projection");
        }

        _defaultSlicer.Identity(identityFunc);
    }

    public void Identities<TEvent>(Func<TEvent, IReadOnlyList<TId>> identitiesFunc)
    {
        if (_customSlicer != null)
        {
            throw new InvalidOperationException(
                "There is already a custom event slicer registered for this projection");
        }

        _defaultSlicer.Identities(identitiesFunc);
    }

    /// <summary>
    ///     Apply a custom event grouping strategy for events. This is additive to Identity() or Identities()
    /// </summary>
    /// <param name="grouper"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void CustomGrouping(IAggregateGrouper<TId> grouper)
    {
        if (_customSlicer != null)
        {
            throw new InvalidOperationException(
                "There is already a custom event slicer registered for this projection");
        }

        _defaultSlicer.CustomGrouping(grouper);
    }

    /// <summary>
    ///     If your grouping of events to aggregates doesn't fall into any simple pattern supported
    ///     directly by ViewProjection, supply your own "let me do whatever I want" event slicer
    /// </summary>
    /// <param name="slicer"></param>
    public void CustomGrouping(IEventSlicer<TDoc, TId> slicer)
    {
        _customSlicer = slicer;
    }


    protected override void specialAssertValid()
    {
        if (_customSlicer == null && !_defaultSlicer.HasAnyRules())
        {
            throw new InvalidProjectionException(
                $"ViewProjection {GetType().FullNameInCode()} has no Identity() rules defined or registered lookup grouping rules and does not know how to identify event membership in the aggregated document {typeof(TDoc).FullNameInCode()}");
        }
    }

    /// <summary>
    ///     Apply "fan out" operations to the given TEvent type that inserts an enumerable of TChild events right behind the
    ///     parent
    ///     event in the event stream
    /// </summary>
    /// <param name="fanOutFunc"></param>
    /// <param name="mode">Should the fan out operation happen after grouping, or before? Default is after</param>
    /// <typeparam name="TEvent"></typeparam>
    /// <typeparam name="TChild"></typeparam>
    public void FanOut<TEvent, TChild>(Func<TEvent, IEnumerable<TChild>> fanOutFunc,
        FanoutMode mode = FanoutMode.AfterGrouping)
    {
        _defaultSlicer.FanOut(fanOutFunc, mode);
    }

    protected override object buildEventSlicer(StoreOptions options)
    {
        if (_customSlicer != null)
        {
            return _customSlicer;
        }

        var mapping = options.Storage.MappingFor(typeof(TDoc));
        var aggregateStyle = mapping.TenancyStyle;
        var eventStyle = options.Events.TenancyStyle;

        switch (aggregateStyle)
        {
            case TenancyStyle.Conjoined when eventStyle == TenancyStyle.Conjoined:
                _defaultSlicer.GroupByTenant();
                break;
            case TenancyStyle.Conjoined:
                throw new InvalidProjectionException(
                    $"Aggregate {typeof(TDoc).FullNameInCode()} is multi-tenanted, but the events are not");
        }

        return _defaultSlicer;
    }

    protected override IEnumerable<string> validateDocumentIdentity(StoreOptions options, DocumentMapping mapping)
    {
        yield break;
    }

    protected override Type baseTypeForAggregationRuntime()
    {
        return typeof(CrossStreamAggregationRuntime<,>).MakeGenericType(typeof(TDoc), typeof(TId));
    }
}

[Obsolete("Please switch to MultiStreamProjection<T> with the exact same syntax")]
public abstract class MultiStreamAggregation<TDoc, TId>: MultiStreamProjection<TDoc, TId>
{
}

[Obsolete("Please switch to MultiStreamProjection<T> with the exact same syntax")]
public abstract class ViewProjection<TDoc, TId>: MultiStreamProjection<TDoc, TId>
{
}
