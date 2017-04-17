using System;
using System.Collections.Generic;
using Marten.Storage;

namespace Marten.Schema
{
    public interface IDocumentSchema : ITenant
    {
        /// <summary>
        /// The original StoreOptions used to configure this DocumentStore
        /// </summary>
        [Obsolete("Move off of DocumentStore")]
        StoreOptions StoreOptions { get; }


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


        /// <summary>
        /// Query against the actual Postgresql database schema objects
        /// </summary>
        IDbObjects DbObjects { get; }

        [Obsolete("Move to StorageFeatures")]
        IEnumerable<IDocumentMapping> AllMappings { get; }


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