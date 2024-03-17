using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.RuntimeCompiler;
using Marten.Events.CodeGeneration;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Storage;

namespace Marten.Events.Aggregation;

public abstract partial class GeneratedAggregateProjectionBase<T>
{
    private readonly object _compilationLock = new();

    public ILiveAggregator<T> Build(StoreOptions options)
    {
        if (_liveType == null)
        {
            lock (_compilationLock)
            {
                if (_liveType == null)
                {
                    Compile(options);
                }
            }
        }

        return BuildLiveAggregator();
    }

    internal string SourceCode()
    {
        var writer = new StringWriter();
        writer.WriteLine(_liveGeneratedType.SourceCode);
        writer.WriteLine();

        writer.WriteLine(_inlineGeneratedType.SourceCode);
        writer.WriteLine();

        return writer.ToString();
    }

    internal void Compile(StoreOptions options)
    {
        var rules = options.CreateGenerationRules();
        Compile(options, rules);
    }

    internal void Compile(StoreOptions options, GenerationRules rules)
    {
        // HOKEY, but makes ICodeFile functions work. Temporary, hopefully.
        StoreOptions = options;

        this.As<ICodeFile>().InitializeSynchronously(rules, options.EventGraph, null);

        // You have to do this for the sake of the Setters
        if (_liveGeneratedType == null || _liveType == null)
        {
            lock (_assembleLocker)
            {
                if (_liveGeneratedType == null)
                {
                    assembleTypes(new GeneratedAssembly(rules), options);
                }
            }
        }
    }

    protected override bool tryAttachTypes(Assembly assembly, StoreOptions options)
    {
        _inlineType = assembly.GetExportedTypes().FirstOrDefault(x => x.Name == _inlineAggregationHandlerType);
        _liveType = assembly.GetExportedTypes().FirstOrDefault(x => x.Name == _liveAggregationTypeName);

        if (_liveGeneratedType != null)
        {
            Debug.WriteLine(_liveGeneratedType.SourceCode);
        }

        if (_inlineGeneratedType != null)
        {
            Debug.WriteLine(_inlineGeneratedType.SourceCode);
        }

        return _inlineType != null && _liveType != null;
    }

    protected override void assembleTypes(GeneratedAssembly assembly, StoreOptions options)
    {
        referenceAssembliesAndTypes(assembly);
        addUsingNamespaces(assembly);
        checkAndSetAsyncFlag();
        validateAndSetAggregateMapping(options);
        buildAggregationTypes(assembly);
    }

    private void referenceAssembliesAndTypes(GeneratedAssembly assembly)
    {
        assembly.Rules.ReferenceTypes(GetType());
        assembly.ReferenceAssembly(GetType().Assembly);
        assembly.ReferenceAssembly(typeof(T).Assembly);
        assembly.Rules.ReferenceTypes(_applyMethods.ReferencedTypes().ToArray());
        assembly.Rules.ReferenceTypes(_createMethods.ReferencedTypes().ToArray());
        assembly.Rules.ReferenceTypes(_shouldDeleteMethods.ReferencedTypes().ToArray());

        // Walk the assembly dependencies for the projection and aggregate types,
        // and this will catch generic type argument dependencies as well. For GH-2061
        assembly.Rules.ReferenceTypes(GetType(), typeof(T));
    }

    private static void addUsingNamespaces(GeneratedAssembly assembly)
    {
        assembly.UsingNamespaces.Add("System");
        assembly.UsingNamespaces.Add("System.Linq");
    }

    private void checkAndSetAsyncFlag()
    {
        _isAsync = _createMethods.IsAsync || _applyMethods.IsAsync;
    }

    private void validateAndSetAggregateMapping(StoreOptions options)
    {
        _aggregateMapping = options.Storage.FindMapping(typeof(T));
        if (_aggregateMapping.IdMember == null)
        {
            throw new InvalidDocumentException(
                $"No identity property or field can be determined for the aggregate '{typeof(T).FullNameInCode()}', but one is required to be used as an aggregate in projections");
        }
    }

    private void buildAggregationTypes(GeneratedAssembly assembly)
    {
        buildLiveAggregationType(assembly);
        buildInlineAggregationType(assembly);
    }

    protected override IProjection buildProjectionObject(DocumentStore store)
    {
        return BuildRuntime(store);
    }

    internal ILiveAggregator<T> BuildLiveAggregator()
    {
        var aggregator = (ILiveAggregator<T>)Activator.CreateInstance(_liveType, this);

        foreach (var setter in _liveGeneratedType.Setters)
        {
            var prop = _liveType.GetProperty(setter.PropName);
            prop.SetValue(aggregator, setter.InitialValue);
        }

        _versioning.Inner = aggregator;

        return _versioning;
    }

    internal IAggregationRuntime BuildRuntime(DocumentStore store)
    {
        // You have to have the inlineGeneratedType built out to apply
        // setter values
        if (_liveType == null)
        {
            Compile(store.Options);
        }
        else if (_inlineGeneratedType == null)
        {
            lock (_assembleLocker)
            {
                var rules = store.Options.CreateGenerationRules();
                assembleTypes(new GeneratedAssembly(rules), store.Options);
            }
        }

        var storage = store.Options.Providers.StorageFor<T>().Lightweight;
        var slicer = buildEventSlicer(store.Options);

        var inline = (IAggregationRuntime)Activator.CreateInstance(_inlineType, store, this, slicer,
            storage, this);

        foreach (var setter in _inlineGeneratedType.Setters)
        {
            var prop = _inlineType.GetProperty(setter.PropName);
            prop.SetValue(inline, setter.InitialValue);
        }

        inline.Versioning = _versioning;

        return inline;
    }

