using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using LamarCompiler;
using Marten.Events.CodeGeneration;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Events.Aggregation
{
    public partial class AggregateProjection<T> : ILiveAggregatorSource<T>, IProjectionSource, IValidatedProjection
    {
        private GeneratedType _liveType;
        private GeneratedType _inlineType;
        private GeneratedType _asyncDaemonType;
        private DocumentMapping _aggregateMapping;

        private readonly CreateMethodCollection _createMethods;
        private readonly ApplyMethodCollection _applyMethods;
        private readonly ShouldDeleteMethodCollection _shouldDeleteMethods;
        private bool _isAsync;
        private GeneratedAssembly _assembly;
        private Type _storageType;

        public AggregateProjection()
        {
            _createMethods = new CreateMethodCollection(GetType(), typeof(T));
            _applyMethods = new ApplyMethodCollection(GetType(), typeof(T));
            _shouldDeleteMethods = new ShouldDeleteMethodCollection(GetType(), typeof(T));

            ProjectionName = typeof(T).Name;
        }

        Type IAggregateProjection.AggregateType => typeof(T);



        public string ProjectionName { get; protected set; }

        public IInlineProjection BuildInline(StoreOptions options)
        {
            if (_inlineType == null)
            {
                Compile(options);
            }

            // This will have to change when we introduce 1st class support for tenancy by
            // separate databases
            options.Tenancy.Default.EnsureStorageExists(typeof(T));
            return BuildInlineProjection(options);
        }


        internal ILiveAggregator<T> BuildLiveAggregator()
        {
            var aggregator = (ILiveAggregator<T>)Activator.CreateInstance(_liveType.CompiledType, this);
            _liveType.ApplySetterValues(aggregator);

            return aggregator;
        }

        internal IInlineProjection BuildInlineProjection(StoreOptions options)
        {
            var storage = options.Providers.StorageFor<T>().Lightweight;
            var slicer = buildEventSlicer();

            var ctor = _inlineType.CompiledType.GetConstructors().Single();
            foreach (var parameter in ctor.GetParameters())
            {
                Debug.WriteLine(parameter.ParameterType.NameInCode());
            }

            var inline = (IInlineProjection)Activator.CreateInstance(_inlineType.CompiledType, this, slicer, options.Tenancy, storage, this);
            _inlineType.ApplySetterValues(inline);

            return inline;
        }

        protected virtual object buildEventSlicer()
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
            _assembly = new GeneratedAssembly(new GenerationRules("Marten.Generated"));

            _assembly.Generation.Assemblies.Add(GetType().Assembly);
            _assembly.Generation.Assemblies.Add(typeof(T).Assembly);
            _assembly.Generation.Assemblies.AddRange(_applyMethods.ReferencedAssemblies());
            _assembly.Generation.Assemblies.AddRange(_createMethods.ReferencedAssemblies());
            _assembly.Generation.Assemblies.AddRange(_shouldDeleteMethods.ReferencedAssemblies());

            _assembly.Namespaces.Add("System.Linq");

            _isAsync = _createMethods.IsAsync || _applyMethods.IsAsync;

            _aggregateMapping = options.Storage.MappingFor(typeof(T));


            if (_aggregateMapping.IdMember == null)
            {
                // TODO -- possibly try to relax this!!!
                throw new InvalidDocumentException(
                    $"No identity property or field can be determined for the aggregate '{typeof(T).FullNameInCode()}', but one is required to be used as an aggregate in projections");
            }

            _storageType = typeof(IDocumentStorage<,>).MakeGenericType(typeof(T), _aggregateMapping.IdType);


            // TODO -- Validate the id strategy for the mapping
            // against the aggregation setup


            buildLiveAggregationType();
            buildInlineAggregationType();
            buildAsyncDaemonAggregation();

            var assemblyGenerator = new AssemblyGenerator();

            assemblyGenerator.ReferenceAssembly(typeof(IMartenSession).Assembly);
            assemblyGenerator.Compile(_assembly);

            Debug.WriteLine(_liveType.SourceCode);

            return _assembly;
        }

        private void buildAsyncDaemonAggregation()
        {
            var daemonBuilderIsAsync = _applyMethods.IsAsync || _createMethods.IsAsync || _shouldDeleteMethods.IsAsync;
            var baseType = (daemonBuilderIsAsync ? typeof(AsyncDaemonAggregationBase<,>) : typeof(SyncDaemonAggregationBase<,>))
                .MakeGenericType(typeof(T), _aggregateMapping.IdType);

            _asyncDaemonType =
                _assembly.AddType(GetType().Name.Sanitize() + "AsyncDaemonAggregation", baseType);

            _asyncDaemonType.AllInjectedFields.Add(new InjectedField(_storageType));

            var injectedField = new InjectedField(GetType());
            _asyncDaemonType.AllInjectedFields.Add(injectedField);

            // Build the create method
            _createMethods.BuildCreateMethod(_asyncDaemonType, _aggregateMapping);

            buildDetermineOperationMethodForDaemonRunner(daemonBuilderIsAsync);

            _asyncDaemonType.Setters.AddRange(_applyMethods.Setters());
            _asyncDaemonType.Setters.AddRange(_createMethods.Setters());
            _asyncDaemonType.Setters.AddRange(_shouldDeleteMethods.Setters());
        }



        private void buildDetermineOperationMethodForDaemonRunner(bool daemonBuilderIsAsync)
        {
            var methodName = daemonBuilderIsAsync ? "DetermineOperation" : "DetermineOperationSync";
            var method = _asyncDaemonType.MethodFor(methodName);
            if (daemonBuilderIsAsync)
            {
                method.AsyncMode = AsyncMode.AsyncTask;

            }

            method.DerivedVariables.Add(Variable.For<ITenant>($"slice.{nameof(EventSlice<string, string>.Tenant)}"));
            method.DerivedVariables.Add(Variable.For<IEvent>("@event"));
            method.DerivedVariables.Add(Variable.For<IQuerySession>($"(({typeof(IQuerySession).FullNameInCode()})session)"));

            // At most, only one of these would be used
            method.DerivedVariables.Add(Variable.For<Guid>($"slice.Id"));
            method.DerivedVariables.Add(Variable.For<string>($"slice.Id"));

            var aggregate = new Variable(typeof(T),
                $"slice.{nameof(EventSlice<string, string>.Aggregate)}");
            method.DerivedVariables.Add(aggregate);

            var createFrame = new CallCreateAggregateFrame(_createMethods, aggregate)
            {
                FirstEventExpression = $"slice.{nameof(EventSlice<string, string>.Events)}[0]",
                Action = CreateAggregateAction.NullCoalesce
            };

            method.Frames.Add(createFrame);
            method.Frames.Add(new MethodCall(_storageType, "SetIdentity"));

            var handlers = MethodCollection.AddEventHandling(typeof(T), _aggregateMapping, _applyMethods, _shouldDeleteMethods);
            var iterate = new ForEachEventFrame((IReadOnlyList<Frame>) handlers)
            {
                EventIteration = "slice.Events"
            };
            method.Frames.Add(iterate);

            var upsertMethod = typeof(IDocumentStorage<>).MakeGenericType(typeof(T)).GetMethod("Upsert");

            var upsert = new MethodCall(_storageType, upsertMethod)
            {
                ReturnAction = ReturnAction.Return
            };

            method.Frames.Add(upsert);
        }


        private void buildInlineAggregationType()
        {
            var inlineBaseType =
                typeof(InlineAggregationBase<,>).MakeGenericType(typeof(T), _aggregateMapping.IdType);

            _inlineType = _assembly.AddType(GetType().NameInCode().Sanitize() + "InlineHandler", inlineBaseType);

            _createMethods.BuildCreateMethod(_inlineType, _aggregateMapping);

            _inlineType.AllInjectedFields.Add(new InjectedField(_storageType));
            _inlineType.AllInjectedFields.Add(new InjectedField(GetType()));

            var method = _inlineType.MethodFor(nameof(InlineAggregationBase<string,string>.DetermineOperation));

            // This gets you the EventSlice aggregate Id
            method.DerivedVariables.Add(Variable.For<Guid>($"slice.{nameof(EventSlice<string, string>.Id)}"));
            method.DerivedVariables.Add(Variable.For<string>($"slice.{nameof(EventSlice<string, string>.Id)}"));

            // TODO -- this is hokey. Just pass in ITenant?
            method.DerivedVariables.Add(Variable.For<ITenant>($"(({typeof(IMartenSession).FullNameInCode()})session).{nameof(IMartenSession.Tenant)}"));
            method.DerivedVariables.Add(Variable.For<IEvent>("@event"));
            method.DerivedVariables.Add(Variable.For<IMartenSession>($"({typeof(IMartenSession).FullNameInCode()})session"));
            method.DerivedVariables.Add(Variable.For<IQuerySession>("session"));
            method.DerivedVariables.Add(Variable.For<IAggregateProjection>(nameof(InlineAggregationBase<string, string>.Projection)));

            var sliceType =
                typeof(EventSlice<,>).MakeGenericType(_aggregateMapping.DocumentType, _aggregateMapping.IdType);

            if (DeleteEvents.Any())
            {
                method.Frames.Code($"if (Projection.{nameof(MatchesAnyDeleteType)}({{0}})) return {{1}}.{nameof(IDocumentStorage<string, string>.DeleteForId)}({{2}});", new Use(sliceType), new Use(_storageType), new Use(_aggregateMapping.IdType));
            }

            var createFrame = new CallCreateAggregateFrame(_createMethods);
            method.Frames.Add(new InitializeLiveAggregateFrame(typeof(T), _aggregateMapping.IdType, createFrame));

            method.Frames.Add(new MethodCall(_storageType, "SetIdentity"));

            var handlers = MethodCollection.AddEventHandling( typeof(T), _aggregateMapping, _applyMethods, _shouldDeleteMethods);
            var iterate = new ForEachEventFrame((IReadOnlyList<Frame>) handlers);
            method.Frames.Add(iterate);

            var upsertMethod = typeof(IDocumentStorage<>).MakeGenericType(typeof(T)).GetMethod("Upsert");

            var upsert = new MethodCall(_storageType, upsertMethod)
            {
                ReturnAction = ReturnAction.Return
            };

            method.Frames.Add(upsert);

            _inlineType.Setters.AddRange(_applyMethods.Setters());
            _inlineType.Setters.AddRange(_createMethods.Setters());
            _inlineType.Setters.AddRange(_shouldDeleteMethods.Setters());
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
            buildMethod.Frames.Code("if (!events.Any()) return null;");
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

        void IValidatedProjection.AssertValidity()
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

        IEnumerable<string> IValidatedProjection.ValidateConfiguration(StoreOptions options)
        {
            var mapping = options.Storage.MappingFor(typeof(T));
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

            if (options.Events.TenancyStyle != mapping.TenancyStyle)
            {
                yield return
                    $"Tenancy storage style mismatch between the events ({options.Events.TenancyStyle}) and the aggregate type {typeof(T).FullNameInCode()} ({mapping.TenancyStyle})";
            }
        }
    }
}
