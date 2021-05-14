using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.HighWater;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Marten.Transforms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
#nullable enable
namespace Marten
{
    /// <summary>
    ///     The main entry way to using Marten
    /// </summary>
    public class  DocumentStore: IDocumentStore
    {
        private readonly IMartenLogger _logger;
        private readonly IRetryPolicy _retryPolicy;

        /// <summary>
        ///     Creates a new DocumentStore with the supplied StoreOptions
        /// </summary>
        /// <param name="options"></param>
        public DocumentStore(StoreOptions options)
        {
            options.ApplyConfiguration();
            options.CreatePatching();
            options.Validate();

            Options = options;
            _logger = options.Logger();
            Serializer = options.Serializer();
            _retryPolicy = options.RetryPolicy();

            if (options.CreateDatabases != null)
            {
                IDatabaseGenerator databaseGenerator = new DatabaseGenerator();
                databaseGenerator.CreateDatabases(Tenancy, options.CreateDatabases);
            }

            //Tenancy.Initialize();

            Schema = Tenancy.Schema;

            Storage.PostProcessConfiguration();
            Events.AssertValidity(this);
            Options.Projections.AssertValidity(this);

            Advanced = new AdvancedOperations(this);

            Diagnostics = new Diagnostics(this);

            Transform = new DocumentTransforms(this, Tenancy.Default);

            options.InitialData.Each(x => x.Populate(this).GetAwaiter().GetResult());
        }

        IReadOnlyStoreOptions IDocumentStore.Options => Options;

        public ITenancy Tenancy => Options.Tenancy;

        public EventGraph Events => Options.EventGraph;

        public StorageFeatures Storage => Options.Storage;

        public ISerializer Serializer { get; }

        public StoreOptions Options { get; }

        public virtual void Dispose()
        {
        }

        public IDocumentSchema Schema { get; }
        public AdvancedOperations Advanced { get; }

        public void BulkInsert<T>(IReadOnlyCollection<T> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
            int batchSize = 1000)
        {
            var bulkInsertion = new BulkInsertion(Tenancy.Default, Options);
            bulkInsertion.BulkInsert(documents, mode, batchSize);
        }

        public void BulkInsertDocuments(IEnumerable<object> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
            int batchSize = 1000)
        {
            var bulkInsertion = new BulkInsertion(Tenancy.Default, Options);
            bulkInsertion.BulkInsertDocuments(documents, mode, batchSize);
        }

        public void BulkInsert<T>(string tenantId, IReadOnlyCollection<T> documents,
            BulkInsertMode mode = BulkInsertMode.InsertsOnly,
            int batchSize = 1000)
        {
            var bulkInsertion = new BulkInsertion(Tenancy[tenantId], Options);
            bulkInsertion.BulkInsert(documents, mode, batchSize);
        }

        public void BulkInsertDocuments(string tenantId, IEnumerable<object> documents,
            BulkInsertMode mode = BulkInsertMode.InsertsOnly,
            int batchSize = 1000)
        {
            var bulkInsertion = new BulkInsertion(Tenancy[tenantId], Options);
            bulkInsertion.BulkInsertDocuments(documents, mode, batchSize);
        }

        public Task BulkInsertAsync<T>(IReadOnlyCollection<T> documents,
            BulkInsertMode mode = BulkInsertMode.InsertsOnly,
            int batchSize = 1000, CancellationToken cancellation = default)
        {
            var bulkInsertion = new BulkInsertion(Tenancy.Default, Options);
            return bulkInsertion.BulkInsertAsync(documents, mode, batchSize, cancellation);
        }

        public Task BulkInsertAsync<T>(string tenantId, IReadOnlyCollection<T> documents,
            BulkInsertMode mode = BulkInsertMode.InsertsOnly, int batchSize = 1000,
            CancellationToken cancellation = default)
        {
            var bulkInsertion = new BulkInsertion(Tenancy[tenantId], Options);
            return bulkInsertion.BulkInsertAsync(documents, mode, batchSize, cancellation);
        }

        public Task BulkInsertDocumentsAsync(IEnumerable<object> documents,
            BulkInsertMode mode = BulkInsertMode.InsertsOnly,
            int batchSize = 1000, CancellationToken cancellation = default)
        {
            var bulkInsertion = new BulkInsertion(Tenancy.Default, Options);
            return bulkInsertion.BulkInsertDocumentsAsync(documents, mode, batchSize, cancellation);
        }

        public Task BulkInsertDocumentsAsync(string tenantId, IEnumerable<object> documents,
            BulkInsertMode mode = BulkInsertMode.InsertsOnly,
            int batchSize = 1000, CancellationToken cancellation = default)
        {
            var bulkInsertion = new BulkInsertion(Tenancy[tenantId], Options);
            return bulkInsertion.BulkInsertDocumentsAsync(documents, mode, batchSize, cancellation);
        }

        public IDiagnostics Diagnostics { get; }

        public IDocumentSession OpenSession(SessionOptions options)
        {
            return openSession(options);
        }

        public IDocumentSession OpenSession(DocumentTracking tracking = DocumentTracking.IdentityOnly,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            return openSession(new SessionOptions {Tracking = tracking, IsolationLevel = isolationLevel});
        }

        public IDocumentSession OpenSession(string tenantId, DocumentTracking tracking = DocumentTracking.IdentityOnly,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            return openSession(new SessionOptions
            {
                Tracking = tracking, IsolationLevel = isolationLevel, TenantId = tenantId
            });
        }

        public IDocumentSession DirtyTrackedSession(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            return OpenSession(DocumentTracking.DirtyTracking, isolationLevel);
        }

