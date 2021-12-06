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

        public ISerializer Serializer { get; }

        public Tenant Tenant { get; protected set; }
        public StoreOptions Options { get; }
        public IQueryEventStore Events { get; }

        protected virtual IQueryEventStore CreateEventStore(DocumentStore store, Tenant tenant)
        {
            return new QueryEventStore(this, store, tenant);
        }

        public IList<IDocumentSessionListener> Listeners { get; } = new List<IDocumentSessionListener>();

        internal SessionOptions? SessionOptions { get; }

        /// <summary>
        ///     Used for code generation
        /// </summary>
#nullable disable

        // TODO -- this can be eliminated with some sort of LightweightMartenSession. Only used in code generation.
        protected QuerySession(StoreOptions options)
        {
            Serializer = options.Serializer();
            Tenant = options.Tenancy.Default;
            Options = options;
            _providers = options.Providers;
            _retryPolicy = options.RetryPolicy();
        }
#nullable enable

        internal QuerySession(DocumentStore store,
            SessionOptions sessionOptions,
            IConnectionLifetime connection)
        {
            DocumentStore = store;

            SessionOptions = sessionOptions;

            Listeners.AddRange(store.Options.Listeners);

            if (sessionOptions.Timeout is < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sessionOptions.Timeout),
                    "CommandTimeout can't be less than zero");
            }

            Listeners.AddRange(sessionOptions.Listeners);
            _providers = sessionOptions.Tenant.Storage.Providers ??
                         throw new ArgumentNullException(nameof(IMartenDatabase.Providers));

            _connection = connection;
            Serializer = store.Serializer;
            Tenant = sessionOptions.Tenant;
            Options = store.Options;

            _retryPolicy = Options.RetryPolicy();

            Events = CreateEventStore(store, Tenant);
        }

        public ConcurrencyChecks Concurrency { get; protected set; } = ConcurrencyChecks.Enabled;

        public NpgsqlConnection Connection
        {
            get
            {
                // TODO -- I don't like this. Potentially mixes
                // sync and async code.
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
        public IDocumentStore DocumentStore { get; }
    }
}
