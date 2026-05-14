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
        // One holdout: query plans whose parameters need an
        // ICompiledQueryAwareFilter (string Contains/StartsWith/EndsWith,
        // JSONB containment via .Contains() on a HashSet<>, dictionary
        // ContainsKey, child-collection JsonPath counts). Those filters
        // customize parameter writes through codegen-time GenerateCode hooks
        // with no runtime equivalent — for now, plans containing them fall
        // through to the JasperFx.RuntimeCompiler path below. Lifting that
        // restriction (filter runtime APIs) is tracked as a follow-up to
        // #4405; once it lands, this fallthrough is deleted and a registry
        // miss throws.
        if (CompiledQueryHandlerRegistry.TryGet(query.GetType(), out var descriptor)
            && !PlanRequiresCodegenFilters(plan))
        {
            var enumAsString = _store.Options.Serializer().EnumStorage == EnumStorage.AsString;
            source = new SourceGeneratedCompiledQuerySource<TOut>(plan, descriptor, enumAsString);
            _querySources = _querySources.AddOrUpdate(query.GetType(), source);
            return source;
        }
        // ---- /#4405 iterations 3-4 ----

        // PoC bridge: registry miss or non-Stateless shape falls through to the
        // existing JasperFx.RuntimeCompiler codegen path. This branch is deleted
        // once iteration 4 lands green; the final V9 behavior is "registry miss
        // throws" (#4405 in-issue commentary 2026-05-14).
        var file = new CompiledQueryCodeFile(query.GetType(), _store, plan, _tracking);

        var rules = _store.Options.CreateGenerationRules();
        rules.ReferenceTypes(typeof(TDoc), typeof(TOut), query.GetType());

        // 9.0 (#4309): route through the AllowRuntimeCodeGeneration gate so
        // AOT-friendly hosts can opt out of Roslyn — compiled queries that
        // weren't pre-generated will throw with a descriptive message rather
        // than silently invoke the runtime compiler.
        Marten.Internal.CodeGeneration.StaticOnlyCodeFileLoader.Initialize(
            file, rules, _store, null, _store.Options.AllowRuntimeCodeGeneration);

        source = file.Build(rules);
        _querySources = _querySources.AddOrUpdate(query.GetType(), source);

        return source;
    }

    /// <summary>
    /// Returns <see langword="true"/> if any parameter in the plan needs an
    /// <see cref="Marten.Internal.CompiledQueries.ICompiledQueryAwareFilter"/>
    /// to write its value. Those filters today only emit codegen — there's no
    /// runtime hook for them — so the source-gen path can't fully serve such
    /// plans. Tracked as a follow-up to #4405.
    /// </summary>
    private static bool PlanRequiresCodegenFilters(CompiledQueryPlan plan)
    {
        foreach (var command in plan.Commands)
        {
            foreach (var usage in command.Parameters)
            {
                if (usage.Filter != null) return true;
            }
        }
        return false;
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
        var tenant = new Tenant(StorageConstants.DefaultTenantId, new StandinDatabase(Options));
        using var lightweight =
            (QuerySession)LightweightSession(
                new SessionOptions { AllowAnyTenant = true, Tenant = tenant });

        using var identityMap = (QuerySession)IdentitySession(
            new SessionOptions { AllowAnyTenant = true, Tenant = tenant });
        using var dirty = (QuerySession)DirtyTrackedSession(
            new SessionOptions { AllowAnyTenant = true, Tenant = tenant });


        var options = new SessionOptions { AllowAnyTenant = true, Tenant = tenant };

        var connection = options.Initialize(this, CommandRunnerMode.ReadOnly, Options.OpenTelemetry);

        using var readOnly = new QuerySession(this, options, connection);

        return Options.CompiledQueryTypes.SelectMany(x => new ICodeFile[]
        {
            new CompiledQueryCodeFile(x, this, QueryCompiler.BuildQueryPlan(lightweight, x, Options),
                DocumentTracking.None),
            new CompiledQueryCodeFile(x, this, QueryCompiler.BuildQueryPlan(identityMap, x, Options),
                DocumentTracking.IdentityOnly),
            new CompiledQueryCodeFile(x, this, QueryCompiler.BuildQueryPlan(dirty, x, Options),
                DocumentTracking.DirtyTracking),
            new CompiledQueryCodeFile(x, this, QueryCompiler.BuildQueryPlan(readOnly, x, Options),
                DocumentTracking.QueryOnly)
        }).ToList();
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