        public IDocumentSession DirtyTrackedSession(string tenantId,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            return OpenSession(tenantId, DocumentTracking.DirtyTracking, isolationLevel);
        }

        public IDocumentSession LightweightSession(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            return OpenSession(DocumentTracking.None, isolationLevel);
        }

        public IDocumentSession LightweightSession(string tenantId,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            return OpenSession(tenantId, DocumentTracking.None, isolationLevel);
        }

        public IQuerySession QuerySession(SessionOptions options)
        {
            var tenant = Tenancy[options.TenantId];

            if (!Options.Advanced.DefaultTenantUsageEnabled && tenant.TenantId == Marten.Storage.Tenancy.DefaultTenantId)
                throw new DefaultTenantUsageDisabledException();

            var connection = buildManagedConnection(options, tenant, CommandRunnerMode.ReadOnly, _retryPolicy);
            var session = new QuerySession(this, options, connection, tenant);

            connection.BeginSession();

            session.Logger = _logger.StartSession(session);

            return session;
        }

        public IQuerySession QuerySession()
        {
            return QuerySession(Marten.Storage.Tenancy.DefaultTenantId);
        }

        public IQuerySession QuerySession(string tenantId)
        {
            var tenant = Tenancy[tenantId];

            if (!Options.Advanced.DefaultTenantUsageEnabled && tenant.TenantId == Marten.Storage.Tenancy.DefaultTenantId)
                throw new DefaultTenantUsageDisabledException();


            var connection = tenant.OpenConnection(CommandRunnerMode.ReadOnly);

            var session = new QuerySession(this, null, connection, tenant);

            connection.BeginSession();

            session.Logger = _logger.StartSession(session);

            return session;
        }

        public IDocumentTransforms Transform { get; }
        public IProjectionDaemon BuildProjectionDaemon(ILogger? logger = null)
        {
            logger ??= new NulloLogger();

            // TODO -- this will vary later
            var detector = new HighWaterDetector(new AutoOpenSingleQueryRunner(Tenancy.Default), Events);

            return new ProjectionDaemon(this, detector, logger);
        }

        /// <summary>
        ///     Quick way to stand up a DocumentStore to the given database connection
        ///     in the "development" mode for auto-creating schema objects as needed
        ///     with the default behaviors
        /// </summary>
        /// <param name="connectionString"></param>
        /// <returns></returns>
        public static DocumentStore For(string connectionString)
        {
            return For(_ =>
            {
                _.Connection(connectionString);
            });
        }

        /// <summary>
        ///     Configures a DocumentStore for an existing StoreOptions type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static DocumentStore For<T>() where T : StoreOptions, new()
        {
            return new(new T());
        }

        /// <summary>
        ///     Configures a DocumentStore by defining the StoreOptions settings first
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static DocumentStore For(Action<StoreOptions> configure)
        {
            var options = new StoreOptions();
            configure(options);

            return new DocumentStore(options);
        }

        private IDocumentSession openSession(SessionOptions options)
        {
            var tenant = Tenancy[options.TenantId];

            if (!Options.Advanced.DefaultTenantUsageEnabled && tenant.TenantId == Marten.Storage.Tenancy.DefaultTenantId)
                throw new DefaultTenantUsageDisabledException();

            var connection = buildManagedConnection(options, tenant, CommandRunnerMode.Transactional, _retryPolicy);
            connection.BeginSession();

            IDocumentSession session = options.Tracking switch
            {
                DocumentTracking.None => new LightweightSession(this, options, connection, tenant),
                DocumentTracking.IdentityOnly => new IdentityMapDocumentSession(this, options, connection, tenant),
                DocumentTracking.DirtyTracking => new DirtyCheckingDocumentSession(this, options, connection, tenant),
                _ => throw new ArgumentOutOfRangeException(nameof(SessionOptions.Tracking))
            };

            session.Logger = _logger.StartSession(session);

            return session;
        }

        private static IManagedConnection buildManagedConnection(SessionOptions options, ITenant tenant,
            CommandRunnerMode commandRunnerMode, IRetryPolicy retryPolicy)
        {
            // TODO -- this is all spaghetti code. Make this some kind of more intelligent state machine
            // w/ the logic encapsulated into SessionOptions

            // Hate crap like this, but if we don't control the transation, use External to direct
            // IManagedConnection not to call commit or rollback
            if (!options.OwnsTransactionLifecycle && commandRunnerMode != CommandRunnerMode.ReadOnly)
                commandRunnerMode = CommandRunnerMode.External;

            if (options.Connection != null || options.Transaction != null) options.OwnsConnection = false;

            if (options.Transaction != null) options.Connection = options.Transaction.Connection;

            if (options.Connection == null && options.DotNetTransaction != null)
            {
                var connection = tenant.CreateConnection();
                connection.Open();

                options.OwnsConnection = true;
                options.Connection = connection;
            }

            if (options.DotNetTransaction != null)
            {
                options.Connection!.EnlistTransaction(options.DotNetTransaction);
                options.OwnsTransactionLifecycle = false;
            }

            if (options.Connection == null)
                return tenant.OpenConnection(commandRunnerMode, options.IsolationLevel, options.Timeout);
            return new ManagedConnection(options, commandRunnerMode, retryPolicy);
        }
    }



    internal class NulloLogger : ILogger, IDisposable
    {

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var message = $"{logLevel}: {formatter(state, exception)}";
            Debug.WriteLine(message);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return this;
        }


        public void Dispose()
        {
            // Nothing
        }
    }


}
