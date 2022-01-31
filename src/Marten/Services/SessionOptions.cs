using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Storage;
using Npgsql;
#nullable enable
namespace Marten.Services
{
    public sealed class SessionOptions
    {
        internal IConnectionLifetime Initialize(DocumentStore store, CommandRunnerMode mode)
        {
            Mode = mode;
            Tenant ??= TenantId != null ? store.Tenancy.GetTenant(TenantId) : store.Tenancy.Default;

            if (!store.Options.Advanced.DefaultTenantUsageEnabled &&
                Tenant.TenantId == Marten.Storage.Tenancy.DefaultTenantId)
            {
                throw new DefaultTenantUsageDisabledException();
            }

            if (!OwnsTransactionLifecycle && mode != CommandRunnerMode.ReadOnly)
            {
                Mode = CommandRunnerMode.External;
            }

            // TODO -- blow up if it's serializable???

            if (OwnsConnection && OwnsTransactionLifecycle)
            {
                return mode == CommandRunnerMode.ReadOnly
                    ? new ReadOnlyMartenControlledConnectionTransaction(this)
                    : new MartenControlledConnectionTransaction(this);
            }

            if (Transaction != null)
            {
                return new ExternalTransaction(this);
            }

            if (DotNetTransaction != null)
            {
                return new AmbientTransactionLifetime(this);
            }

            // var connection = new ManagedConnection(this, store.Options.RetryPolicy());
            // connection.BeginSession(); // See if this can be eliminated
            //
            // //_retryPolicy = store.Options.RetryPolicy();
            // // TODO -- blow up if isolation level is serializable

            throw new NotSupportedException("Invalid combination of SessionOptions");
        }

        internal async Task<IConnectionLifetime> InitializeAsync(DocumentStore store)
        {
            // LATER with GH-2048

            // See QuerySession ctor logic as well
            // take over DocumentStore.buildManagedConnection logic here
            // find the tenant
            // throw if tenant is required
            throw new NotImplementedException();
        }

        internal CommandRunnerMode Mode { get; private set; }
        internal Tenant Tenant { get; set; }


        /// <summary>
        /// Create a new session options for the supplied connection string
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
        /// Default to DocumentTracking.IdentityOnly
        /// </summary>
        public DocumentTracking Tracking { get; set; } = DocumentTracking.IdentityOnly;

        /// <summary>
        /// Override the document tracking
        /// </summary>
        /// <param name="tracking"></param>
        /// <returns></returns>
        public SessionOptions WithTracking(DocumentTracking tracking)
        {
            Tracking = tracking;
            return this;
        }

        /// <summary>
        /// If not specified, sessions default to Npgsql command timeout (30 seconds)
        /// </summary>
        public int? Timeout { get; set; }

        /// <summary>
        /// Default to IsolationLevel.ReadCommitted
        /// </summary>
        public System.Data.IsolationLevel IsolationLevel { get; set; } = System.Data.IsolationLevel.ReadCommitted;

        /// <summary>
        ///     Add, remove, or reorder local session listeners
        /// </summary>
        public readonly IList<IDocumentSessionListener> Listeners = new List<IDocumentSessionListener>();

        /// <summary>
        /// Add a single document session listener
        /// </summary>
        /// <param name="listener"></param>
        /// <returns></returns>
        public SessionOptions ListenAt(IDocumentSessionListener listener)
        {
            Listeners.Add(listener);
            return this;
        }

        /// <summary>
        /// Override the tenant id for the requested session
        /// </summary>
        public string TenantId { get; set; } = Tenancy.DefaultTenantId;

        /// <summary>
        /// Use to enable or disable optimistic concurrency for just this session
        /// </summary>
        public ConcurrencyChecks ConcurrencyChecks { get; set; } = ConcurrencyChecks.Enabled;

        /// <summary>
        /// Optional mechanism to open a session with an existing connection
        /// </summary>
        // TODO -- try to make the setter private again
        public NpgsqlConnection? Connection { get; internal set; }

        /// <summary>
        /// Optional mechanism to open a session with an existing transaction
        /// </summary>
        public NpgsqlTransaction? Transaction { get; private set; }

        /// <summary>
        /// Default is true. If false, Marten will issue commands on IDocumentSession.SaveChanges/SaveChangesAsync,
        /// but will **not** commit the transaction
        /// </summary>
        public bool OwnsTransactionLifecycle { get; private set; } = true;

        /// <summary>
        /// Enlist in the native Npgsql transaction and direct the session
        /// *not* to own the transactional lifecycle
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="shouldAutoCommit">Optionally specify whether Marten should commit this transaction when SaveChanges() is called.</param>
        /// <returns></returns>
        public static SessionOptions ForTransaction(NpgsqlTransaction transaction, bool shouldAutoCommit = false) // TODO -- introduce an enum here.
        {
            return new SessionOptions
            {
                Transaction = transaction,
                Connection = transaction.Connection,
                OwnsConnection = false,
                OwnsTransactionLifecycle = shouldAutoCommit,
                Timeout = transaction.Connection.CommandTimeout
            };
        }

        /// <summary>
        /// Create a new session options object using the current, ambient
        /// transaction scope
        /// </summary>
        /// <returns></returns>
        public static SessionOptions ForCurrentTransaction()
        {
            return new SessionOptions().EnlistInAmbientTransactionScope();
        }

        /// <summary>
        /// Enlist the session in the current, ambient transaction scope
        /// </summary>
        public SessionOptions EnlistInAmbientTransactionScope()
        {
            OwnsTransactionLifecycle = false;
            DotNetTransaction = System.Transactions.Transaction.Current!;
            return this;
        }


        /// <summary>
        /// Enlist the session in this transaction
        /// </summary>
        public System.Transactions.Transaction? DotNetTransaction { get; private set; }

        internal bool OwnsConnection { get; set; } = true;

        public static SessionOptions ForConnection(NpgsqlConnection connection)
        {
            return new SessionOptions
            {
                Connection = connection, Timeout = connection.CommandTimeout, OwnsConnection = false
            };
        }
    }

    public enum ConcurrencyChecks
    {
        /// <summary>
        /// Optimistic concurrency checks are enforced (Default)
        /// </summary>
        Enabled,

        /// <summary>
        /// Optimistic concurrency checks are disabled for this session
        /// </summary>
        Disabled
    }
}
