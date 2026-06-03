#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;

namespace Marten.Internal;

/// <summary>
/// Builds a thin runtime subclass <c>class &lt;T&gt;Implementation : DocumentStore, T</c>
/// per user-supplied secondary-store interface via
/// <see cref="System.Reflection.Emit"/>. <c>T</c> here is a marker interface
/// (<c>T : IDocumentStore</c>) with no extra abstract members, so the emitted
/// type only needs a forwarding constructor.
/// </summary>
[RequiresDynamicCode("Generates a runtime subclass per secondary-store interface via System.Reflection.Emit.")]
internal static class SecondaryStoreProxyFactory
{
    // Lazy<Type> (not Type) is the value: ConcurrentDictionary.GetOrAdd does NOT
    // guarantee the value-factory runs only once under contention, so caching the
    // raw Type would let multiple threads run Build for the same key — each calling
    // DefineType with the same name on the shared module and throwing
    // "Duplicate type name within an assembly". Caching a Lazy makes Build run
    // exactly once per key.
    //
    // Note: Lazy (ExecutionAndPublication) caches a thrown exception permanently,
    // so a failed Build is no longer retried on the next call as it was with the
    // bare GetOrAdd(Build). Build only fails deterministically here (a non-marker
    // interface that can't be implemented), so caching the failure is acceptable.
    private static readonly ConcurrentDictionary<Type, Lazy<Type>> _cache = new();

    // The ModuleBuilder is process-shared across every secondary-store interface,
    // and ModuleBuilder.DefineType/CreateType are not thread-safe even for distinct
    // type names. Serialise all emission through this gate.
    private static readonly object _emitLock = new();

    private static readonly Lazy<ModuleBuilder> _module = new(() =>
    {
        var asm = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Marten.DynamicStores"),
            AssemblyBuilderAccess.Run);
        return asm.DefineDynamicModule("Marten.DynamicStores");
    });

    public static Type GetOrCreate(Type interfaceType)
    {
        if (!typeof(IDocumentStore).IsAssignableFrom(interfaceType))
        {
            throw new ArgumentException(
                $"{interfaceType.FullName} must inherit from {nameof(IDocumentStore)} to be used as a secondary store interface.",
                nameof(interfaceType));
        }

        return _cache.GetOrAdd(interfaceType, t => new Lazy<Type>(() => Build(t))).Value;
    }

    private static Type Build(Type interfaceType)
    {
        lock (_emitLock)
        {
            return BuildCore(interfaceType);
        }
    }

    private static Type BuildCore(Type interfaceType)
    {
        var module = _module.Value;
        var typeName = $"Marten.DynamicStores.{interfaceType.Name}Implementation";

        var tb = module.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Class,
            typeof(DocumentStore),
            new[] { interfaceType });

        var baseCtor = typeof(DocumentStore).GetConstructor(
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: new[] { typeof(StoreOptions) },
            modifiers: null)
            ?? throw new InvalidOperationException(
                $"Could not find public DocumentStore(StoreOptions) constructor on {typeof(DocumentStore).FullName}.");

        var ctor = tb.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            new[] { typeof(StoreOptions) });

        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, baseCtor);
        il.Emit(OpCodes.Ret);

        return tb.CreateType()
            ?? throw new InvalidOperationException($"Failed to materialize the secondary-store proxy for {interfaceType.FullName}.");
    }
}
