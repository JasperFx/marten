using System;
using Baseline;
using LamarCodeGeneration;
using LamarCompiler;
using Marten.Events.V4Concept.CodeGeneration;
using Marten.Internal;
using Marten.Internal.CodeGeneration;

namespace Marten.Events.V4Concept.Aggregation
{
    internal static class SelfLiveAggregatingBuilder
    {
        internal static ILiveAggregator<T> Build<T>(StoreOptions options)
        {
            var mapping = options.Storage.MappingFor(typeof(T));

            var assembly = new GeneratedAssembly(new GenerationRules("Marten.Generated"));

            var applyMethods = new ApplyMethodCollection(typeof(T), typeof(T));

            // TODO -- this doesn't do very much right now.
            var createMethods = new CreateMethodCollection(typeof(T), typeof(T));

            assembly.Generation.Assemblies.Add(typeof(IMartenSession).Assembly);
            assembly.Generation.Assemblies.AddRange(applyMethods.ReferencedAssemblies());
            assembly.Generation.Assemblies.AddRange(createMethods.ReferencedAssemblies());

            var isAsync = applyMethods.IsAsync;

            assembly.Namespaces.Add("System.Linq");

            var liveBaseType = isAsync
                ? typeof(AsyncLiveAggregatorBase<>)
                : typeof(SyncLiveAggregatorBase<>);

            liveBaseType = liveBaseType.MakeGenericType(typeof(T));

            var liveType = assembly.AddType(typeof(T).NameInCode().Sanitize() + "LiveAggregation", liveBaseType);

            var overrideMethodName = isAsync ? "BuildAsync" : "Build";
            var buildMethod = liveType.MethodFor(overrideMethodName);

            // TODO -- use constructor functions later
            // TODO -- use static Create() methods
            // TODO -- validate that there is some way to create the aggregate
            buildMethod.Frames.Code("if (!events.Any()) return null;");
            buildMethod.Frames.Add(new CallCreateAggregateFrame(createMethods));
            buildMethod.Frames.Add(new CallApplyAggregateFrame(applyMethods){InsideForEach = true});

            buildMethod.Frames.Return(typeof(T));

            createMethods.BuildCreateMethod(liveType, mapping);
            applyMethods.BuildApplyMethod(liveType, mapping);

            var assemblyGenerator = new AssemblyGenerator();

            assemblyGenerator.ReferenceAssembly(typeof(IMartenSession).Assembly);
            assemblyGenerator.Compile(assembly);

            return (ILiveAggregator<T>) Activator.CreateInstance(liveType.CompiledType);
        }
    }
}
