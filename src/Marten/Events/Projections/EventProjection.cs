using System;
using System.Collections.Generic;
using System.Linq;
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
using Marten.Storage;
using Weasel.Core;

namespace Marten.Events.Projections;

/// <summary>
///     This is the "do anything" projection type
/// </summary>
public abstract class EventProjection: ProjectionBase, IValidatedProjection, IProjection, IProjectionSource, IProjectionSchemaSource, IProjectionStorage<IDocumentOperations>
{
    private readonly EventProjectionApplication<IDocumentOperations> _application;

    public EventProjection()
    {
        _application = new EventProjectionApplication<IDocumentOperations>(this);

        IncludedEventTypes.Fill(_application.AllEventTypes());

        foreach (var publishedType in _application.PublishedTypes())
        {
            RegisterPublishedType(publishedType);
        }

        ProjectionName = GetType().FullNameInCode();
    }

    public void Store<T>(IDocumentOperations ops, T entity)
    {
        ops.Store(entity);
    }

    public Type ProjectionType => GetType();
    public AsyncOptions Options { get; } = new();
    public IReadOnlyList<AsyncProjectionShard> AsyncProjectionShards(DocumentStore store)
    {
        return new List<AsyncProjectionShard> { new(this)
        {
            IncludeArchivedEvents = IncludeArchivedEvents,
            EventTypes = IncludedEventTypes,
            StreamType = StreamType
        } };
    }

    public ValueTask<EventRangeGroup> GroupEvents(DocumentStore store, IMartenDatabase daemonDatabase, EventRange range,
        CancellationToken cancellationToken)
    {
        return new ValueTask<EventRangeGroup>(
            new TenantedEventRangeGroup(
                store,
                daemonDatabase,
                this,
                Options,
                range,
                cancellationToken
            )
        );
    }

    public async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
    {
        foreach (var e in streams.SelectMany(x => x.Events).OrderBy(x => x.Sequence))
        {
            await _application.ApplyAsync(operations, e, cancellation).ConfigureAwait(false);
        }
    }

    public IProjection Build(DocumentStore store)
    {
        return this;
    }

    public bool TryBuildReplayExecutor(DocumentStore store, IMartenDatabase database, out IReplayExecutor executor)
    {
        executor = default;
        return false;
    }

    public SubscriptionDescriptor Describe()
    {
        return new SubscriptionDescriptor(this, SubscriptionType.EventProjection);
    }

    /// <summary>
    ///     Use to register additional or custom schema objects like database tables that
    ///     will be used by this projection. Originally meant to support projecting to flat
    ///     tables
    /// </summary>
    public IList<ISchemaObject> SchemaObjects { get; } = new List<ISchemaObject>();

    IEnumerable<ISchemaObject> IProjectionSchemaSource.CreateSchemaObjects(EventGraph events)
    {
        return SchemaObjects;
    }

    [MartenIgnore]
    public void Project<TEvent>(Action<TEvent, IDocumentOperations> project) where TEvent : class
    {
        _application.Project<TEvent>(project);
    }

    [MartenIgnore]
    public void ProjectAsync<TEvent>(Func<TEvent, IDocumentOperations, Task> project) where TEvent : class
    {
        _application.ProjectAsync<TEvent>(project);
    }

    public IEnumerable<string> ValidateConfiguration(StoreOptions options)
    {
        _application.AssertMethodValidity();

        return ArraySegment<string>.Empty;
    }

    internal override void AssembleAndAssertValidity()
    {
        _application.AssertMethodValidity();
    }
}
