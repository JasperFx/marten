#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;

namespace Marten.Internal;

/// <summary>
/// Builds a thin runtime subclass <c>class &lt;T&gt;Implementation : DocumentStore, T</c>
/// per user-supplied secondary-store interface — what the Roslyn-emit path
/// in <see cref="SecondaryStoreConfig{T}"/> used to produce. Uses
/// <see cref="System.Reflection.Emit"/> instead of JasperFx.RuntimeCompiler:
/// <c>T</c> here is a marker interface (<c>T : IDocumentStore</c>) with no
/// extra abstract members, so the emitted type only needs a forwarding
/// constructor.
/// </summary>
[RequiresDynamicCode("Generates a runtime subclass per secondary-store interface via System.Reflection.Emit.")]
internal static class SecondaryStoreProxyFactory
{
    private static readonly ConcurrentDictionary<Type, Type> _cache = new();

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

        return _cache.GetOrAdd(interfaceType, Build);
    }

    private static Type Build(Type interfaceType)
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
