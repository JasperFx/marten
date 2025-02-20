using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Core.Descriptions;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.CodeGeneration;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Events.Aggregation;

public abstract partial class AggregateProjectionBase<T>: ProjectionBase, IProjectionSource, IAggregateProjection,
    ILiveAggregatorSource<T>, IAggregateProjectionWithSideEffects<T>, IValidatedProjection
{
    private readonly Lazy<Type[]> _allEventTypes;

    private readonly AggregateVersioning<T> _versioning;
    internal IDocumentMapping _aggregateMapping;
    private IAggregationRuntime _runtime;
    private readonly AggregateApplication<T,IQuerySession> _application;

    protected AggregateProjectionBase(AggregationScope scope)
    {
        ProjectionName = typeof(T).NameInCode();
        _application = new AggregateApplication<T, IQuerySession>(this);

        Options.DeleteViewTypeOnTeardown<T>();

        _allEventTypes = new Lazy<Type[]>(determineEventTypes);

        _versioning = new AggregateVersioning<T>(scope){Inner = _application};

        RegisterPublishedType(typeof(T));

        if (typeof(T).TryGetAttribute<ProjectionVersionAttribute>(out var att))
        {
            ProjectionVersion = att.Version;
        }
    }

    IEnumerable<string> IValidatedProjection.ValidateConfiguration(StoreOptions options)
    {
        return ValidateConfiguration(options);
    }

    public AsyncOptions Options { get; } = new();

    // TODO -- duplicated with AggregationRuntime, and that's an ick.
    /// <summary>
    /// If more than 0 (the default), this is the maximum number of aggregates
    /// that will be cached in a 2nd level, most recently used cache during async
    /// projection. Use this to potentially improve async projection throughput
    /// </summary>
    public int CacheLimitPerTenant { get; set; } = 0;

    /// <summary>
    /// Use to create "side effects" when running an aggregation (single stream, custom projection, multi-stream)
    /// asynchronously in a continuous mode (i.e., not in rebuilds)
    /// </summary>
    /// <param name="operations"></param>
    /// <param name="slice"></param>
    /// <returns></returns>
    [JasperFxIgnore]
    public virtual ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<T> slice)
    {
        return new ValueTask();
    }

    public abstract bool IsSingleStream();

    internal IList<Type> DeleteEvents { get; } = new List<Type>();
    internal IList<Type> TransformedEvents { get; } = new List<Type>();

    Type IAggregateProjection.AggregateType => typeof(T);

    /// <summary>
    /// Template method that is called on the last event in a slice of events that
    /// are updating an aggregate. This was added specifically to add metadata like "LastModifiedBy"
    /// from the last event to an aggregate with user-defined logic. Override this for your own specific logic
    /// </summary>
    /// <param name="aggregate"></param>
    /// <param name="lastEvent"></param>
    public virtual T ApplyMetadata(T aggregate, IEvent lastEvent)
    {
        return aggregate;
    }

    object IMetadataApplication.ApplyMetadata(object aggregate, IEvent lastEvent)
    {
        if (aggregate is T t) return ApplyMetadata(t, lastEvent);

        return aggregate;
    }

    public bool AppliesTo(IEnumerable<Type> eventTypes)
    {
        return eventTypes
            .Intersect(AllEventTypes).Any() || eventTypes.Any(type => AllEventTypes.Any(type.CanBeCastTo));
    }

    public Type[] AllEventTypes => _allEventTypes.Value;

    public bool MatchesAnyDeleteType(IEventSlice slice)
    {
        return slice.Events().Select(x => x.EventType).Intersect(DeleteEvents).Any();
    }

    public bool MatchesAnyDeleteType(StreamAction action)
    {
        return action.Events.Select(x => x.EventType).Intersect(DeleteEvents).Any();
    }

    protected abstract object buildEventSlicer(StoreOptions options);

    /// <summary>
    ///     When used as an asynchronous projection, this opts into
    ///     only taking in events from streams explicitly marked as being
    ///     the aggregate type for this projection. Only use this if you are explicitly
    ///     marking streams with the aggregate type on StartStream()
    /// </summary>
    [MartenIgnore]
    public void FilterIncomingEventsOnStreamType()
    {
        StreamType = typeof(T);
    }

    public ValueTask<EventRangeGroup> GroupEvents(DocumentStore store, IMartenDatabase daemonDatabase, EventRange range,
        CancellationToken cancellationToken)
    {
        _runtime ??= BuildRuntime(store);

        return _runtime.GroupEvents(store, daemonDatabase, range, cancellationToken);
    }

    public Type ProjectionType => GetType();

    public abstract SubscriptionDescriptor Describe();

    protected virtual Type[] determineEventTypes()
    {
        var eventTypes = _application.AllEventTypes()
            .Concat(DeleteEvents).Concat(TransformedEvents).Distinct().ToArray();
        return eventTypes;
    }

    public virtual void ConfigureAggregateMapping(DocumentMapping mapping, StoreOptions storeOptions)
    {
        // nothing
    }

    public bool TryBuildReplayExecutor(DocumentStore store, IMartenDatabase database, out IReplayExecutor executor)
    {
        var projection = (IProjection)BuildRuntime(store);
        if (projection is IAggregationRuntime runtime)
        {
            return runtime.TryBuildReplayExecutor(store, database, out executor);
        }

        executor = default;
        return false;
    }

    IReadOnlyList<AsyncProjectionShard> IProjectionSource.AsyncProjectionShards(DocumentStore store)
    {
        StoreOptions = store.Options;

        return new List<AsyncProjectionShard> { new(this)
        {
            IncludeArchivedEvents = IncludeArchivedEvents,
            EventTypes = IncludedEventTypes,
            StreamType = StreamType
        } };
    }

    IProjection IProjectionSource.Build(DocumentStore store)
    {
        return BuildRuntime(store);
    }

    internal StoreOptions StoreOptions { get; set; }

    public IAggregator<T, IQuerySession> BuildAggregator(StoreOptions options)
    {
        return _versioning;
    }

    private void validateAndSetAggregateMapping(StoreOptions options)
    {
        _aggregateMapping = options.Storage.FindMapping(typeof(T));
        if (_aggregateMapping.IdMember == null)
        {
            throw new InvalidDocumentException(
                $"No identity property or field can be determined for the aggregate '{typeof(T).FullNameInCode()}', but one is required to be used as an aggregate in projections");
        }
    }

    internal IAggregationRuntime BuildRuntime(DocumentStore store)
    {
        validateAndSetAggregateMapping(store.Options);
        var storage = store.Options.Providers.StorageFor<T>().Lightweight;
        var slicer = buildEventSlicer(store.Options);
        var runtimeType = typeof(AggregationApplicationRuntime<,>).MakeGenericType(typeof(T), _aggregateMapping.IdType);
        var runtime =
            (IAggregationRuntime)Activator.CreateInstance(runtimeType, store, this, slicer, storage, _application);

        runtime.Versioning = _versioning;
        return runtime;
    }
}
