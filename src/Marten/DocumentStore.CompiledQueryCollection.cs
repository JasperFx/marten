using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.RuntimeCompiler;
using Marten.Exceptions;
using Marten.Internal.CompiledQueries;
using Marten.Internal.Sessions;
using Marten.Linq;
using Marten.Services;
using Marten.Storage;

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

    internal ICompiledQuerySource GetCompiledQuerySourceFor<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query,
        QuerySession session)
    {
        if (_querySources.TryFind(query.GetType(), out var source))
        {
            return source;
        }

        if (typeof(TOut).CanBeCastTo<Task>())
        {
            throw InvalidCompiledQueryException.ForCannotBeAsync(query.GetType());
        }

        var plan = QueryCompiler.BuildPlan(session, query, _store.Options);
        var file = new CompiledQueryCodeFile(query.GetType(), _store, plan, _tracking);

        var rules = _store.Options.CreateGenerationRules();
        rules.ReferenceTypes(typeof(TDoc), typeof(TOut), query.GetType());

        file.InitializeSynchronously(rules, _store, null);

        source = file.Build(rules);
        _querySources = _querySources.AddOrUpdate(query.GetType(), source);

        return source;
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
        var tenant = new Tenant(Marten.Storage.Tenancy.DefaultTenantId, new StandinDatabase(Options));
        using var lightweight =
            (QuerySession)OpenSession(
                new SessionOptions { Tracking = DocumentTracking.None, AllowAnyTenant = true, Tenant = tenant });

        using var identityMap = (QuerySession)OpenSession(
            new SessionOptions { Tracking = DocumentTracking.IdentityOnly, AllowAnyTenant = true, Tenant = tenant });
        using var dirty = (QuerySession)OpenSession(
            new SessionOptions { Tracking = DocumentTracking.DirtyTracking, AllowAnyTenant = true, Tenant = tenant });


        var options = new SessionOptions { AllowAnyTenant = true, Tenant = tenant };

        var connection = options.Initialize(this, CommandRunnerMode.ReadOnly);

        using var readOnly = new QuerySession(this, options, connection);

        return Options.CompiledQueryTypes.SelectMany(x => new ICodeFile[]
        {
            new CompiledQueryCodeFile(x, this, QueryCompiler.BuildPlan(lightweight, x, Options),
                DocumentTracking.None),
            new CompiledQueryCodeFile(x, this, QueryCompiler.BuildPlan(identityMap, x, Options),
                DocumentTracking.IdentityOnly),
            new CompiledQueryCodeFile(x, this, QueryCompiler.BuildPlan(dirty, x, Options),
                DocumentTracking.DirtyTracking),
            new CompiledQueryCodeFile(x, this, QueryCompiler.BuildPlan(readOnly, x, Options),
                DocumentTracking.QueryOnly)
        }).ToList();
    }

    string ICodeFileCollection.ChildNamespace { get; } = "CompiledQueries";

    internal ICompiledQuerySource GetCompiledQuerySourceFor<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query,
        QuerySession session)
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
