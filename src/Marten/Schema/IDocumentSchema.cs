using System;
using System.Collections.Generic;
using Marten.Events;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Schema.BulkLoading;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;

namespace Marten.Schema
{
    public interface IDocumentSchema 
    {
        /// <summary>
        /// The original StoreOptions used to configure this DocumentStore
        /// </summary>
        StoreOptions StoreOptions { get; }

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

        /// <summary>
        /// The event store configuration
        /// </summary>
        IEventStoreConfiguration Events { get; }

        /// <summary>
        /// Access to Linq parsing for adhoc querying techniques
        /// </summary>
        MartenExpressionParser Parser { get; }

        /// <summary>
        /// Write the SQL script to build the database schema
        /// objects to a file
        /// </summary>
        /// <param name="filename"></param>
        void WriteDDL(string filename);


        /// <summary>
        /// Write all the SQL scripts to build the database schema, but
        /// split by document type
        /// </summary>
        /// <param name="directory"></param>
        void WriteDDLByType(string directory);

        /// <summary>
        /// Creates all the SQL script that would build all the database
        /// schema objects for the configured schema
        /// </summary>
        /// <returns></returns>
        string ToDDL();


        IEnumerable<IDocumentMapping> AllDocumentMaps();
        string[] AllSchemaNames();

        IResolver<T> ResolverFor<T>();


        IdAssignment<T> IdAssignmentFor<T>();





            /// <summary>
        /// Used to create IQueryHandler's for Linq queries
        /// </summary>
        IQueryHandlerFactory HandlerFactory { get; }

        /// <summary>
        /// Directs Marten to disregard any previous schema checks. Useful
        /// if you change the underlying schema without shutting down the document store
        /// </summary>
        void ResetSchemaExistenceChecks();


        /// <summary>
        /// Query against the actual Postgresql database schema objects
        /// </summary>
        IDbObjects DbObjects { get; }

        IBulkLoader<T> BulkLoaderFor<T>();
        IDocumentUpsert UpsertFor(Type documentType);
    }
}