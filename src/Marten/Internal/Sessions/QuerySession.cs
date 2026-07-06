#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten.Events;
using Marten.Services;
using Marten.Storage;
using Npgsql;
using Polly;

namespace Marten.Internal.Sessions;

public partial class QuerySession: IMartenSession, IQuerySession, ITenantedQuerySession<IQuerySession>
{
    public const string SynchronousRemoval =
        "As of Marten 9.0, only asynchronous data access is supported. Please use the asynchronous equivalent.";

    public const string SynchronousNotSupportedMessage =
        "As of Marten 9.0, only asynchronous data access is supported";

    internal const char DefaultParameterPlaceholder = '?';

    private readonly DocumentStore _store;
    private readonly ResiliencePipeline _resilience;

    internal virtual DocumentTracking TrackingMode => DocumentTracking.QueryOnly;

    public ISerializer Serializer { get; }

    // #4819: the public ISerializer satisfies IMartenSession.Serializer (new ISerializer); this
    // explicit impl satisfies the narrower IStorageSession.Serializer (IStorageSerializer) — C#
    // interface implementation needs an exact return type, and ISerializer : IStorageSerializer.
    IStorageSerializer IStorageSession.Serializer => Serializer;

    // #4825: concrete Versions satisfies IMartenSession (new VersionTracker); this explicit impl
    // satisfies the narrower IStorageSession.Versions (IVersionTracker).
    IVersionTracker IStorageSession.Versions => Versions;

    public StoreOptions Options { get; }
    public IQueryEventStore Events { get; }

    protected virtual IQueryEventStore CreateEventStore(DocumentStore store, Tenant tenant)
    {
        return new QueryEventStore(this, store, tenant);
    }

    public IList<IDocumentSessionListener> Listeners { get; } = new List<IDocumentSessionListener>();

    internal SessionOptions SessionOptions { get; }

    /// <summary>
    ///     Used for code generation
    /// </summary>
#nullable disable

    public IMartenDatabase Database { get; protected set; }

    // #4827: the public IMartenDatabase satisfies IMartenSession.Database (new IMartenDatabase);
    // this explicit impl satisfies the narrower IStorageSession.Database (IStorageDatabase) —
    // interface implementation needs an exact return type, and IMartenDatabase : IStorageDatabase.
    IStorageDatabase IStorageSession.Database => Database;


#nullable enable

    internal QuerySession(
        DocumentStore store,
        SessionOptions sessionOptions,
        IConnectionLifetime connection,
        Tenant? tenant = default
    )
    {
        _store = store;
        TenantId = store.Options.TenantIdStyle.MaybeCorrectTenantId(tenant?.TenantId ?? sessionOptions.Tenant?.TenantId ?? sessionOptions.TenantId);
        Database = tenant?.Database ?? sessionOptions.Tenant?.Database ??
            throw new ArgumentNullException(nameof(SessionOptions.Tenant));

        SessionOptions = sessionOptions;

        Listeners.AddRange(store.Options.Listeners);

        if (sessionOptions.Timeout is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionOptions.Timeout),
                "CommandTimeout can't be less than zero");
        }

        Listeners.AddRange(sessionOptions.Listeners);
        _providers = sessionOptions.Tenant.Database.Providers ??
                     throw new ArgumentNullException(nameof(IMartenDatabase.Providers));

        _connection = connection;
        Serializer = store.Serializer;
        Options = store.Options;

        Events = CreateEventStore(store, tenant ?? sessionOptions.Tenant);

        Logger = store.Options.Logger().StartSession(this);

        _resilience = Options.ResiliencePipeline;
    }

    public ConcurrencyChecks Concurrency { get; protected set; } = ConcurrencyChecks.Enabled;

    public virtual NpgsqlConnection Connection
    {
        get
        {
            switch (_connection)
            {
                case IAlwaysConnectedLifetime lifetime:
                    return lifetime.Connection;
                case ITransactionStarter starter:
                {
                    var l = starter.Start();
                    _connection = l;
                    return l.Connection;
                }
                default:
                    throw new InvalidOperationException(
                        $"The current lifetime {_connection} is neither a {nameof(IAlwaysConnectedLifetime)} nor a {nameof(ITransactionStarter)}");
            }
        }
    }

    public IMartenSessionLogger Logger
    {
        get => _connection.Logger;
        set => _connection.Logger = value;
    }

    public int RequestCount { get; set; }
    public IDocumentStore DocumentStore => _store;

    public IAdvancedSql AdvancedSql => this;
    public Task<T> QueryByPlanAsync<T>(IQueryPlan<T> plan, CancellationToken token = default)
    {
        // This is literally like this *just* to make mocking easier -- even though I don't agree with that often!
        return plan.Fetch(this, token);
    }

    IQuerySession ITenantedQuerySession<IQuerySession>.ForTenant(string tenantId)
    {
        return ForTenant(tenantId);
    }
}
