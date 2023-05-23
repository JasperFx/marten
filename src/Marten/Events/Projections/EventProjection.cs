using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten.Events.CodeGeneration;
using Marten.Events.Daemon;
using Marten.Storage;
using Weasel.Core;

namespace Marten.Events.Projections;

/// <summary>
///     This is the "do anything" projection type
/// </summary>
public abstract partial class EventProjection: GeneratedProjection, IProjectionSchemaSource
{
    private readonly CreateMethodCollection _createMethods;

    private readonly Lazy<IProjection> _generatedProjection;
    private readonly string _inlineTypeName;
    private readonly ProjectMethodCollection _projectMethods;
    private Type _generatedType;

    private GeneratedType _inlineType;
    private bool _isAsync;

    public EventProjection(): base("Projections")
    {
        _projectMethods = new ProjectMethodCollection(GetType());
        _createMethods = new CreateMethodCollection(GetType());

        ProjectionName = GetType().FullNameInCode();
        _inlineTypeName = GetType().ToSuffixedTypeName("InlineProjection");

        _generatedProjection = new Lazy<IProjection>(() =>
        {
            if (_generatedType == null)
            {
                throw new InvalidOperationException("The EventProjection has not created its inner IProjection");
            }

            var projection = (IProjection)Activator.CreateInstance(_generatedType, this);
            foreach (var setter in _inlineType.Setters) setter.SetInitialValue(projection);

            return projection;
        });
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


    protected override bool needsSettersGenerated()
    {
        return _inlineType == null;
    }

    protected override ValueTask<EventRangeGroup> groupEvents(DocumentStore store, IMartenDatabase daemonDatabase,
        EventRange range,
        CancellationToken cancellationToken)
    {
        return new ValueTask<EventRangeGroup>(
            new TenantedEventRangeGroup(
                store,
                daemonDatabase,
                _generatedProjection.Value,
                Options,
                range,
                cancellationToken
            )
        );
    }


    [MartenIgnore]
    public void Project<TEvent>(Action<TEvent, IDocumentOperations> project)
    {
        _projectMethods.AddLambda(project, typeof(TEvent));
    }

    [MartenIgnore]
    public void ProjectAsync<TEvent>(Func<TEvent, IDocumentOperations, Task> project)
    {
        _projectMethods.AddLambda(project, typeof(TEvent));
    }
}

public abstract class SyncEventProjection<T>: SyncEventProjectionBase where T : EventProjection
{
    public SyncEventProjection(T projection)
    {
        Projection = projection;
    }

    public T Projection { get; }
}

public abstract class AsyncEventProjection<T>: AsyncEventProjectionBase where T : EventProjection
{
    public AsyncEventProjection(T projection)
    {
        Projection = projection;
    }

    public T Projection { get; }
}
