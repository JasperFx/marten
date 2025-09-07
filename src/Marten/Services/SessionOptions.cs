#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using JasperFx;
using JasperFx.MultiTenancy;
using Marten.Exceptions;
using Marten.Internal.OpenTelemetry;
using Marten.Internal.Sessions;
using Marten.Storage;
using Npgsql;
using IsolationLevel = System.Data.IsolationLevel;

namespace Marten.Services;

public sealed class SessionOptions
{
    /// <summary>
    ///     Add, remove, or reorder local session listeners
    /// </summary>
    public readonly IList<IDocumentSessionListener> Listeners = new List<IDocumentSessionListener>();

    internal CommandRunnerMode Mode { get; private set; }
    internal Tenant? Tenant { get; set; }

    // Note: recent one
    /// <summary>
    /// Define the type of session document tracking you'd like to open.
    /// We recommend using lightweight session, and this is the default.<br/>
    /// Read more in documentation: https://martendb.io/documents/sessions.html.
    /// </summary>
    public DocumentTracking Tracking { get; set; } = DocumentTracking.None;

    /// <summary>
    ///     If not specified, sessions default to Npgsql command timeout (30 seconds)
    /// </summary>
    public int? Timeout { get; set; }

    /// <summary>
    ///     Default to IsolationLevel.ReadCommitted
    /// </summary>
    public IsolationLevel IsolationLevel { get; set; } = IsolationLevel.ReadCommitted;

    /// <summary>
    ///     Override the tenant id for the requested session
    /// </summary>
    public string TenantId { get; set; } = StorageConstants.DefaultTenantId;

    /// <summary>
    ///     Use to enable or disable optimistic concurrency for just this session
    /// </summary>
    public ConcurrencyChecks ConcurrencyChecks { get; set; } = ConcurrencyChecks.Enabled;

    /// <summary>
    ///     Optional mechanism to open a session with an existing connection
    /// </summary>
    public NpgsqlConnection? Connection { get; internal set; }

    /// <summary>
    ///     Optional mechanism to open a session with an existing transaction
    /// </summary>
    public NpgsqlTransaction? Transaction { get; private set; }

    /// <summary>
    ///     Default is true. If false, Marten will issue commands on IDocumentSession.SaveChanges/SaveChangesAsync,
    ///     but will **not** commit the transaction
    /// </summary>
    public bool OwnsTransactionLifecycle { get; private set; } = true;


    /// <summary>
    ///     Enlist the session in this transaction
    /// </summary>
    public Transaction? DotNetTransaction { get; private set; }

    internal bool OwnsConnection { get; set; } = true;

    /// <summary>
    ///     If set to true, this allows a session to be opened for "any"
    ///     tenant even if the StoreOptions.Advanced.DefaultTenantUsageEnabled is disabled normally
    ///     in this DocumentStore
    /// </summary>
    public bool AllowAnyTenant { get; set; }

    internal IConnectionLifetime Initialize(DocumentStore store, CommandRunnerMode mode,
        OpenTelemetryOptions telemetryOptions)
    {
        Mode = mode;
        Tenant ??= TenantId != StorageConstants.DefaultTenantId ? store.Tenancy.GetTenant(store.Options.TenantIdStyle.MaybeCorrectTenantId(TenantId)) : store.Tenancy.Default;

        if (!AllowAnyTenant && !store.Options.Advanced.DefaultTenantUsageEnabled &&
            (Tenant == null || Tenant.TenantId == StorageConstants.DefaultTenantId))
        {
            throw new DefaultTenantUsageDisabledException();
        }

        var innerConnectionLifetime = buildConnectionLifetime(store, mode);

        return telemetryOptions.TrackConnections == TrackLevel.None || !MartenTracing.ActivitySource.HasListeners()
            ? innerConnectionLifetime
            : new EventTracingConnectionLifetime(innerConnectionLifetime, Tenant.TenantId, telemetryOptions);
    }

    private IConnectionLifetime buildConnectionLifetime(DocumentStore store, CommandRunnerMode mode)
    {
        if (!OwnsTransactionLifecycle && mode != CommandRunnerMode.ReadOnly)
        {
            Mode = CommandRunnerMode.External;
        }

        if (OwnsConnection && OwnsTransactionLifecycle)
        {
            if (IsolationLevel == IsolationLevel.Serializable)
            {
                var transaction = mode == CommandRunnerMode.ReadOnly
                    ? new ReadOnlyTransactionalConnection(this) { CommandTimeout = Timeout ?? store.Options.CommandTimeout }
                    : new TransactionalConnection(this) { CommandTimeout = Timeout ?? store.Options.CommandTimeout };
                transaction.BeginTransaction();

                return transaction;
            }
            else if (store.Options.UseStickyConnectionLifetimes)
            {
                return new TransactionalConnection(this) { CommandTimeout = Timeout ?? store.Options.CommandTimeout };
            }

            {
                return new AutoClosingLifetime(this, store.Options);
            }
        }


        if (Transaction != null)
        {
            return new ExternalTransaction(this) { CommandTimeout = Timeout ?? store.Options.CommandTimeout };
        }


        if (DotNetTransaction != null)
        {
            return new AmbientTransactionLifetime(this) { CommandTimeout = Timeout ?? store.Options.CommandTimeout };
        }

        if (Connection != null)
        {
            return new TransactionalConnection(this) { CommandTimeout = Timeout ?? store.Options.CommandTimeout };
        }


        throw new NotSupportedException("Invalid combination of SessionOptions");
    }

