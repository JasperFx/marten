using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using LamarCompiler;
using Marten.Events.CodeGeneration;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Storage;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Marten.Storage;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Aggregation
{
    public partial class AggregateProjection<T> : ProjectionSource, ILiveAggregatorSource<T>
    {
        private GeneratedType _liveType;
        private GeneratedType _inlineType;
        private DocumentMapping _aggregateMapping;

        private readonly CreateMethodCollection _createMethods;
        private readonly ApplyMethodCollection _applyMethods;
        private readonly ShouldDeleteMethodCollection _shouldDeleteMethods;
        private bool _isAsync;
        private GeneratedAssembly _assembly;
        private Type _storageType;

        private readonly Lazy<Type[]> _allEventTypes;
        private IAggregationRuntime _runtime;

        public AggregateProjection() : base(typeof(T).NameInCode())
        {
            _createMethods = new CreateMethodCollection(GetType(), typeof(T));
            _applyMethods = new ApplyMethodCollection(GetType(), typeof(T));
            _shouldDeleteMethods = new ShouldDeleteMethodCollection(GetType(), typeof(T));

            ProjectionName = typeof(T).Name;

            Options.DeleteViewTypeOnTeardown<T>();

            _allEventTypes = new Lazy<Type[]>(() =>
            {
                return _createMethods.Methods.Concat(_applyMethods.Methods).Concat(_shouldDeleteMethods.Methods)
                    .Select(x => x.EventType).Concat(DeleteEvents).Distinct().ToArray();
            });


        }

        public override Type ProjectionType => GetType();

        public bool AppliesTo(IEnumerable<Type> eventTypes)
        {
            return eventTypes
                .Intersect(AllEventTypes).Any() || eventTypes.Any(type => AllEventTypes.Any(type.CanBeCastTo));
        }

        public Type[] AllEventTypes => _allEventTypes.Value;

        Type IAggregateProjection.AggregateType => typeof(T);

        internal override IProjection Build(DocumentStore store)
        {
            if (_inlineType == null)
            {
                Compile(store.Options);
            }

            // This will have to change when we introduce 1st class support for tenancy by
            // separate databases
            store.Tenancy.Default.EnsureStorageExists(typeof(T));
            return BuildRuntime(store);
        }

        internal ILiveAggregator<T> BuildLiveAggregator()
        {
            var aggregator = (ILiveAggregator<T>)Activator.CreateInstance(_liveType.CompiledType, this);
            _liveType.ApplySetterValues(aggregator);

            return aggregator;
        }

        internal IAggregationRuntime BuildRuntime(DocumentStore store)
        {
            if (_liveType == null)
            {
                Compile(store.Options);
            }

            var storage = store.Options.Providers.StorageFor<T>().Lightweight;
            var slicer = buildEventSlicer(store.Options);

            var ctor = _inlineType.CompiledType.GetConstructors().Single();
            foreach (var parameter in ctor.GetParameters())
            {
                Debug.WriteLine(parameter.ParameterType.NameInCode());
            }

            var inline = (IAggregationRuntime)Activator.CreateInstance(_inlineType.CompiledType, store, this, slicer, store.Options.Tenancy, storage, this);
            _inlineType.ApplySetterValues(inline);

            return inline;
        }

        protected virtual object buildEventSlicer(StoreOptions documentMapping)
        {
            Type slicerType = null;
            if (_aggregateMapping.IdType == typeof(Guid))
            {
                slicerType = typeof(ByStreamId<>).MakeGenericType(_aggregateMapping.DocumentType);
            }
            else if (_aggregateMapping.IdType != typeof(string))
            {
                throw new ArgumentOutOfRangeException(
                    $"{_aggregateMapping.IdType.FullNameInCode()} is not a supported stream id type for aggregate {_aggregateMapping.DocumentType.FullNameInCode()}");
            }
            else
            {
                slicerType = typeof(ByStreamKey<>).MakeGenericType(_aggregateMapping.DocumentType);
            }

            return Activator.CreateInstance(slicerType);
        }


        internal string SourceCode()
        {
            var writer = new StringWriter();
            writer.WriteLine(_liveType.SourceCode);
            writer.WriteLine();

            writer.WriteLine(_inlineType.SourceCode);
            writer.WriteLine();

            return writer.ToString();
        }

        internal GeneratedAssembly Compile(StoreOptions options)
        {
            _assembly = new GeneratedAssembly(new GenerationRules(SchemaConstants.MartenGeneratedNamespace));

            _assembly.Generation.Assemblies.Add(GetType().Assembly);
            _assembly.Generation.Assemblies.Add(typeof(T).Assembly);
            _assembly.Generation.Assemblies.AddRange(_applyMethods.ReferencedAssemblies());
            _assembly.Generation.Assemblies.AddRange(_createMethods.ReferencedAssemblies());
            _assembly.Generation.Assemblies.AddRange(_shouldDeleteMethods.ReferencedAssemblies());

            _assembly.Namespaces.Add("System");
            _assembly.Namespaces.Add("System.Linq");

            _isAsync = _createMethods.IsAsync || _applyMethods.IsAsync;

            _aggregateMapping = options.Storage.MappingFor(typeof(T));


            if (_aggregateMapping.IdMember == null)
            {
                throw new InvalidDocumentException(
                    $"No identity property or field can be determined for the aggregate '{typeof(T).FullNameInCode()}', but one is required to be used as an aggregate in projections");
            }

            _storageType = typeof(IDocumentStorage<,>).MakeGenericType(typeof(T), _aggregateMapping.IdType);


            buildLiveAggregationType();
            buildInlineAggregationType();

            var assemblyGenerator = new AssemblyGenerator();

            assemblyGenerator.ReferenceAssembly(typeof(IMartenSession).Assembly);
            assemblyGenerator.Compile(_assembly);

            Debug.WriteLine(_liveType.SourceCode);

            return _assembly;
        }

        private void buildInlineAggregationType()
        {
            var inlineBaseType =
                typeof(AggregationRuntime<,>).MakeGenericType(typeof(T), _aggregateMapping.IdType);

            _inlineType = _assembly.AddType(GetType().NameInCode().Sanitize() + "InlineHandler", inlineBaseType);

            _createMethods.BuildCreateMethod(_inlineType, _aggregateMapping);

            _inlineType.AllInjectedFields.Add(new InjectedField(GetType()));

            buildApplyEventMethod();

            _inlineType.Setters.AddRange(_applyMethods.Setters());
            _inlineType.Setters.AddRange(_createMethods.Setters());
            _inlineType.Setters.AddRange(_shouldDeleteMethods.Setters());
        }

        private GeneratedMethod buildApplyEventMethod()
        {
            var method = _inlineType.MethodFor(nameof(AggregationRuntime<string, string>.ApplyEvent));

            // This gets you the EventSlice aggregate Id

            method.DerivedVariables.Add(new Variable(_aggregateMapping.IdType, $"slice.{nameof(EventSlice<string, string>.Id)}"));
            method.DerivedVariables.Add(Variable.For<ITenant>($"slice.{nameof(EventSlice<string, string>.Tenant)}"));
            method.DerivedVariables.Add(Variable.For<ITenant>($"slice.{nameof(EventSlice<string, string>.Tenant)}"));
            method.DerivedVariables.Add(Variable.For<IEvent>("@event"));
            method.DerivedVariables.Add(Variable.For<IMartenSession>($"({typeof(IMartenSession).FullNameInCode()})session"));
            method.DerivedVariables.Add(Variable.For<IQuerySession>("session"));
            method.DerivedVariables.Add(
                Variable.For<IAggregateProjection>(nameof(AggregationRuntime<string, string>.Projection)));


            var eventHandlers = new LightweightCache<Type, AggregateEventProcessingFrame>(
                eventType => new AggregateEventProcessingFrame(typeof(T), eventType));

            foreach (var deleteEvent in DeleteEvents)
            {
                eventHandlers[deleteEvent].AlwaysDeletes = true;
            }

            foreach (var slot in _applyMethods.Methods)
            {
                eventHandlers[slot.EventType].Apply = new ApplyMethodCall(slot);
            }

            foreach (var slot in _createMethods.Methods)
            {
                eventHandlers[slot.EventType].CreationFrame = slot.Method is ConstructorInfo
                    ? (Frame)new AggregateConstructorFrame(slot)
                    : new CreateAggregateFrame(slot);
            }

            foreach (var slot in _shouldDeleteMethods.Methods)
            {
                eventHandlers[slot.EventType].Deletion = new ShouldDeleteFrame(slot);
            }

            var patternMatching = new EventTypePatternMatchFrame(eventHandlers.OfType<EventProcessingFrame>().ToList());
            method.Frames.Add(patternMatching);

            method.Frames.Code($"return aggregate;");

            return method;
        }

        private void buildLiveAggregationType()
        {
            var liveBaseType = _isAsync
                ? typeof(AsyncLiveAggregatorBase<>)
                : typeof(SyncLiveAggregatorBase<>);

            liveBaseType = liveBaseType.MakeGenericType(typeof(T));


            _liveType =
                _assembly.AddType(GetType().NameInCode().Sanitize() + "LiveAggregation", liveBaseType);

            var overrideMethodName = _isAsync ? "BuildAsync" : "Build";
            var buildMethod = _liveType.MethodFor(overrideMethodName);

            buildMethod.DerivedVariables.Add(Variable.For<IQuerySession>("(IQuerySession)session"));

            buildMethod.Frames.Code("if (!events.Any()) return null;");

            buildMethod.Frames.Add(new DeclareAggregateFrame(typeof(T)));


            var callCreateAggregateFrame = new CallCreateAggregateFrame(_createMethods);

            // This is the existing snapshot passed into the LiveAggregator
            var snapshot = buildMethod.Arguments.Single(x => x.VariableType == typeof(T));
            callCreateAggregateFrame.CoalesceAssignTo(snapshot);

            buildMethod.Frames.Add(callCreateAggregateFrame);
            buildMethod.Frames.Add(new CallApplyAggregateFrame(_applyMethods){InsideForEach = true});

            buildMethod.Frames.Return(typeof(T));

            _liveType.AllInjectedFields.Add(new InjectedField(GetType()));

            _createMethods.BuildCreateMethod(_liveType, _aggregateMapping);
            _applyMethods.BuildApplyMethod(_liveType, _aggregateMapping);

            _liveType.Setters.AddRange(_applyMethods.Setters());
            _liveType.Setters.AddRange(_createMethods.Setters());
            _liveType.Setters.AddRange(_shouldDeleteMethods.Setters());
        }


        public ILiveAggregator<T> Build(StoreOptions options)
        {
            if (_liveType == null)
            {
                Compile(options);
            }

            return BuildLiveAggregator();
        }

        internal override void AssertValidity()
        {
            if (_applyMethods.IsEmpty() && _createMethods.IsEmpty())
            {
                throw new InvalidProjectionException(
                    $"AggregateProjection for {typeof(T).FullNameInCode()} has no valid create or apply operations");
            }

            var invalidMethods = MethodCollection.FindInvalidMethods(GetType(), _applyMethods, _createMethods, _shouldDeleteMethods);

            if (invalidMethods.Any())
            {
                throw new InvalidProjectionException(this, invalidMethods);
            }

            specialAssertValid();
        }

        protected virtual void specialAssertValid()
        {
        }

        internal override IEnumerable<string> ValidateConfiguration(StoreOptions options)
        {
            var mapping = options.Storage.MappingFor(typeof(T));
            foreach (var p in validateDocumentIdentity(options, mapping)) yield return p;

            if (options.Events.TenancyStyle != mapping.TenancyStyle)
            {
                yield return
                    $"Tenancy storage style mismatch between the events ({options.Events.TenancyStyle}) and the aggregate type {typeof(T).FullNameInCode()} ({mapping.TenancyStyle})";
            }
        }

        protected virtual IEnumerable<string> validateDocumentIdentity(StoreOptions options, DocumentMapping mapping)
        {
            if (options.Events.StreamIdentity == StreamIdentity.AsGuid)
            {
                if (mapping.IdType != typeof(Guid))
                {
                    yield return
                        $"Id type mismatch. The stream identity type is System.Guid, but the aggregate document {typeof(T).FullNameInCode()} id type is {mapping.IdType.NameInCode()}";
                }
            }

            if (options.Events.StreamIdentity == StreamIdentity.AsString)
            {
                if (mapping.IdType != typeof(string))
                {
                    yield return
                        $"Id type mismatch. The stream identity type is string, but the aggregate document {typeof(T).FullNameInCode()} id type is {mapping.IdType.NameInCode()}";
                }
            }
        }

        internal override IReadOnlyList<AsyncProjectionShard> AsyncProjectionShards(DocumentStore store)
        {
            _runtime = BuildRuntime(store);

            var eventTypes = determineEventTypes();

            var baseFilters = new ISqlFragment[0];
            if (!eventTypes.Any(x => x.IsAbstract || x.IsInterface))
            {
                baseFilters = new ISqlFragment[] {new Marten.Events.Daemon.EventTypeFilter(store.Events, eventTypes)};
            }

            return new List<AsyncProjectionShard> {new(this, baseFilters)};
        }

        protected virtual Type[] determineEventTypes()
        {
            var eventTypes = MethodCollection.AllEventTypes(_applyMethods, _createMethods, _shouldDeleteMethods)
                .Concat(DeleteEvents).Distinct().ToArray();
            return eventTypes;
        }

        internal override ValueTask<EventRangeGroup> GroupEvents(DocumentStore store, EventRange range, CancellationToken cancellationToken)
        {
            _runtime ??= BuildRuntime(store);

            return _runtime.GroupEvents(store, range, cancellationToken);
        }
    }
}
