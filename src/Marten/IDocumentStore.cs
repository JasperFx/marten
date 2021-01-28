using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Services;
using Marten.Transforms;
using Microsoft.Extensions.Logging;
using IsolationLevel = System.Data.IsolationLevel;

namespace Marten
{
    /// <summary>
    /// The core abstraction for a Marten document and event store. This should probably be scoped as a
    /// singleton in your system
    /// </summary>
    public interface IDocumentStore: IDisposable
    {
        /// <summary>
        ///     Information about the document and event storage
        /// </summary>
        IDocumentSchema Schema { get; }

        /// <summary>
        ///     Infrequently used operations like document cleaning and the initial store configuration
        /// </summary>
        AdvancedOptions Advanced { get; }

        /// <summary>
        ///     Access to Marten's diagnostics for trouble shooting
        /// </summary>
        IDiagnostics Diagnostics { get; }

        /// <summary>
        ///     Uses Postgresql's COPY ... FROM STDIN BINARY feature to efficiently store
        ///     a large number of documents of type "T" to the database. This operation is transactional.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="documents"></param>
        /// <param name="mode"></param>
        /// <param name="batchSize"></param>
        void BulkInsert<T>(IReadOnlyCollection<T> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly, int batchSize = 1000);

        /// <summary>
        ///     Uses Postgresql's COPY ... FROM STDIN BINARY feature to efficiently store
        ///     a large number of documents of type "T" to the database. This operation is transactional.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="tenantId"></param>
        /// <param name="documents"></param>
        /// <param name="mode"></param>
        /// <param name="batchSize"></param>
        void BulkInsert<T>(string tenantId, IReadOnlyCollection<T> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly, int batchSize = 1000);

        /// <summary>
        ///     Uses Postgresql's COPY ... FROM STDIN BINARY feature to efficiently store
        ///     a large number of documents of type "T" to the database. This operation is transactional.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="documents"></param>
        /// <param name="mode"></param>
        /// <param name="batchSize"></param>
        Task BulkInsertAsync<T>(IReadOnlyCollection<T> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly, int batchSize = 1000, CancellationToken cancellation = default(CancellationToken));

        /// <summary>
        ///     Uses Postgresql's COPY ... FROM STDIN BINARY feature to efficiently store
        ///     a large number of documents of type "T" to the database. This operation is transactional.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="tenantId"></param>
        /// <param name="documents"></param>
        /// <param name="mode"></param>
        /// <param name="batchSize"></param>
        Task BulkInsertAsync<T>(string tenantId, IReadOnlyCollection<T> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly, int batchSize = 1000, CancellationToken cancellation = default(CancellationToken));




        /// <summary>
        ///     Open a new IDocumentSession with the supplied DocumentTracking.
        ///     "IdentityOnly" is the default.
        /// </summary>
        /// <param name="tracking"></param>
        /// <returns></returns>
        IDocumentSession OpenSession(DocumentTracking tracking = DocumentTracking.IdentityOnly,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

        /// <summary>
        ///     Open a new IDocumentSession with the supplied DocumentTracking.
        ///     "IdentityOnly" is the default.
        /// </summary>
        /// <param name="tracking"></param>
        /// <returns></returns>
        IDocumentSession OpenSession(string tenantId, DocumentTracking tracking = DocumentTracking.IdentityOnly,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

        /// <summary>
        ///     Open a new IDocumentSession with the supplied DocumentTracking.
        ///     "IdentityOnly" is the default.
        /// </summary>
        /// <param name="options">Additional options for session</param>
        /// <returns></returns>
        IDocumentSession OpenSession(SessionOptions options);

        /// <summary>
        ///     Convenience method to create a new "lightweight" IDocumentSession with no IdentityMap
        ///     or automatic dirty checking
        /// </summary>
        /// <returns></returns>
        IDocumentSession LightweightSession(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

        /// <summary>
        ///     Convenience method to create a new "lightweight" IDocumentSession with no IdentityMap
        ///     or automatic dirty checking
        /// </summary>
        /// <returns></returns>
        IDocumentSession LightweightSession(string tenantId, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

        /// <summary>
        ///     Convenience method to create an IDocumentSession with both IdentityMap and automatic
        ///     dirty checking
        /// </summary>
        /// <returns></returns>
        IDocumentSession DirtyTrackedSession(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

        /// <summary>
        ///     Convenience method to create an IDocumentSession with both IdentityMap and automatic
        ///     dirty checking
        /// </summary>
        /// <returns></returns>
        IDocumentSession DirtyTrackedSession(string tenantId, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

        /// <summary>
        ///     Opens a read-only IQuerySession to the current document store for efficient
        ///     querying without any underlying object tracking.
        /// </summary>
        /// <returns></returns>
        IQuerySession QuerySession();

        /// <summary>
        ///     Opens a read-only IQuerySession to the current document store for efficient
        ///     querying without any underlying object tracking.
        /// </summary>
        /// <returns></returns>
        IQuerySession QuerySession(string tenantId);

        /// <summary>
        ///     Opens a read-only IQuerySession to the current document store for efficient
        ///     querying without any underlying object tracking.
        /// </summary>
        /// <param name="options">Additional options for session. DocumentTracking is not applicable for IQuerySession.</param>
        /// <returns></returns>
        IQuerySession QuerySession(SessionOptions options);

        /// <summary>
        ///     Bulk insert a potentially mixed enumerable of document types
        /// </summary>
        /// <param name="documents"></param>
        /// <param name="mode"></param>
        /// <param name="batchSize"></param>
        void BulkInsertDocuments(IEnumerable<object> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
            int batchSize = 1000);

        /// <summary>
        ///     Bulk insert a potentially mixed enumerable of document types
        /// </summary>
        /// <param name="documents"></param>
        /// <param name="mode"></param>
        /// <param name="batchSize"></param>
        void BulkInsertDocuments(string tenantId, IEnumerable<object> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
            int batchSize = 1000);

        /// <summary>
        ///     Bulk insert a potentially mixed enumerable of document types
        /// </summary>
        /// <param name="documents"></param>
        /// <param name="mode"></param>
        /// <param name="batchSize"></param>
        Task BulkInsertDocumentsAsync(IEnumerable<object> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
            int batchSize = 1000, CancellationToken cancellation = default(CancellationToken));

        /// <summary>
        ///     Bulk insert a potentially mixed enumerable of document types
        /// </summary>
        /// <param name="documents"></param>
        /// <param name="mode"></param>
        /// <param name="batchSize"></param>
        Task BulkInsertDocumentsAsync(string tenantId, IEnumerable<object> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
            int batchSize = 1000, CancellationToken cancellation = default(CancellationToken));


        /// <summary>
        /// Use Javascript transformations to alter existing documents
        /// </summary>
        IDocumentTransforms Transform { get; }


        /// <summary>
        /// Build a new instance of the asynchronous projection daemon to use interactively
        /// in your own code
        /// </summary>
        /// <paramref name="Override the logger inside this instance of the async daemon"/>
        /// <returns></returns>
        IDaemon BuildProjectionDaemon(ILogger logger = null);


    }
}