    internal async Task<IConnectionLifetime> InitializeAsync(DocumentStore store, CommandRunnerMode mode,
        CancellationToken token)
    {
        Mode = mode;
        Tenant ??= TenantId != StorageConstants.DefaultTenantId
            ? await store.Tenancy.GetTenantAsync(store.Options.TenantIdStyle.MaybeCorrectTenantId(TenantId)).ConfigureAwait(false)
            : store.Tenancy.Default;

        if (!AllowAnyTenant && !store.Options.Advanced.DefaultTenantUsageEnabled &&
            Tenant.TenantId == StorageConstants.DefaultTenantId)
        {
            throw new DefaultTenantUsageDisabledException();
        }

        if (!OwnsTransactionLifecycle && mode != CommandRunnerMode.ReadOnly)
        {
            Mode = CommandRunnerMode.External;
        }

        if (OwnsConnection && OwnsTransactionLifecycle)
        {
            var transaction = mode == CommandRunnerMode.ReadOnly
                ? new ReadOnlyTransactionalConnection(this)
                : new TransactionalConnection(this);

            if (IsolationLevel == IsolationLevel.Serializable)
            {
                await transaction.BeginTransactionAsync(token).ConfigureAwait(false);
            }

            return transaction;
        }

        if (Transaction != null)
        {
            return new ExternalTransaction(this);
        }

        if (DotNetTransaction != null)
        {
            return new AmbientTransactionLifetime(this);
        }

        throw new NotSupportedException("Invalid combination of SessionOptions");
    }


    /// <summary>
    ///     Create a new session options for the supplied connection string
    /// </summary>
    /// <param name="connectionString"></param>
    /// <returns></returns>
    public static SessionOptions ForConnectionString(string connectionString)
    {
        return new SessionOptions
        {
            Connection = new NpgsqlConnection(connectionString),
            OwnsConnection = true,
            OwnsTransactionLifecycle = true
        };
    }

    /// <summary>
    ///     Create a session for all tenants within the supplied database
    /// </summary>
    /// <param name="database"></param>
    /// <returns></returns>
    public static SessionOptions ForDatabase(IMartenDatabase database) =>
        ForDatabase(StorageConstants.DefaultTenantId, database);

    /// <summary>
    ///     Create a session for tenant within the supplied database
    /// </summary>
    /// <param name="tenantId"></param>
    /// <param name="database"></param>
    /// <returns></returns>
    public static SessionOptions ForDatabase(string tenantId, IMartenDatabase database)
    {
        return new SessionOptions
        {
            Tenant = new Tenant(tenantId, database),
            AllowAnyTenant = true,
            OwnsConnection = true,
            OwnsTransactionLifecycle = true,
            Tracking = DocumentTracking.None
        };
    }

    /// <summary>
    ///     Override the document tracking
    /// </summary>
    /// <param name="tracking"></param>
    /// <returns></returns>
    public SessionOptions WithTracking(DocumentTracking tracking)
    {
        Tracking = tracking;
        return this;
    }

    /// <summary>
    ///     Add a single document session listener
    /// </summary>
    /// <param name="listener"></param>
    /// <returns></returns>
    public SessionOptions ListenAt(IDocumentSessionListener listener)
    {
        Listeners.Add(listener);
        return this;
    }

    /// <summary>
    ///     Enlist in the native Npgsql transaction and direct the session
    ///     *not* to own the transactional lifecycle
    /// </summary>
    /// <param name="transaction"></param>
    /// <param name="shouldAutoCommit">
    ///     Optionally specify whether Marten should commit this transaction when SaveChanges() is
    ///     called.
    /// </param>
    /// <returns></returns>
    public static SessionOptions
        ForTransaction(NpgsqlTransaction transaction, bool shouldAutoCommit = false) // TODO -- introduce an enum here.
    {
        return new SessionOptions
        {
            Transaction = transaction,
            Connection = transaction.Connection,
            OwnsConnection = false,
            OwnsTransactionLifecycle = shouldAutoCommit,
            Timeout = transaction.Connection?.CommandTimeout
        };
    }

    /// <summary>
    ///     Create a new session options object using the current, ambient
    ///     transaction scope. NOTE THAT MARTEN'S AUTOMATIC DATABASE MIGRATIONS
    ///     DO NOT WORK USING THIS OPTION
    /// </summary>
    /// <returns></returns>
    public static SessionOptions ForCurrentTransaction()
    {
        return new SessionOptions().EnlistInAmbientTransactionScope();
    }

    /// <summary>
    ///     Enlist the session in the current, ambient transaction scope
    /// </summary>
    public SessionOptions EnlistInAmbientTransactionScope()
    {
        OwnsTransactionLifecycle = false;
        DotNetTransaction = System.Transactions.Transaction.Current!;
        return this;
    }

    public static SessionOptions ForConnection(NpgsqlConnection connection)
    {
        return new SessionOptions
        {
            Connection = connection,
            Timeout = connection.CommandTimeout,
            OwnsConnection = connection.State == ConnectionState.Closed
        };
    }
}

public enum ConcurrencyChecks
{
    /// <summary>
    ///     Optimistic concurrency checks are enforced (Default)
    /// </summary>
    Enabled,

    /// <summary>
    ///     Optimistic concurrency checks are disabled for this session
    /// </summary>
    Disabled
}
