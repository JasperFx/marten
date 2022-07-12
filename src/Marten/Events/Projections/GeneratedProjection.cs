using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using LamarCompiler;
using Marten.Events.Daemon;
using Marten.Storage;

#nullable enable
namespace Marten.Events.Projections
{
    /// <summary>
    /// Base type for projection types that operate by code generation
    /// </summary>
    public abstract class GeneratedProjection: ProjectionBase, IProjectionSource, ICodeFile
    {
        protected GeneratedProjection(string projectionName)
        {
            ProjectionName = projectionName;
            Lifecycle = ProjectionLifecycle.Inline;
        }

        bool ICodeFile.AttachTypesSynchronously(GenerationRules rules, Assembly assembly, IServiceProvider services,
            string containingNamespace)
        {
            return tryAttachTypes(assembly, StoreOptions);
        }

        public string FileName => GetType().ToSuffixedTypeName("RuntimeSupport");



        public Type ProjectionType => GetType();

        void ICodeFile.AssembleTypes(GeneratedAssembly assembly)
        {
            assembleTypes(assembly, StoreOptions);
        }

        Task<bool> ICodeFile.AttachTypes(GenerationRules rules, Assembly assembly, IServiceProvider services, string containingNamespace)
        {
            var attached = tryAttachTypes(assembly, StoreOptions);
            return Task.FromResult(attached);
        }

        protected abstract void assembleTypes(GeneratedAssembly assembly, StoreOptions options);
        protected abstract bool tryAttachTypes(Assembly assembly, StoreOptions options);

        IProjection IProjectionSource.Build(DocumentStore store)
        {
            generateIfNecessary(store);

            return buildProjectionObject(store);
        }

        private bool _hasGenerated;

        private void generateIfNecessary(DocumentStore store)
        {
            if (_hasGenerated) return;

            StoreOptions = store.Options;
            var rules = store.Options.CreateGenerationRules();
            rules.ReferenceTypes(GetType());
            this.As<ICodeFile>().InitializeSynchronously(rules, store.Options.EventGraph, null);

            if (needsSettersGenerated())
            {
                assembleTypes(new GeneratedAssembly(rules), store.Options);
            }

            _hasGenerated = true;
        }

        protected abstract IProjection buildProjectionObject(DocumentStore store);

        protected abstract bool needsSettersGenerated();


        IReadOnlyList<AsyncProjectionShard> IProjectionSource.AsyncProjectionShards(DocumentStore store)
        {
            generateIfNecessary(store);
            StoreOptions = store.Options;

            // TODO -- this will have to change when we actually do sharding!!!
            var filters = BuildFilters(store);

            return new List<AsyncProjectionShard> {new(this, filters)};
        }


        public AsyncOptions Options { get; } = new AsyncOptions();

        internal StoreOptions StoreOptions { get; set; }

        internal virtual IEnumerable<string> ValidateConfiguration(StoreOptions options)
        {
            StoreOptions = options;

            // Nothing
            yield break;
        }

        ValueTask<EventRangeGroup> IProjectionSource.GroupEvents(DocumentStore store,
            IMartenDatabase daemonDatabase,
            EventRange range,
            CancellationToken cancellationToken)
        {
            generateIfNecessary(store);

            return groupEvents(store, daemonDatabase, range, cancellationToken);
        }

        protected abstract ValueTask<EventRangeGroup> groupEvents(DocumentStore store,
            IMartenDatabase daemonDatabase,
            EventRange range,
            CancellationToken cancellationToken);

    }
}
