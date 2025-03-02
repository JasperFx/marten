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
using JasperFx.Events.Daemon;
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
public abstract class EventProjection: JasperFxEventProjectionBase<IDocumentOperations, IQuerySession>, IValidatedProjection<StoreOptions>, IProjectionSchemaSource
{

    protected sealed override void storeEntity<T>(IDocumentOperations ops, T entity)
    {
        ops.Store(entity);
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

    public IEnumerable<string> ValidateConfiguration(StoreOptions options)
    {
        AssembleAndAssertValidity();

        return ArraySegment<string>.Empty;
    }
}
