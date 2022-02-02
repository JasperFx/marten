using System;
using System.Collections.Generic;
using Baseline;
using Marten.Events;
using Marten.Services;
using Marten.Storage;
using Npgsql;

#nullable enable
namespace Marten.Internal.Sessions
{
    public partial class QuerySession: IMartenSession, IQuerySession
    {
        protected readonly IRetryPolicy _retryPolicy;
        private readonly DocumentStore _store;

        public ISerializer Serializer { get; }

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

        public string TenantId { get; protected set; }
#nullable enable

        internal QuerySession(DocumentStore store,
            SessionOptions sessionOptions,
            IConnectionLifetime connection)
        {
            _store = store;
            TenantId = sessionOptions.TenantId;
            Database = sessionOptions.Tenant?.Database ?? throw new ArgumentNullException(nameof(SessionOptions.Tenant));

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

            _retryPolicy = Options.RetryPolicy();

            Events = CreateEventStore(store, sessionOptions.Tenant);

            Logger = store.Options.Logger().StartSession(this);
        }

        public ConcurrencyChecks Concurrency { get; protected set; } = ConcurrencyChecks.Enabled;

        public NpgsqlConnection? Connection
        {
            get
            {
                _connection.BeginTransaction();
                return _connection.Connection;
            }
        }

        public IMartenSessionLogger Logger
        {
            get;
            set;
        }

        public int RequestCount { get; set; }
        public IDocumentStore DocumentStore => _store;
    }
}
