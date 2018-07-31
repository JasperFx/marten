using System;

namespace Marten.Schema
{
    public interface IDocumentSchema
    {
        DdlRules DdlRules { get; }

        /// <summary>
        ///     Write the SQL script to build the database schema
        ///     objects to a file
        /// </summary>
        /// <param name="filename"></param>
        void WriteDDL(string filename, bool transactionalScript = true);


        /// <summary>
        ///     Write all the SQL scripts to build the database schema, but
        ///     split by document type
        /// </summary>
        /// <param name="directory"></param>
        void WriteDDLByType(string directory, bool transactionalScript = true);

        /// <summary>
        ///     Creates all the SQL script that would build all the database
        ///     schema objects for the configured schema
        /// </summary>
        /// <returns></returns>
        string ToDDL(bool transactionalScript = true);


        /// <summary>
        ///     Tries to write a "patch" SQL file to upgrade the database
        ///     to the current Marten schema configuration. Also writes a corresponding
        ///     rollback file as well.
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="withSchemas"></param>
        void WritePatch(string filename, bool withSchemas = true, bool transactionalScript = true);

        /// <summary>
        ///     Tries to write a "patch" SQL text to upgrade the database
        ///     to the current Marten schema configuration
        /// </summary>
        /// <returns></returns>
        SchemaPatch ToPatch(bool withSchemas = true, bool withAutoCreateAll = false);

        /// <summary>
        ///     Validates the Marten configuration of documents and transforms against
        ///     the current database schema. Will throw an exception if any differences are
        ///     detected. Useful for "environment tests"
        /// </summary>
        void AssertDatabaseMatchesConfiguration();

        /// <summary>
        ///     Executes all detected DDL patches to the schema based on current configuration
        ///     upfront at one time
        /// </summary>
        void ApplyAllConfiguredChangesToDatabase();


        /// <summary>
        ///     Generate a DDL patch for one specific document type
        /// </summary>
        /// <param name="documentType"></param>
        /// <returns></returns>
        SchemaPatch ToPatch(Type documentType);

        void WritePatchByType(string directory, bool transactionalScript = true);
    }
}