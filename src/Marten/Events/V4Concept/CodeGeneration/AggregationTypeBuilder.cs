using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using LamarCodeGeneration.Util;
using LamarCompiler;
using Marten.Events.V4Concept.Aggregation;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Events.V4Concept.CodeGeneration
{
    internal class AggregationTypeBuilder
    {
        private readonly IAggregateProjection _projection;
        private readonly GeneratedAssembly _assembly;
        private readonly Type _projectionType;
        private readonly DocumentMapping _aggregateMapping;
        private readonly CreateMethodCollection _createMethods;
        private readonly ApplyMethodCollection _applyMethods;
        private readonly ShouldDeleteMethodCollection _shouldDeleteMethods;
        private readonly bool _isAsync;
        private readonly Type _storageType;

        public AggregationTypeBuilder(IAggregateProjection projection, DocumentStore store)
        {
            _projection = projection;

            _assembly = NewAssembly();
            _projectionType = projection.GetType();

            _assembly.Generation.Assemblies.Add(_projectionType.Assembly);
            _assembly.Namespaces.Add("System.Linq");

            _aggregateMapping = store.Storage.MappingFor(projection.AggregateType);

            _storageType = typeof(IDocumentStorage<,>).MakeGenericType(projection.AggregateType, _aggregateMapping.IdType);


            _createMethods = new CreateMethodCollection(_projectionType, projection.AggregateType);
            _applyMethods = new ApplyMethodCollection(_projectionType, projection.AggregateType);
            _shouldDeleteMethods = new ShouldDeleteMethodCollection(_projectionType, projection.AggregateType, _aggregateMapping.IdType);

            _isAsync = _createMethods.IsAsync || _applyMethods.IsAsync;
        }

        public GeneratedAssembly Compile()
        {
            buildLiveAggregationType();
            buildInlineAggregationType();
            buildAsyncDaemonAggregation();

            var assemblyGenerator = new AssemblyGenerator();

            assemblyGenerator.ReferenceAssembly(typeof(AggregationTypeBuilder).Assembly);
            assemblyGenerator.Compile(_assembly);

            return _assembly;
        }

        private void buildAsyncDaemonAggregation()
        {
            var daemonBuilderIsAsync = _applyMethods.IsAsync || _createMethods.IsAsync || _shouldDeleteMethods.IsAsync;
            var baseType = (daemonBuilderIsAsync ? typeof(AsyncDaemonAggregationBase<,>) : typeof(SyncDaemonAggregationBase<,>))
                .MakeGenericType(_projection.AggregateType, _aggregateMapping.IdType);

            _projection.AsyncAggregationType =
                _assembly.AddType(_projectionType.Name.Sanitize() + "AsyncDaemonAggregation", baseType);

            _projection.AsyncAggregationType.AllInjectedFields.Add(new InjectedField(_storageType));

            var injectedField = new InjectedField(_projectionType);
            _projection.AsyncAggregationType.AllInjectedFields.Add(injectedField);

            // Build the create method
            _createMethods.BuildCreateMethod(_projection.AsyncAggregationType);

            buildDetermineOperationMethodForDaemonRunner(daemonBuilderIsAsync);

            buildAsyncDaemonSplitMethod();
        }

        private void buildAsyncDaemonSplitMethod()
        {
            var method = _projection.AsyncAggregationType.MethodFor("Split");

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
                $"return {typeof(StreamFragmentSplitter).FullNameInCode()}.{splitterMethodName}<{_projection.AggregateType.FullNameInCode()}>(events, storeTenancy);");
        }

        private void buildDetermineOperationMethodForDaemonRunner(bool daemonBuilderIsAsync)
        {
            var methodName = daemonBuilderIsAsync ? "DetermineOperation" : "DetermineOperationSync";
            var method = _projection.AsyncAggregationType.MethodFor(methodName);
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

            var aggregate = new Variable(_projection.AggregateType,
                $"fragment.{nameof(StreamFragment<string, string>.Aggregate)}");
            method.DerivedVariables.Add(aggregate);

            var createFrame = new CallCreateAggregateFrame(_createMethods, aggregate)
            {
                FirstEventExpression = $"fragment.{nameof(StreamFragment<string, string>.Events)}[0]",
                Action = CreateAggregateAction.NullCoalesce
            };

            method.Frames.Add(createFrame);
            method.Frames.Add(new MethodCall(_storageType, "SetIdentity"));

            var handlers = MethodCollection.AddEventHandling(_projection.AggregateType, _applyMethods, _shouldDeleteMethods);
            var iterate = new ForEachEventFrame((IReadOnlyList<Frame>) handlers)
            {
                EventIteration = "fragment.Events"
            };
            method.Frames.Add(iterate);

            var upsertMethod = typeof(IDocumentStorage<>).MakeGenericType(_projection.AggregateType).GetMethod("Upsert");

            var upsert = new MethodCall(_storageType, upsertMethod)
            {
                ReturnAction = ReturnAction.Return
            };

            method.Frames.Add(upsert);
        }


        private void buildInlineAggregationType()
        {
            var inlineBaseType =
                typeof(InlineAggregationBase<,>).MakeGenericType(_projection.AggregateType, _aggregateMapping.IdType);

            _projection.InlineType = _assembly.AddType(_projectionType.NameInCode().Sanitize() + "InlineHandler", inlineBaseType);

            _createMethods.BuildCreateMethod(_projection.InlineType);

            _projection.InlineType.AllInjectedFields.Add(new InjectedField(_storageType));
            _projection.InlineType.AllInjectedFields.Add(new InjectedField(_projectionType));

            var method = _projection.InlineType.MethodFor(nameof(InlineAggregationBase<string,string>.DetermineOperation));

            // This gets you the StreamAction Id
            method.DerivedVariables.Add(Variable.For<Guid>($"stream.{nameof(StreamAction.Id)}"));
            method.DerivedVariables.Add(Variable.For<string>($"stream.{nameof(StreamAction.Key)}"));
            method.DerivedVariables.Add(Variable.For<ITenant>($"session.{nameof(IMartenSession.Tenant)}"));
            method.DerivedVariables.Add(Variable.For<IEvent>("@event"));
            method.DerivedVariables.Add(Variable.For<IMartenSession>($"({typeof(IMartenSession).FullNameInCode()})session"));
            method.DerivedVariables.Add(Variable.For<IQuerySession>("session"));

            var createFrame = new CallCreateAggregateFrame(_createMethods);
            method.Frames.Add(new InitializeLiveAggregateFrame(_projection.AggregateType, _aggregateMapping.IdType, createFrame));

            method.Frames.Add(new MethodCall(_storageType, "SetIdentity"));

            var handlers = MethodCollection.AddEventHandling( _projection.AggregateType, _applyMethods, _shouldDeleteMethods);
            var iterate = new ForEachEventFrame((IReadOnlyList<Frame>) handlers);
            method.Frames.Add(iterate);

            var upsertMethod = typeof(IDocumentStorage<>).MakeGenericType(_projection.AggregateType).GetMethod("Upsert");

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

            liveBaseType = liveBaseType.MakeGenericType(_projection.AggregateType);


            _projection.LiveAggregationType =
                _assembly.AddType(_projectionType.NameInCode().Sanitize() + "LiveAggregation", liveBaseType);

            var overrideMethodName = _isAsync ? "BuildAsync" : "Build";
            var buildMethod = _projection.LiveAggregationType.MethodFor(overrideMethodName);
            buildMethod.Frames.Code("if (!events.Any()) return null;");
            buildMethod.Frames.Add(new CallCreateAggregateFrame(_createMethods));
            buildMethod.Frames.Add(new CallApplyAggregateFrame(_applyMethods){InsideForEach = true});

            buildMethod.Frames.Return(_projection.AggregateType);

            _projection.LiveAggregationType.AllInjectedFields.Add(new InjectedField(_projectionType));

            _createMethods.BuildCreateMethod(_projection.LiveAggregationType);
            _applyMethods.BuildApplyMethod(_projection.LiveAggregationType);
        }

        public static Type EventTypeFor(MethodInfo method)
        {
            var parameterInfo = method.GetParameters().FirstOrDefault(x => x.Name == "@event" || x.Name == "event");
            if (parameterInfo == null) return null;

            if (parameterInfo.ParameterType.Closes(typeof(Event<>)))
                return parameterInfo.ParameterType.GetGenericArguments()[0];

            return parameterInfo.ParameterType;
        }

        public static GeneratedAssembly NewAssembly()
        {
            return new GeneratedAssembly(new GenerationRules("Marten.Generated"));
        }


    }
}
