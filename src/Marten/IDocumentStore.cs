using System;
using System.Collections.Generic;
using System.Data;
using Marten.Events.Projections.Async;
using Marten.Schema;
using Marten.Transforms;

namespace Marten
{
    public interface IDocumentStore : IDisposable
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
        void BulkInsert<T>(T[] documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly, int batchSize = 1000);

        /// <summary>
        ///     Open a new IDocumentSession with the supplied DocumentTracking.
        ///     "IdentityOnly" is the default.
        /// </summary>
        /// <param name="tracking"></param>
        /// <returns></returns>
        IDocumentSession OpenSession(DocumentTracking tracking = DocumentTracking.IdentityOnly,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

        /// <summary>
        ///     Convenience method to create a new "lightweight" IDocumentSession with no IdentityMap
        ///     or automatic dirty checking
        /// </summary>
        /// <returns></returns>
        IDocumentSession LightweightSession(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

        /// <summary>
        ///     Convenience method to create an IDocumentSession with both IdentityMap and automatic
        ///     dirty checking
        /// </summary>
        /// <returns></returns>
        IDocumentSession DirtyTrackedSession(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

        /// <summary>
        ///     Opens a read-only IQuerySession to the current document store for efficient
        ///     querying without any underlying object tracking.
        /// </summary>
        /// <returns></returns>
        IQuerySession QuerySession();

        /// <summary>
        ///     Bulk insert a potentially mixed enumerable of document types
        /// </summary>
        /// <param name="documents"></param>
        /// <param name="mode"></param>
        /// <param name="batchSize"></param>
        void BulkInsertDocuments(IEnumerable<object> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
            int batchSize = 1000);


        /// <summary>
        /// Use Javascript transformations to alter existing documents
        /// </summary>
        IDocumentTransforms Transform { get; }



        IDaemon BuildProjectionDaemon(Type[] viewTypes = null, IDaemonLogger logger = null, DaemonSettings settings = null);
    }
}