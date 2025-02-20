#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core.Descriptions;
using JasperFx.Core.Reflection;
using JasperFx.RuntimeCompiler;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Storage;

namespace Marten.Events.Projections;

/// <summary>
///     Base type for projection types that operate by code generation
/// </summary>
[Obsolete("Make this go away in V4")]
public abstract class GeneratedProjection: ProjectionBase, IProjectionSource, ICodeFile, IValidatedProjection
{
    protected bool _hasGenerated;

    protected GeneratedProjection(string projectionName)
    {
        ProjectionName = projectionName;
    }

    public abstract SubscriptionDescriptor Describe();

    internal StoreOptions StoreOptions { get; set; }

    bool ICodeFile.AttachTypesSynchronously(GenerationRules rules, Assembly assembly, IServiceProvider services,
        string containingNamespace)
    {
        return tryAttachTypes(assembly, StoreOptions);
    }

    public string FileName => GetType().ToSuffixedTypeName("RuntimeSupport");

    void ICodeFile.AssembleTypes(GeneratedAssembly assembly)
    {
        if (_hasGenerated)
            return;

        lock (_assembleLocker)
        {
            if (_hasGenerated)
                return;
            assembleTypes(assembly, StoreOptions);
            _hasGenerated = true;
        }
    }

    Task<bool> ICodeFile.AttachTypes(GenerationRules rules, Assembly assembly, IServiceProvider services,
        string containingNamespace)
    {
        var attached = tryAttachTypes(assembly, StoreOptions);
        return Task.FromResult(attached);
    }


    public Type ProjectionType => GetType();

    IProjection IProjectionSource.Build(DocumentStore store)
    {
        generateIfNecessary(store);

        return buildProjectionObject(store);
    }

    public bool TryBuildReplayExecutor(DocumentStore store, IMartenDatabase database, out IReplayExecutor executor)
    {
        generateIfNecessary(store);

        var projection = buildProjectionObject(store);
        if (projection is IAggregationRuntime runtime)
        {
            return runtime.TryBuildReplayExecutor(store, database, out executor);
        }

        executor = default;
        return false;
    }


    IReadOnlyList<AsyncProjectionShard> IProjectionSource.AsyncProjectionShards(DocumentStore store)
    {
        generateIfNecessary(store);
        StoreOptions = store.Options;

        return new List<AsyncProjectionShard> { new(this)
        {
            IncludeArchivedEvents = IncludeArchivedEvents,
            EventTypes = IncludedEventTypes,
            StreamType = StreamType
        } };
    }

    public AsyncOptions Options { get; } = new();

    ValueTask<EventRangeGroup> IProjectionSource.GroupEvents(DocumentStore store,
        IMartenDatabase daemonDatabase,
        EventRange range,
        CancellationToken cancellationToken)
    {
        generateIfNecessary(store);

        return groupEvents(store, daemonDatabase, range, cancellationToken);
    }

    protected abstract void assembleTypes(GeneratedAssembly assembly, StoreOptions options);
    protected abstract bool tryAttachTypes(Assembly assembly, StoreOptions options);

    private void generateIfNecessary(DocumentStore store)
    {
        lock (_assembleLocker)
        {
            if (_hasGenerated)
            {
                return;
            }

            generateIfNecessaryLocked();

            _hasGenerated = true;
        }

        return;

        void generateIfNecessaryLocked()
        {
            StoreOptions = store.Options;
            var rules = store.Options.CreateGenerationRules();
            rules.ReferenceTypes(GetType());
            this.As<ICodeFile>().InitializeSynchronously(rules, store.Options.EventGraph, null);

            if (!needsSettersGenerated())
            {
                return;
            }

            var generatedAssembly = new GeneratedAssembly(rules);
            assembleTypes(generatedAssembly, store.Options);

            // This will force it to create any setters or dynamic funcs
            generatedAssembly.GenerateCode();
        }
    }

    /// <summary>
    /// Prevent code generation bugs when multiple aggregates are code generated in parallel
    /// Happens more often on dynamic code generation
    /// </summary>
    protected static object _assembleLocker = new object();

    protected abstract IProjection buildProjectionObject(DocumentStore store);

    protected abstract bool needsSettersGenerated();

    IEnumerable<string> IValidatedProjection.ValidateConfiguration(StoreOptions options)
    {
        return ValidateConfiguration(options);
    }

    internal virtual IEnumerable<string> ValidateConfiguration(StoreOptions options)
    {
        StoreOptions = options;

        // Nothing
        yield break;
    }

    protected abstract ValueTask<EventRangeGroup> groupEvents(DocumentStore store,
        IMartenDatabase daemonDatabase,
        EventRange range,
        CancellationToken cancellationToken);
}
