using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx;
using JasperFx.Core.Reflection;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Internal;

[UnconditionalSuppressMessage("Trimming", "IL2077",
    Justification = "Class-level: DAM-annotated field/property assigned from a reflective lookup whose source type is preserved at the registration boundary.")]
[UnconditionalSuppressMessage("Trimming", "IL2091",
    Justification = "Class-level: generic type argument doesn't carry the DAM annotation of its target. The argument types flow in from StoreOptions / projection-registration on the caller side and are preserved by the trimmer at that boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal class SecondaryDocumentStores
{
    private readonly List<IStoreConfig> _files = new();

    public IReadOnlyList<IStoreConfig> Stores => _files;

    public void Add(IStoreConfig config)
    {
        _files.Add(config);
        config.Parent = this;
    }
}

internal interface IStoreConfig
{
    SecondaryDocumentStores Parent { get; set; }
    StoreOptions BuildStoreOptions(IServiceProvider provider);
}

public interface IConfigureMarten<T>: IConfigureMarten where T : IDocumentStore
{
}


[UnconditionalSuppressMessage("Trimming", "IL2077",
    Justification = "Class-level: DAM-annotated field/property assigned from a reflective lookup whose source type is preserved at the registration boundary.")]
[UnconditionalSuppressMessage("Trimming", "IL2091",
    Justification = "Class-level: generic type argument doesn't carry the DAM annotation of its target. The argument types flow in from StoreOptions / projection-registration on the caller side and are preserved by the trimmer at that boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal class SecondaryStoreConfig<T>: IStoreConfig where T : IDocumentStore
{
    private readonly Func<IServiceProvider, StoreOptions> _configuration;

    public SecondaryStoreConfig(Func<IServiceProvider, StoreOptions> configuration)
    {
        _configuration = configuration;
    }

    public SecondaryDocumentStores Parent { get; set; }

    public StoreOptions BuildStoreOptions(IServiceProvider provider)
    {
        var options = _configuration(provider);
        options.StoreName = typeof(T).Name;

        var configures = provider.GetServices<IConfigureMarten<T>>();
        foreach (var configure in configures) configure.Configure(provider, options);

        var globals = provider.GetServices<IConfigureMarten>().OfType<IGlobalConfigureMarten>();
        foreach (var configureMarten in globals)
        {
            configureMarten.Configure(provider, options);
        }

        // #5409: StoreName is assigned ONCE, above, before the IConfigureMarten<T> chain -- so a
        // contribution that names the store is honoured. It used to be assigned a second time here,
        // which silently reverted any such override to the marker type's name with nothing to say so.
        // ReadJasperFxOptions never touches StoreName, so that reassignment guarded nothing.
        //
        // Polecat records the same ordering decision for the same reason (polecat#207: "Set before the
        // IConfigurePolecat<T> chain so a user override still wins"), and Fisher seeds it into the
        // options for the same purpose. Marten was the only one of the three that took the name back.
        //
        // The duplicate ReadJasperFxOptions call that stood between the two assignments went with them.
        // It arrived in "Ongoing work for Marten 8" (d52a6d5e7) rather than deliberately, and the method
        // is idempotent -- every branch is ??= or a guarded list-add -- so calling it once is the same.
        options.ReadJasperFxOptions(provider.GetService<JasperFxOptions>());
        options.Projections.AttachServiceProvider(provider);
        options.Services = provider;

        return options;
    }

    public T Build(IServiceProvider provider)
    {
        var options = BuildStoreOptions(provider);

        // Phase 3 (#4454): hand-built System.Reflection.Emit subclass replaces
        // the Roslyn-emit ICodeFile path. The user's secondary-store interface
        // is a marker (T : IDocumentStore with no extra abstract members) so
        // the proxy only needs a forwarding constructor.
        var storeType = SecondaryStoreProxyFactory.GetOrCreate(typeof(T));
        var store = (T)Activator.CreateInstance(storeType, options)!;
        store.As<DocumentStore>().Subject = new Uri("marten://" + SanitizeForUri(typeof(T)));

        return store;
    }

    // #5039: a closed generic marker interface (e.g. IMartenStoreMarker<MyContext>) has a
    // CLR type name containing a backtick and arity ("IMartenStoreMarker`1"), which is not a
    // valid URI hostname and throws UriFormatException. Strip the arity and fold in the
    // (sanitized) generic argument names so distinct closed generics still map to distinct URIs.
    internal static string SanitizeForUri(Type type) => StoreSubject.Sanitize(type);
}
