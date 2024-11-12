using System;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events.CodeGeneration;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.CodeGeneration;
using Marten.Events.Daemon;
using Marten.Storage;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Projections.Flattened;

public partial class FlatTableProjection
{
    private readonly Lazy<IProjection> _generatedProjection;
    private Type? _generatedType;
    private GeneratedType _projectionType;


    public override ISubscriptionExecution BuildExecution(AsyncProjectionShard shard, DocumentStore store, IMartenDatabase database,
        ILogger logger)
    {
        throw new NotImplementedException();
    }

    protected override void assembleTypes(GeneratedAssembly assembly, StoreOptions options)
    {
        assembly.Rules.Assemblies.Add(GetType().Assembly);
        assembly.Rules.Assemblies.AddRange(_handlers.Select(x => x.EventType.Assembly).Distinct());

        var baseType = typeof(SyncEventProjectionBase);
        _projectionType = assembly.AddType(_inlineTypeName, baseType);

        var method = _projectionType.MethodFor(nameof(SyncEventProjectionBase.ApplyEvent));

        readSchema(options.EventGraph);
        var frames = _handlers
            .Select(x => new EventProcessingFrame(null, x.BuildFrame(options.EventGraph, Table))).ToList();

        var eventStatement = new EventTypePatternMatchFrame(frames);
        method.Frames.Add(eventStatement);
    }

    protected override bool tryAttachTypes(Assembly assembly, StoreOptions options)
    {
        _generatedType = assembly.GetExportedTypes().FirstOrDefault(x => x.Name == _inlineTypeName);
        return _generatedType != null;
    }

    protected override IProjection buildProjectionObject(DocumentStore store)
    {
        return _generatedProjection.Value;
    }

    protected override bool needsSettersGenerated()
    {
        return false;
    }
}
