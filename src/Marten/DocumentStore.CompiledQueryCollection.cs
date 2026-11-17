using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ImTools;
using JasperFx;
using JasperFx.Core;
using JasperFx.Core.Reflection;
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

    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers supply source-generator-backed serializer impls per the AOT publishing guide.")]
    internal ICompiledQuerySource GetCompiledQuerySourceFor<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query,
        QuerySession session) where TDoc : notnull
    {
        // QueryAcrossAllDatabasesAsync: the plan bakes the session's tenant filter into its SQL, so a session
        // reading every tenant (or a list of them) gets a plan of its own that is never cached for, or
        // shared with, ordinary sessions
        if (session.TenantScope != null)
        {
            return buildTenantScopedSource(query, session);
        }

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
            // CompiledQueryPlan and cache it in the registry. Subsequent calls hit
            // the fast registry path.
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

    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers supply source-generator-backed serializer impls per the AOT publishing guide.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Same runtime descriptor fallback as GetCompiledQuerySourceFor.")]
    private ICompiledQuerySource buildTenantScopedSource<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query,
        QuerySession session) where TDoc : notnull
    {
        if (typeof(TOut).CanBeCastTo<Task>())
        {
            throw InvalidCompiledQueryException.ForCannotBeAsync(query.GetType());
        }

        var plan = QueryCompiler.BuildQueryPlan(session, query);

        // The descriptor binds the query type's members and does not depend on the SQL, so a registered one
        // is safe to use here. One built here is not registered.
        if (!CompiledQueryHandlerRegistry.TryGet(query.GetType(), out var descriptor))
        {
            descriptor = RuntimeCompiledQueryDescriptorFactory.Build(plan);
        }

        var enumAsString = _store.Options.Serializer().EnumStorage == EnumStorage.AsString;
        return new SourceGeneratedCompiledQuerySource<TOut>(plan, descriptor, enumAsString);
    }
}

public partial class DocumentStore
{
    private readonly CompiledQueryCollection _dirtyTrackedCompiledQueries;
    private readonly CompiledQueryCollection _identityMapCompiledQueries;
    private readonly CompiledQueryCollection _lightweightCompiledQueries;
    private readonly CompiledQueryCollection _queryOnlyCompiledQueries;

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