    protected override bool needsSettersGenerated()
    {
        return _liveGeneratedType == null || _inlineGeneratedType == null;
    }

    private void buildInlineAggregationType(GeneratedAssembly assembly)
    {
        var inlineBaseType = baseTypeForAggregationRuntime();

        _inlineGeneratedType = assembly.AddType(_inlineAggregationHandlerType, inlineBaseType);

        _createMethods.BuildCreateMethod(_inlineGeneratedType, _aggregateMapping);

        _inlineGeneratedType.AllInjectedFields.Add(new InjectedField(GetType()));

        buildApplyEventMethod();

        _inlineGeneratedType.Setters.AddRange(_applyMethods.Setters());
        _inlineGeneratedType.Setters.AddRange(_createMethods.Setters());
        _inlineGeneratedType.Setters.AddRange(_shouldDeleteMethods.Setters());
    }

    private GeneratedMethod buildApplyEventMethod()
    {
        var method = _inlineGeneratedType.MethodFor(nameof(AggregationRuntime<string, string>.ApplyEvent));

        // This gets you the EventSlice aggregate Id

        method.DerivedVariables.Add(new Variable(_aggregateMapping.IdType,
            $"slice.{nameof(EventSlice<string, string>.Id)}"));
        method.DerivedVariables.Add(Variable.For<Tenant>($"slice.{nameof(EventSlice<string, string>.Tenant)}"));
        method.DerivedVariables.Add(Variable.For<Tenant>($"slice.{nameof(EventSlice<string, string>.Tenant)}"));
        method.DerivedVariables.Add(Variable.For<IEvent>("@event"));
        method.DerivedVariables.Add(
            Variable.For<IMartenSession>($"({typeof(IMartenSession).FullNameInCode()})session"));
        method.DerivedVariables.Add(Variable.For<IQuerySession>("session"));
        method.DerivedVariables.Add(
            Variable.For<IAggregateProjection>(nameof(AggregationRuntime<string, string>.Projection)));


        var eventHandlers = new LightweightCache<Type, AggregateEventProcessingFrame>(
            eventType => new AggregateEventProcessingFrame(typeof(T), eventType));

        foreach (var deleteEvent in DeleteEvents) eventHandlers[deleteEvent].AlwaysDeletes = true;

        foreach (var slot in _applyMethods.Methods) eventHandlers[slot.EventType].Apply = new ApplyMethodCall(slot);

        foreach (var slot in _createMethods.Methods)
        {
            eventHandlers[slot.EventType].CreationFrame = slot.Method is ConstructorInfo
                ? new AggregateConstructorFrame(slot)
                : new CreateAggregateFrame(slot);
        }

        foreach (var slot in _shouldDeleteMethods.Methods)
            eventHandlers[slot.EventType].Deletion = new ShouldDeleteFrame(slot);

        var frames = eventHandlers.OfType<EventProcessingFrame>().ToList();

        var patternMatching = new EventTypePatternMatchFrame(frames);
        method.Frames.Add(patternMatching);

        method.Frames.Code("return aggregate;");

        return method;
    }

    private void buildLiveAggregationType(GeneratedAssembly assembly)
    {
        var liveBaseType = _isAsync
            ? typeof(AsyncLiveAggregatorBase<>)
            : typeof(SyncLiveAggregatorBase<>);

        liveBaseType = liveBaseType.MakeGenericType(typeof(T));


        _liveGeneratedType =
            assembly.AddType(_liveAggregationTypeName, liveBaseType);

        var overrideMethodName = _isAsync ? "BuildAsync" : "Build";
        var buildMethod = _liveGeneratedType.MethodFor(overrideMethodName);

        buildMethod.DerivedVariables.Add(Variable.For<IQuerySession>("(IQuerySession)session"));

        buildMethod.Frames.Code("if (!events.Any()) return null;");

        buildMethod.Frames.Add(new DeclareAggregateFrame(typeof(T)));

        var callCreateAggregateFrame = new CallCreateAggregateFrame(_createMethods);

        // This is the existing snapshot passed into the LiveAggregator
        var snapshot = buildMethod.Arguments.Single(x => x.VariableType == typeof(T));
        callCreateAggregateFrame.CoalesceAssignTo(snapshot);

        buildMethod.Frames.Add(callCreateAggregateFrame);
        buildMethod.Frames.Add(new CallApplyAggregateFrame(_applyMethods) { InsideForEach = true });

        buildMethod.Frames.Return(typeof(T));

        _liveGeneratedType.AllInjectedFields.Add(new InjectedField(GetType()));

        _createMethods.BuildCreateMethod(_liveGeneratedType, _aggregateMapping);
        _applyMethods.BuildApplyMethod(_liveGeneratedType, _aggregateMapping);

        _liveGeneratedType.Setters.AddRange(_applyMethods.Setters());
        _liveGeneratedType.Setters.AddRange(_createMethods.Setters());
        _liveGeneratedType.Setters.AddRange(_shouldDeleteMethods.Setters());
    }
}
