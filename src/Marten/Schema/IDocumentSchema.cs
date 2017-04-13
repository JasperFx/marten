using System;
using System.Collections.Generic;
using Marten.Events;
using Marten.Schema.BulkLoading;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Transforms;

namespace Marten.Schema
{
    public interface IDocumentSchema 
    {
        /// <summary>
        /// The original StoreOptions used to configure this DocumentStore
        /// </summary>
        [Obsolete("Move off of DocumentStore")]
        StoreOptions StoreOptions { get; }

        /// <summary>
        /// Retrieves or generates the active IDocumentStorage object
        /// for the given document type
        /// </summary>
        /// <param name="documentType"></param>
        /// <returns></returns>
        [Obsolete("Move to StorageFeatures")]
        IDocumentStorage StorageFor(Type documentType);

        /// <summary>
        /// Finds or creates the IDocumentMapping for a document type
        /// that governs how that document type is persisted and queried
        /// </summary>
        /// <param name="documentType"></param>
        /// <returns></returns>
        [Obsolete("Move to StorageFeatures")]
        IDocumentMapping MappingFor(Type documentType);

        /// <summary>
        /// Ensures that the IDocumentStorage object for a document type is ready
        /// and also attempts to update the database schema for any detected changes
        /// </summary>
        /// <param name="documentType"></param>
        [Obsolete("Move to StorageFeatures & Tenant")]
        void EnsureStorageExists(Type documentType);

        /// <summary>
        /// Used to create new Hilo sequences 
        /// </summary>
        [Obsolete("Move to StorageFeatures")]
        ISequences Sequences { get; }

        /// <summary>
        /// The event store configuration
        /// </summary>
        [Obsolete("Hang off of DocumentStore directly")]
        EventGraph Events { get; }


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


        string[] AllSchemaNames();

        [Obsolete("Move to StorageFeatures")]
        IDocumentStorage<T> StorageFor<T>();

        [Obsolete("Move to StorageFeatures")]
        IdAssignment<T> IdAssignmentFor<T>();


        TransformFunction TransformFor(string name);


        /// <summary>
        /// Directs Marten to disregard any previous schema checks. Useful
        /// if you change the underlying schema without shutting down the document store
        /// </summary>
        [Obsolete("Move to StorageFeatures and Tenant")]
        void ResetSchemaExistenceChecks();


        /// <summary>
        /// Query against the actual Postgresql database schema objects
        /// </summary>
        IDbObjects DbObjects { get; }

        [Obsolete("Move to StorageFeatures")]
        IEnumerable<IDocumentMapping> AllMappings { get; }

        /// <summary>
        /// Retrieve a configured IBulkLoader for a document type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [Obsolete("Move to StorageFeatures")]
        IBulkLoader<T> BulkLoaderFor<T>();

        /// <summary>
        /// Retrieve the IDocumentUpsert object for the given document type
        /// </summary>
        /// <param name="documentType"></param>
        /// <returns></returns>
        [Obsolete("Move to StorageFeatures")]
        IDocumentUpsert UpsertFor(Type documentType);

        /// <summary>
        /// Tries to write a "patch" SQL file to upgrade the database
        /// to the current Marten schema configuration. Also writes a corresponding
        /// rollback file as well.
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="withSchemas"></param>
        void WritePatch(string filename, bool withSchemas = true);

        /// <summary>
        /// Tries to write a "patch" SQL text to upgrade the database
        /// to the current Marten schema configuration
        /// </summary>
        /// <returns></returns>
        SchemaPatch ToPatch(bool withSchemas = true);

        /// <summary>
        /// Validates the Marten configuration of documents and transforms against
        /// the current database schema. Will throw an exception if any differences are
        /// detected. Useful for "environment tests"
        /// </summary>
        void AssertDatabaseMatchesConfiguration();

        /// <summary>
        /// Executes all detected DDL patches to the schema based on current configuration
        /// upfront at one time
        /// </summary>
        void ApplyAllConfiguredChangesToDatabase();

        [Obsolete("Move to StorageFeatures")]
        void EnsureFunctionExists(string functionName);


        /// <summary>
        /// Generate a DDL patch for one specific document type
        /// </summary>
        /// <param name="documentType"></param>
        /// <returns></returns>
        SchemaPatch ToPatch(Type documentType);

        void WritePatchByType(string directory);
    }
}