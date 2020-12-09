using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using LamarCompiler;
using Marten.Events.V4Concept.CodeGeneration;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Events.V4Concept.Aggregation
{
    public abstract partial class V4AggregateProjection<T>
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

        public V4AggregateProjection()
        {
            _createMethods = new CreateMethodCollection(GetType(), typeof(T));
            _applyMethods = new ApplyMethodCollection(GetType(), typeof(T));
            _shouldDeleteMethods = new ShouldDeleteMethodCollection(GetType(), typeof(T));
        }

        Type IAggregateProjection.AggregateType => typeof(T);

        public bool MatchesAnyDeleteType(IEnumerable<IEvent> events)
        {
            return events.Select(x => x.EventType).Intersect(DeleteEvents).Any();
        }


        internal ILiveAggregator<T> BuildLiveAggregator()
        {
            return (ILiveAggregator<T>)Activator.CreateInstance(_liveType.CompiledType, this);
        }

        internal IInlineProjection BuildInlineProjection(IMartenSession session)
        {
            var storage = session.StorageFor<T>();

            return (IInlineProjection)Activator.CreateInstance(_inlineType.CompiledType, GetType().Name, storage, this);
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

        internal GeneratedAssembly Compile(DocumentStore store)
        {
            _assembly = new GeneratedAssembly(new GenerationRules("Marten.Generated"));

            _assembly.Generation.Assemblies.Add(GetType().Assembly);
            _assembly.Namespaces.Add("System.Linq");

            _isAsync = _createMethods.IsAsync || _applyMethods.IsAsync;

            _aggregateMapping = store.Storage.MappingFor(typeof(T));
            _storageType = typeof(IDocumentStorage<,>).MakeGenericType(typeof(T), _aggregateMapping.IdType);


            // TODO -- Validate the id strategy for the mapping
            // against the aggregation setup


            buildLiveAggregationType();
            buildInlineAggregationType();
            buildAsyncDaemonAggregation();

            var assemblyGenerator = new AssemblyGenerator();

            assemblyGenerator.ReferenceAssembly(typeof(IMartenSession).Assembly);
            assemblyGenerator.Compile(_assembly);

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

            buildAsyncDaemonSplitMethod();
        }


        private void buildAsyncDaemonSplitMethod()
        {
            var method = _asyncDaemonType.MethodFor("Split");

            string splitterMethodName = null;
            if (_aggregateMapping.TenancyStyle == TenancyStyle.Conjoined)
            {
                splitterMethodName = _aggregateMapping.IdType == typeof(Guid)
                    ? nameof(StreamFragmentSplitter.SplitByStreamIdMultiTenanted)
                    : nameof(StreamFragmentSplitter.SplitByStreamKeyMultiTenanted);
            }
            else
            {
                splitterMethodName = _aggregateMapping.IdType == typeof(Guid)
                    ? nameof(StreamFragmentSplitter.SplitByStreamId)
                    : nameof(StreamFragmentSplitter.SplitByStreamKey);
            }

            method.Frames.Code(
                $"return {typeof(StreamFragmentSplitter).FullNameInCode()}.{splitterMethodName}<{typeof(T).FullNameInCode()}>(events, storeTenancy);");
        }

        private void buildDetermineOperationMethodForDaemonRunner(bool daemonBuilderIsAsync)
        {
            var methodName = daemonBuilderIsAsync ? "DetermineOperation" : "DetermineOperationSync";
            var method = _asyncDaemonType.MethodFor(methodName);
            if (daemonBuilderIsAsync)
            {
                method.AsyncMode = AsyncMode.AsyncTask;
            }

            method.DerivedVariables.Add(Variable.For<ITenant>($"fragment.{nameof(StreamFragment<string, string>.Tenant)}"));
            method.DerivedVariables.Add(Variable.For<IEvent>("@event"));
            method.DerivedVariables.Add(Variable.For<IQuerySession>($"(({typeof(IQuerySession).FullNameInCode()})session)"));

            // At most, only one of these would be used
            method.DerivedVariables.Add(Variable.For<Guid>($"fragment.Id"));
            method.DerivedVariables.Add(Variable.For<string>($"fragment.Id"));

            var aggregate = new Variable(typeof(T),
                $"fragment.{nameof(StreamFragment<string, string>.Aggregate)}");
            method.DerivedVariables.Add(aggregate);

            var createFrame = new CallCreateAggregateFrame(_createMethods, aggregate)
            {
                FirstEventExpression = $"fragment.{nameof(StreamFragment<string, string>.Events)}[0]",
                Action = CreateAggregateAction.NullCoalesce
            };

            method.Frames.Add(createFrame);
            method.Frames.Add(new MethodCall(_storageType, "SetIdentity"));

            var handlers = MethodCollection.AddEventHandling(typeof(T), _aggregateMapping, _applyMethods, _shouldDeleteMethods);
            var iterate = new ForEachEventFrame((IReadOnlyList<Frame>) handlers)
            {
                EventIteration = "fragment.Events"
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

            // This gets you the StreamAction Id
            method.DerivedVariables.Add(Variable.For<Guid>($"stream.{nameof(StreamAction.Id)}"));
            method.DerivedVariables.Add(Variable.For<string>($"stream.{nameof(StreamAction.Key)}"));
            method.DerivedVariables.Add(Variable.For<ITenant>($"session.{nameof(IMartenSession.Tenant)}"));
            method.DerivedVariables.Add(Variable.For<IEvent>("@event"));
            method.DerivedVariables.Add(Variable.For<IMartenSession>($"({typeof(IMartenSession).FullNameInCode()})session"));
            method.DerivedVariables.Add(Variable.For<IQuerySession>("session"));

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
            buildMethod.Frames.Add(new CallCreateAggregateFrame(_createMethods));
            buildMethod.Frames.Add(new CallApplyAggregateFrame(_applyMethods){InsideForEach = true});

            buildMethod.Frames.Return(typeof(T));

            _liveType.AllInjectedFields.Add(new InjectedField(GetType()));

            _createMethods.BuildCreateMethod(_liveType, _aggregateMapping);
            _applyMethods.BuildApplyMethod(_liveType, _aggregateMapping);
        }



    }
}
