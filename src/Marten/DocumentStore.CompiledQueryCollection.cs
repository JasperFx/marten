using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ImTools;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.RuntimeCompiler;
using Marten.Exceptions;
using Marten.Internal.CompiledQueries;
using Marten.Internal.Sessions;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Marten.Storage;
using System.Diagnostics.CodeAnalysis;
using Weasel.Core;

namespace Marten;

[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal class CompiledQueryCollection
{
    private readonly DocumentStore _store;
    private readonly DocumentTracking _tracking;
    private ImHashMap<Type, ICompiledQuerySource> _querySources = ImHashMap<Type, ICompiledQuerySource>.Empty;

    public CompiledQueryCollection(DocumentTracking tracking, DocumentStore store)
    {
        _tracking = tracking;
        _store = store;
    }

    internal ICompiledQuerySource GetCompiledQuerySourceFor<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query,
        QuerySession session) where TDoc : notnull
    {
        if (_querySources.TryFind(query.GetType(), out var source))
        {
            return source;
        }

        if (typeof(TOut).CanBeCastTo<Task>())
        {
            throw InvalidCompiledQueryException.ForCannotBeAsync(query.GetType());
        }

        var plan = QueryCompiler.BuildQueryPlan(session, query);

        // ---- #4405 iterations 3-4: source-gen registry-first dispatch ----
        // Implicit opt-in: if the consumer referenced Marten.SourceGenerator and
        // marked the defining assembly with [JasperFxAssembly], a handler
        // descriptor was registered at assembly load via a [ModuleInitializer].
        // Iteration 4 widened the runtime to cover all three handler shapes
        // (Stateless, Cloned, Complex), so the source-gen path serves most
        // registered query types.
        //
        // #4454 Phase 1A+B: ICompiledQueryAwareFilter now exposes a runtime
        // BuildSetter() hook that the source-gen path consumes alongside the
        // descriptor's typed BindParameter. Containment / JsonPath / Contains-
        // style queries are therefore source-gen-eligible too — the prior
        // PlanRequiresCodegenFilters gate is gone.
        if (!CompiledQueryHandlerRegistry.TryGet(query.GetType(), out var descriptor))
        {
            // #4454 Phase 1D — FEC fallback. When no source-gen [ModuleInitializer]
            // registered a descriptor for this query type (consumer assembly missing
            // the [JasperFxAssembly] marker, or a query type registered at runtime via
            // reflection) we build the descriptor reflectively from the freshly-walked
            // CompiledQueryPlan and cache it in the registry. Subsequent calls hit the
            // fast registry path. Replaces the previous fallthrough to
            // CompiledQuerySourceBuilder / JasperFx.RuntimeCompiler.
            descriptor = RuntimeCompiledQueryDescriptorFactory.Build(plan);
            CompiledQueryHandlerRegistry.Register(query.GetType(), descriptor);
        }
        // ---- /#4405 iterations 3-4 ----

        {
            var enumAsString = _store.Options.Serializer().EnumStorage == EnumStorage.AsString;
            source = new SourceGeneratedCompiledQuerySource<TOut>(plan, descriptor, enumAsString);
            _querySources = _querySources.AddOrUpdate(query.GetType(), source);
            return source;
        }
    }
}

public partial class DocumentStore: ICodeFileCollection
{
    private readonly CompiledQueryCollection _dirtyTrackedCompiledQueries;
    private readonly CompiledQueryCollection _identityMapCompiledQueries;
    private readonly CompiledQueryCollection _lightweightCompiledQueries;
    private readonly CompiledQueryCollection _queryOnlyCompiledQueries;

    public GenerationRules Rules => Options.CreateGenerationRules();

    IReadOnlyList<ICodeFile> ICodeFileCollection.BuildFiles()
    {
        // #4454 Phase 1E: compiled queries no longer emit ICodeFile entries —
        // dispatch runs through CompiledQueryHandlerRegistry / source-gen +
        // RuntimeCompiledQueryDescriptorFactory. The codegen path (and the
        // `dotnet marten codegen` CLI surface that consumed it) is retired
        // entirely in Phase 5. Returning an empty list keeps DocumentStore's
        // ICodeFileCollection contract intact for non-compiled-query consumers
        // until then.
        return Array.Empty<ICodeFile>();
    }

    string ICodeFileCollection.ChildNamespace { get; } = "CompiledQueries";

    internal ICompiledQuerySource GetCompiledQuerySourceFor<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query,
        QuerySession session) where TDoc : notnull
    {
        return session.TrackingMode switch
        {
            DocumentTracking.None => _lightweightCompiledQueries.GetCompiledQuerySourceFor(query, session),
            DocumentTracking.IdentityOnly => _identityMapCompiledQueries.GetCompiledQuerySourceFor(query, session),
            DocumentTracking.DirtyTracking =>
                _dirtyTrackedCompiledQueries.GetCompiledQuerySourceFor(query, session),
            DocumentTracking.QueryOnly => _queryOnlyCompiledQueries.GetCompiledQuerySourceFor(query, session),
            _ => throw new ArgumentOutOfRangeException(nameof(session),
                "Unknown document tracking type " + session.TrackingMode)
        };
    }
}
