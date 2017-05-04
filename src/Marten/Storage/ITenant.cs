using System;
using Marten.Schema;
using Marten.Schema.BulkLoading;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Transforms;

namespace Marten.Storage
{
    public interface ITenant
    {
        /// <summary>
        /// Retrieves or generates the active IDocumentStorage object
        /// for the given document type
        /// </summary>
        /// <param name="documentType"></param>
        /// <returns></returns>
        IDocumentStorage StorageFor(Type documentType);

        /// <summary>
        /// Finds or creates the IDocumentMapping for a document type
        /// that governs how that document type is persisted and queried
        /// </summary>
        /// <param name="documentType"></param>
        /// <returns></returns>
        IDocumentMapping MappingFor(Type documentType);

        /// <summary>
        /// Ensures that the IDocumentStorage object for a document type is ready
        /// and also attempts to update the database schema for any detected changes
        /// </summary>
        /// <param name="documentType"></param>
        void EnsureStorageExists(Type documentType);

        /// <summary>
        /// Used to create new Hilo sequences 
        /// </summary>
        ISequences Sequences { get; }

        IDocumentStorage<T> StorageFor<T>();
        IdAssignment<T> IdAssignmentFor<T>();
        TransformFunction TransformFor(string name);

        /// <summary>
        /// Directs Marten to disregard any previous schema checks. Useful
        /// if you change the underlying schema without shutting down the document store
        /// </summary>
        void ResetSchemaExistenceChecks();

        /// <summary>
        /// Retrieve a configured IBulkLoader for a document type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IBulkLoader<T> BulkLoaderFor<T>();


    }
}