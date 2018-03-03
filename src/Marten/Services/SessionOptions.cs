using System.Collections.Generic;
using System.Data;
using Marten.Storage;
using Npgsql;

namespace Marten.Services
{
    public sealed class SessionOptions
    {

        /// <summary>
        /// Default to DocumentTracking.IdentityOnly
        /// </summary>
        public DocumentTracking Tracking { get; set; } = DocumentTracking.IdentityOnly;

        /// <summary>
        /// Default to 30 seconds
        /// </summary>
        public int Timeout { get; set; } = 30;

        /// <summary>
        /// Default to IsolationLevel.ReadCommitted
        /// </summary>
        public System.Data.IsolationLevel IsolationLevel { get; set; } = System.Data.IsolationLevel.ReadCommitted;

        /// <summary>
        ///     Add, remove, or reorder local session listeners
        /// </summary>
        public readonly IList<IDocumentSessionListener> Listeners = new List<IDocumentSessionListener>();

        

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
        public NpgsqlConnection Connection { get; set; }

        /// <summary>
        /// Optional mechanism to open a session with an existing transaction
        /// </summary>
        public NpgsqlTransaction Transaction { get; set; }

        /// <summary>
        /// Default is true. If false, Marten will issue commands on IDocumentSession.SaveChanges/SaveChangesAsync,
        /// but will **not** commit the transaction 
        /// </summary>
        public bool OwnsTransactionLifecycle { get; set; } = true;

        /// <summary>
        /// Enlist in the native Npgsql transaction and direct the session
        /// *not* to own the transactional lifecycle
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public static SessionOptions ForTransaction(NpgsqlTransaction transaction)
        {
            return new SessionOptions
            {
                Transaction = transaction,
                OwnsTransactionLifecycle = false
            };
        }

#if NET46 || NETSTANDARD2_0
        private bool _enlistInAmbientTransactionScope = false;

        /// <summary>
        /// Enlist the session in the current, ambient transaction scope
        /// </summary>
        public bool EnlistInAmbientTransactionScope
        {
            get => _enlistInAmbientTransactionScope;
            set
            {
                if (value)
                {
                    OwnsTransactionLifecycle = false;
                    DotNetTransaction = System.Transactions.Transaction.Current;
                }

                _enlistInAmbientTransactionScope = value;
            }
        }

        /// <summary>
        /// Enlist the session in this transaction
        /// </summary>
        public System.Transactions.Transaction DotNetTransaction { get; set; }

        


        /// <summary>
        /// Open a session that enlists in the current, ambient TransactionScope
        /// </summary>
        /// <returns></returns>
        public static SessionOptions ForCurrentTransaction()
        {
            return new SessionOptions
            {
                EnlistInAmbientTransactionScope = true,
                OwnsTransactionLifecycle = false
            };
        }

#endif

        internal bool OwnsConnection { get; set; } = true;
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